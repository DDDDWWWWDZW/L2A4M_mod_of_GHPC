# GHPC 穿深随距离衰减机制

> 记录时间：2026-10-27 · 适用版本：GHPC（Unity 2022.3 / Assembly-CSharp）
> 方法：直接反汇编 `Assembly-CSharp.dll` 的 IL（未加载程序集），再用模组运行时 dump 的真实弹药参数做数值验证。
> 工具：`L2A4M\tools\metadump`（自写元数据 + IL 反汇编器）；数据：`Bin\Mods\Leo2A4M\ammo_dump.txt`。

---

## 1. 一句话结论

**穿甲弹的实际穿深 = 名义穿深 × (落速 / 初速)² × 章动修正**；
速度按**二次阻力**衰减，因此穿深随距离近似**指数下降**，指数系数是 `2·k`，其中

```
k = Coeff · SectionalArea · AirDensity / Mass
```

弹越重（Mass 越大），k 越小，远距离保留越多——这就是"重弹存速好"的真实来源。

---

## 2. 三段公式与 IL 证据

### 2.1 初速（`MainGun::doShot`）

```csharp
v0 = MuzzleVelocity * muzzle.forward + 车体刚体速度;
CurrentSpeed = |v0|;
MaxSpeed     = |v0|;
```

即炮口初速里**叠加了车辆自身速度**，行进中开火的初速与静止时略有差异。

### 2.2 速度衰减（`OldLiveRound::accelerate`）

```csharp
a = gravity
  + (-1) * (Coeff * SectionalArea * AirDensity / Mass) * |v|² * v̂;
```

标准二次阻力。`AirDensity` 来自 `WorldScript.LocalAirDensity`，**默认 1.2 kg/m³**（构造函数里的 `ldc.r4 1.2`）。

### 2.3 穿深（`GHPC.Weapons.LiveRound::get_CurrentPenRating`，`OldLiveRound` 同名方法一致）

```csharp
float basePen = (RhaPenetrationOverride > 0) ? RhaPenetrationOverride : Info.RhaPenetration;

if (_isHeat)                       // 化学能弹走另一条分支（见 §5）
{
    if (!_armed) return Info.MaxSpallRha;
    if (!_jetActive) return basePen;
}

float ratio = (CurrentSpeed * CurrentSpeed) / (MaxSpeed * MaxSpeed);     // (v/v0)²
float dist  = _timeTraveled * Info.MuzzleVelocity;                       // 近似飞行距离
if (dist < Info.NutationPenaltyDistance && !IsSpall)
{
    float t = (NutationPenaltyDistance - dist) / NutationPenaltyDistance;
    ratio *= (1f - 0.07f * t);       // 出膛段最多 −7%
}
return basePen * ratio;
```

要点：

- 穿深与**速度比的平方**成正比 —— 速度掉 10%，穿深掉约 19%；
- `dist` 不是真实位移，而是 `飞行时间 × 炮口初速`（省算力的近似），所以远距离时略微低估距离；
- 章动修正 `NutationPenaltyDistance` 通常只有 100 m，**只在炮口附近生效**，最大 −7%，过该距离后归零。

---

## 3. 闭式解：指数衰减

忽略重力对速度的次要影响（平射），令

```
k = Coeff · SectionalArea · AirDensity / Mass        [1/m]
```

二次阻力方程 `dv/dt = −k·v²` 的解：

```
v(x) = v0 · e^(−k·x)
```

代入穿深公式：

```
pen(x) = pen₀ · e^(−2k·x)          （x > NutationPenaltyDistance 时）
```

**每公里保留比例 = e^(−2k·1000)**。这是一个非常干净的结论：远距离穿深完全由 k 决定，而 k 又只由「弹的形状（Coeff·A）/ 质量 / 空气密度」三者决定。

---

## 4. 实测参数与衰减表

`ammo_dump.txt`（进图后由模组写出）给出的原版 **DM23 模板**参数：

| 字段 | 值 |
|---|---|
| `Coeff` | 0.30 |
| `SectionalArea` | 0.000620 m² |
| `NutationPenaltyDistance` | 100 m |
| `AirDensity` | 1.2 kg/m³ |

我们三发弹的 k：

