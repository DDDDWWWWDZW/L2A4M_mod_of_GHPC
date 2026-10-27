
---

# 附录 A · 2026-10-27 实战补充：Leopard 2A4M 非 ERA 附加复合装甲

> 来源：GHPC-L2A4M 模组（`E:\Claude_code_work\test111\GHPC_MOD\L2A4M`），四块附加件：
> 炮塔正面三块楔形 + 车体首上一块，全部是**普通附加复合装甲**（非爆反，命中不消失），
> 数值 100/200 与 50/300，最终在游戏里做到"命中 + 按值结算 + AAR 内构可见 + 位置贴合"。
> 本节只写**上一版指南没覆盖、或写错**的部分。

## A.1 时机：`Vehicle.Awake` 钩子为什么"从没触发过"

上一版指南写的 `[HarmonyPatch(typeof(Vehicle), "Awake")]` 在 LEO2A4M 上**一次都没执行**，
日志里一行 `pre-Awake` 都没有——不是时机不重要，而是 **Unity 只调用最派生的 `Awake`**。
如果子类（例如具体的 `Vehicle` 派生类型）重写了 `Awake` 且没有 `base.Awake()`，挂在基类上的
Harmony Prefix 永远不会跑。

正确写法（扫描整条 `Unit` 继承链上所有声明的 `Awake`，逐个 patch）：

```csharp
[HarmonyPatch]
internal static class UnitAwakePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var found = new List<MethodBase>();
        var asm = typeof(Unit).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (!typeof(Unit).IsAssignableFrom(t) || t.IsAbstract) continue;
            for (var cur = t; cur != null && typeof(Unit).IsAssignableFrom(cur); cur = cur.BaseType)
            {
                var m = cur.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null && !found.Contains(m)) found.Add(m);
            }
        }
        MelonLogger.Msg("Awake hooks: " + found.Count);   // 这个数字必须 > 0
        return found;
    }

    private static void Prefix(Unit __instance)
    {
        if (!SpawnHook.Pending) return;
        var vic = __instance as Vehicle;
        if (vic == null) return;
        SpawnHook.Pending = false;
        AddOnArmour.Build(vic);        // 先建装甲件，再让游戏建 HitZone
    }
}
```

同时保留 `Convert()` 之后的**幂等兜底**（用名字查一次，已在就跳过），这样即使某条刷车路径
绕过了 `SpawnUnit`，件也不会缺：

```csharp
internal static void EnsureBuilt(Vehicle vic)
{
    if (vic.transform.Find("A4M addon vehicle#bone_t2") != null) return;
    Build(vic);
}
```

## A.2 父级与坐标：两个会让装甲"飞天 / 内缩"的坑

1. **飞天**：WT 模型顶点是**车体空间**坐标。把包围盒中心直接赋给 `localPosition` 会飞到天上。
   正确链：`vehicle → world（vic.transform.TransformPoint）→ 父物体局部`。
2. **内缩**：挂到 donor 装甲板（`cheek array left A` 这类）下会**继承该板的缩放**，
   网格被一起压小——形状对、位置偏内，AAR 里一眼能看出来。

处理方式：

- 优先挂真正的装甲根（见 A.3），不要挂 donor 板；
- 若必须挂带缩放的父级，`localScale = 1 / parent.lossyScale`（逐轴取倒数）；
- **终极兜底（推荐）**：转换完成后，把装甲件的变换**直接对齐同一份网格的可见件**。
  两者是同一个 OBJ 部件，变换一致就必然重合，还能跟随炮塔/火炮运动：

```csharp
internal static void AlignToVisuals(Vehicle vic)
{
    foreach (var tr in vic.GetComponentsInChildren<Transform>(true))
    {
        if (!tr.name.StartsWith("A4M addon ")) continue;          // 判定件
        string part = tr.name.Substring("A4M addon ".Length);
        var vis = FindByName(vic, "Leo2A4M " + part);              // 可见件
        if (vis == null) continue;
        tr.SetParent(vis.parent, true);      // 跟随同一锚点（turret / mantlet / hull）
        tr.position = vis.position;
        tr.rotation = vis.rotation;
        tr.localScale = vis.localScale;
    }
}
```

日志里打印 `was (x,y,z) now (x,y,z)`，一眼能看出之前偏了多少。

## A.3 审计样例：LEO2A4 donor 的真实层级（可直接抄名字）

```
LEO2A4 HULL COLLIDERS/LEO2_armour_hull                          layer=8 tag=Penetrable   <- 车体装甲根
LEO2A4 TURRET COLLIDERS/LEO2_armour_turret                      layer=8 tag=Penetrable
LEO2A4 TURRET COLLIDERS/LEO2_armour_turret/turret armour        layer=8 tag=Penetrable   <- 炮塔装甲根
LEO2A4 TURRET COLLIDERS/LEO2_armour_turret/turret armour/array left/inverted armor clone   layer=20
LEO2A4 HULL COLLIDERS/LEO2_armour_hull/upper glacis array/inverted armor clone             layer=20
```

两个关键结论：

1. **`layer 20` 的 `inverted armor clone` 子对象是 AAR/内构的来源**（内壁翻面渲染）。
   自己运行时新增的件**不会**自动获得这个 clone，需要自己做 AAR 表现（见 A.4）。
