# Leopard 2A4M — GHPC add-on mod

把 War Thunder 的 **Leopard 2A4M** 模型做成 **Gunner, HEAT, PC!** 的可玩单位，并附带可配置的附加装甲、弹药与车长独立热像仪。

A War Thunder-derived **Leopard 2A4M** for **Gunner, HEAT, PC!**, with add-on armour, configurable ammunition and a working commander's independent thermal viewer.

- **Version / 版本**: 1.1.4
- **Author / 作者**: DDDDWWWWDZW
- **Game**: GHPC (Unity 2022.3) + MelonLoader 0.6.2 (net35)
- **Donor unit / 母本**: `LEO2A3`（2A4 家族、带 EMES-15 热像仪的 prefab）

---

## Install / 安装

1. 安装 **MelonLoader 0.6.2**（x64，net35）到 GHPC 根目录。
2. 把以下内容放到游戏目录：

   ```text
   <GHPC>/Bin/Mods/Leo2A4M.dll
   <GHPC>/Bin/Mods/Leo2A4M/
       Leo2A4M_GHPC_1.2.obj
       Leo2A4M_GHPC_1.2.mtl
       materials.tsv
       parts.tsv
       textures/                 (68 张)
   ```

   或者直接解压 `Leo2A4M-1.1.4.zip` 到 `<GHPC>/Bin/Mods/`。

3. 启动游戏，在车辆选择里选 **LEO2A4M**；进靶场（TR01 等）会自动在玩家旁刷一辆方便观察。

---

## Features / 功能

### 附加装甲（28 块，独立损伤件）

| 区域 | 块数 | KE / CE |
|---|---|---|
| 炮塔正面楔形（`bone_t1/t2/t3`） | 3 | 140 / 200 |
| 车体首上附加板（`hull_fupaa1`） | 1 | 50 / 300 |
| 炮塔侧（`hatch_06/07`） | 2 | 30 / 60 |
| 炮塔侧后（`ex_armor_l/r_13`） | 2 | 60 / 200 |
| 炮塔侧中前（`ex_armor_l/r_11/12`） | 4 | 100 / 200 |
| 车体侧裙（`ex_armor_l/r_01..06`） | 12 | 50 / 150 |
| 隔栅（车体 3 + 炮塔 1） | 4 | 5 / 120 |

- 层 8 + `Penetrable`、非凸网格碰撞体、独立 codex（倍率 1）；
- 常驻装甲（命中不消失），留弹痕/火花，AAR 内构可见。

### 弹药

- 穿甲弹可选：**KEW-A1**（默认，618 mm）、**DM43**（580 mm）、**DM53**（650 mm，无视 30% 爆反）；
- 破甲弹：原版 **DM12A1**；
- 弹量、弹架布局、可见弹药模型完全沿用原版 L2A3 逻辑。

### 车长独立热像仪（CITV）

- 默认**自由观察**（炮塔不动），按住**右键**接管炮塔/火炮；
- 灵敏度、开关键位可配置。

### 其他

- 车体首上复合装甲等级可配：**b**（原版 300/600）、**c**（读数 500/850）、**d**（读数 592/960）；
- 涂装：Bronzegrun / Sand / Tricolor；
- 备用瞄准镜视角自动隐藏车壳，避免被模型遮挡；
- 靶场自动刷车（TR01 / TankRange 等场景）。

---

## Configuration / 配置

`<GHPC>/Bin/UserData/MelonPreferences.cfg` → `[Leo2A4M]`

| 配置项 | 默认 | 说明 |
|---|---|---|
| `WT Running Gear` | false | 用 WT 负重轮/履带（贴合车体） |
| `Vanilla Running Gear` | true | 保留原厂动画履带 |
| `Take Control On Spawn` | true | 生成后直接接管 |
| `CITV` | true | 车长独立热像仪 |
| `Smooth CITV Panning` | true | CITV 平滑转动 |
| `CITV Aim Sensitivity` | 0.32 | CITV 转动速度（1 = 原速） |
| `CITV Free Look Key` | LeftAlt | 额外自由观察键 |
| `Paint Scheme` | Bronzegrun | 涂装 |
| `Hull Armour Level` | c | 首上等级 b/c/d |
| `AP Ammo` | kewa1 | kewa1 / dm43 / dm53 |

---

## Build / 构建

```powershell
cd src
dotnet build -c Release
```

`Leo2A4M.csproj` 通过 `GHPC_DIR` 定位游戏程序集，默认：

```xml
<GHPC_DIR>E:\SteamLibrary\steamapps\common\Gunner HEAT PC</GHPC_DIR>
```

可覆盖：`dotnet build -c Release /p:GHPC_DIR="你的路径"`。

---

## Docs / 文档

- `docs/GHPC穿深衰减机制.md` —— 穿深随距离衰减的公式与实测数据（IL 级取证）
- `docs/addon_armor_appendix.md` —— 附加装甲实现的经验附录
- `docs/ammo_dump_20261027.txt` —— 游戏内实测的弹药弹道参数

---

## Credits & disclaimer / 致谢与声明

- 模型与贴图来自 **War Thunder**（Gaijin Entertainment）；
- 游戏本体与 prefab 来自 **Gunner, HEAT, PC!**（Radian Simulations）；
- 本仓库仅用于模组制作与学习交流，**不包含任何游戏本体文件**；使用前请自行合法拥有对应游戏。

若相关权利人认为有不妥，请联系仓库作者处理。