| 弹种 | 质量 | k [1/m] | 1 km 速度 |
|---|---|---|---|
| KEW-A1 | 4.0 kg | 5.58×10⁻⁵ | 1646 m/s |
| DM43 | 4.0 kg | 5.58×10⁻⁵ | 1608 m/s |
| DM53 | 5.0 kg | 4.46×10⁻⁵ | 1597 m/s |

按 `pen(x) = pen₀·e^(−2kx)` 计算（0 m 列含 −7% 章动惩罚）：

| 弹种 | 名义 | 0 m | 100 m | 500 m | 1 km | 2 km | 3 km | 4 km |
|---|---|---|---|---|---|---|---|---|
| KEW-A1 | 618 | 575 | 611 | 584 | **553** | 494 | 442 | 395 |
| DM43 | 580 | 539 | 574 | 549 | **519** | 464 | 415 | 371 |
| DM53 | 650 | 604 | 644 | 622 | **594** | 544 | 497 | 455 |

DM53 比 DM43 重 25%，k 小 20%，4 km 时仍保留 455 mm，而 DM43 只剩 371 mm。

---

## 5. 化学能弹（HEAT）不受距离影响

`get_CurrentPenRating` 里 HEAT 走早期返回：

- 未解脱引信：返回 `MaxSpallRha`（用于破片/流弹判定）；
- 已解脱但金属射流未激活：返回 `RhaPenetration`；
- 其余情况才进入速度比公式。

所以对 **DM12A1 这类 HEAT**：穿深是固定值（实测 560 mm），与距离无关，只受入射角、爆反、复合装甲优化影响。`Coeff / Mass / MuzzleVelocity` 只影响**弹道飞行**，不影响穿深。

---

## 6. 对自制弹药的启示

1. **想让远距离更强**：降低 `Coeff`，或提高 `Mass`，或两者一起。举例：给 DM53 单独设 `Coeff = 0.26`，1 km 保留率从 91.5% 提升到约 92.8%，4 km 由 455 升到约 471。
2. **不要指望改 `RhaPenetration` 解决远距离**：它只缩放整条曲线（指数项的系数），曲线形状由 k 决定。
3. **炮口段 −7%**：如果希望近距离完全按名义值结算，把 `NutationPenaltyDistance` 设为 0。
4. **HEAT 的"数值"是硬值**：调 `RhaPenetration` 直接生效，与距离无关。

---

## 7. 复现与工具

### 7.1 弹药参数 dump（模组自带）

进图后自动写 `Bin\Mods\Leo2A4M\ammo_dump.txt`：列出当前载入清单里每种弹的全部弹道字段（Mass / MuzzleVelocity / RhaPenetration / Coeff / SectionalArea / NutationPenaltyDistance / Spall* / ArmorOptimizations），并附公式注释。

### 7.2 IL 反汇编（`tools\metadump`）

```powershell
# 列出 AMMO 类型字段/方法
dotnet tools\metadump\bin\Release\net7.0\metadump.dll <Assembly-CSharp.dll> "AmmoType"

# 反汇编某个方法
dotnet tools\metadump\bin\Release\net7.0\metadump.dll <dll> "IL:GHPC.Weapons.LiveRound::get_CurrentPenRating"

# 找出谁引用了某个字段（例如谁读 RhaPenetration）
dotnet tools\metadump\bin\Release\net7.0\metadump.dll <dll> "X:AmmoType.RhaPenetration"
```

该工具只读元数据、不加载程序集，所以不需要 UnityEngine 依赖。

### 7.3 相关代码位置（IL 级）

| 行为 | 类型 :: 方法 |
|---|---|
| 初速与 MaxSpeed | `MainGun::doShot` |
| 阻力加速度 | `OldLiveRound::accelerate` / `GHPC.PhysicsHelpers.BallisticEvaluatorRK4::Accelerate` |
| 穿深计算 | `GHPC.Weapons.LiveRound::get_CurrentPenRating`（旧版 `OldLiveRound::get_CurrentPenRating`） |
| 空气密度 | `WorldScript.get_AirDensity` → `LocalAirDensity`（默认 1.2） |
| 弹道表（火控用） | `GHPC.BallisticComputer::GetVelocity/GetRange/GetFlightTime` |