2. donor 复合包 / 结构板的名字可以直接当挂载点或对照用：
   `upper glacis array`、`lower glacis array`、`front left/right array`、`hull floor`、
   `cheek array left A`、`cheek array right A`、`cheek array right B`、
   `upper front plate`、`gun breech`、`front fender left/right`、`front left/right plate`。

审计代码建议直接落盘成 `armour_audit.txt`（`LateFollowTargets` + 每个装甲件的
`layer / tag / collider 类型 / 路径`），这是后面所有挂载决策的唯一依据。

## A.4 AAR / 内构：`AarVisual` 的可用字段

`GHPC.AarVisual` 里确认存在 `HideUntilAar`、`SwitchMaterials`、`AarMaterial` 三个字段。
给自制件加内构显示：

```csharp
var type = typeof(UniformArmor).Assembly.GetType("GHPC.AarVisual");
var comp = go.GetComponent(type) ?? go.AddComponent(type);
SetField(comp, "HideUntilAar", true);
SetField(comp, "SwitchMaterials", true);
SetField(comp, "AarMaterial", FindAnyAarMaterial(vic));   // 复用车上已有件的材质
```

- **渲染器保持 enabled**：件在 layer 8，主相机不会画它，所以不会和外观模型重影；
  AAR 通道会按 `AarVisual` 画出来。
- `AarMaterial` 从车上任意一个已有 `AarVisual` 里取第一个非空材质，保证和内构件同观感。
- 若要有原厂那种"内壁翻面"效果，可以再自己克隆一份网格、翻转法线、放 `layer 20`，
  命名 `inverted armor clone`（与 donor 一致，便于后续工具识别）。

## A.5 数值承载（非 ERA 版本）

- `UniformArmor.PrimarySabotRha / PrimaryHeatRha` 写**绝对 mm 值**；
- `ArmorType{ ThicknessSource = Multipliers, RhaeMultiplierKe = 1, RhaeMultiplierCe = 1 }`；
- `_isEra = false`（不能被口径门槛引爆）；`DetonatedObjects = new GameObject[0]`、
  `UndetonatedObjects = { go }`（命中不消失）；
- 参考值：炮塔楔形 100 KE / 200 CE，车体首上附加板 50 KE / 300 CE。

> 注意：不要试图用"20 mm 薄板 + 倍率"去凑数值——那条路只在特定网格上偶然成立；
> 用真实网格 + 倍率 1 才是可重复的做法。

## A.6 配置表模式（多块维护）

四块件用一张表描述，新增部件只加一行：

```csharp
private sealed class Piece { internal string Match, Host; internal float Ke, Ce; }

private static readonly Piece[] Pieces =
{
    new Piece("bone_t1",     "gun breech",          100f, 200f),
    new Piece("bone_t2",     "cheek array left A",  100f, 200f),
    new Piece("bone_t3",     "cheek array right A", 100f, 200f),
    new Piece("hull_fupaa",  "upper glacis array",   50f, 300f),
};
```

配套：`parts.tsv` 里也登记同名部件（锚点 `Hull` / `Turret` / `Gun`），让它作为**可见件**
走正常装配流程；判定件单独一份、命名 `A4M addon <obj名>`，二者靠 A.2 的对齐逻辑重合。

## A.7 失败模式速查（本节新增）

| 症状 | 根因 | 修复 |
|---|---|---|
| 日志里完全没有 pre-Awake 输出 | 只 patch 了基类 `Vehicle.Awake` | `TargetMethods()` 扫描 Unit 继承链所有 `Awake` |
| 装甲件"往内缩"、AAR 里能看出偏内 | 挂在 donor 板下，继承了它的缩放 | 挂 ARMOR 根 + `1/lossyScale`，或直接对齐可见件变换 |
| 装甲件飘在天上 | 车体空间坐标当成父级局部坐标 | `TransformPoint` → 父局部，或对齐可见件 |
| 显示 50 KE 却打不穿 | 把楔形网格本身当判定体，厚度≈600 mm 被乘进去 | 用真实网格 + 倍率 1，或薄板 + 显式厚度 |
| 打不穿 + AAR 回放打不开 | 非凸网格碰撞体挂在带 UniformArmor 的物体上但构造不全 | 补齐 codex / Detonated / Undetonated / layer / tag；对齐后再测 |
| AAR 里看不到自制件 | 没有 `AarVisual`，或渲染器被关掉 | 加 `AarVisual`（A.4），渲染器保持 enabled |

## A.8 落地顺序（非 ERA 附加件）

1. 审计 → 写 `armour_audit.txt`，确认 ARMOR 根与 donor 件名；
2. 新模型部件登记进 `parts.tsv`（可见件）；
3. 配置表写四块（Match / Host / KE / CE）；
4. `Unit.Awake` 全链 patch + `SpawnUnit` Prefix 置 Pending，Awake 前 `Build()`；
5. `Convert()` 之后 `EnsureBuilt()` + `AlignToVisuals()` 兜底对齐；
6. 加 `AarVisual`，复用整车 AAR 材质；
7. 验证：命中名称 → `nominal` 数值 → 能否击穿 → AAR 位置/内构 → 连续命中不消失。
