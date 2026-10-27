# Leopard 2A4M for Gunner, HEAT, PC!

A **Leopard 2A4M** (War Thunder model) playable in **Gunner, HEAT, PC!**, complete with 28 individually
simulated add-on armour pieces, selectable 120 mm ammunition and a working commander's independent
thermal viewer.

- **Version**: 1.1.4
- **Author**: DDDDWWWWDZW
- **Game**: Gunner, HEAT, PC! (Unity 2022.3) + MelonLoader 0.6.2 (x64 / net35)
- **Donor unit**: `LEO2A3` — the 2A4-family prefab that carries the EMES-15 thermal sight

---

## Installation

1. Install **MelonLoader 0.6.2** (x64, net35) into the GHPC installation folder.
2. Download `Leo2A4M-<version>.zip` from the [Releases](../../releases) page and extract it so the
   layout becomes:

   ```text
   <GHPC>/Bin/Mods/Leo2A4M.dll
   <GHPC>/Bin/Mods/Leo2A4M/
       Leo2A4M_GHPC_1.2.obj
       Leo2A4M_GHPC_1.2.mtl
       materials.tsv
       parts.tsv
       textures/          (68 files)
   ```

3. Launch the game, pick **LEO2A4M** from the vehicle list. On a range map (TR01, TankRange, …) a
   second 2A4M spawns next to the player so it can be inspected or shot at.

---

## Features

### Add-on armour — 28 independent damage pieces

| Area | Pieces | KE / CE |
|---|---|---|
| Turret front wedges (`bone_t1/2/3`) | 3 | 140 / 200 |
| Hull front add-on plate (`hull_fupaa1`) | 1 | 50 / 300 |
| Turret sides (`hatch_06/07`) | 2 | 30 / 60 |
| Turret rear sides (`ex_armor_l/r_13`) | 2 | 60 / 200 |
| Turret mid sides (`ex_armor_l/r_11/12`) | 4 | 100 / 200 |
| Hull side skirts (`ex_armor_l/r_01..06`) | 12 | 50 / 150 |
| Slat / grille armour (hull 3 + turret 1) | 4 | 5 / 120 |

- Each piece is registered before the vehicle's `Awake`, placed on layer 8 (`Raycast Only`) with the
  `Penetrable` tag and a non-convex mesh collider, and gets its own runtime armour codex.
- They are **passive** add-on armour: hits leave decals and sparks, the pieces never detonate or
  disappear, and they show up in the AAR / x-ray view as internal structure.

### Ammunition

- Kinetic rounds: **KEW-A1** (default, 618 mm), **DM43** (580 mm), **DM53** (650 mm, ignores 30 % of ERA effect).
- Chemical round: the vanilla **DM12A1**.
- Round counts, rack layout and the visible round models are left entirely to the vanilla L2A3 logic —
  the mod only retunes the round itself.

### Commander's independent thermal viewer (CITV)

- Free observation by default — looking around does **not** move the turret.
- Hold **right mouse** to take over the turret/gun through the sight.
- Panning speed and the optional free-look key are configurable.

### Extras

- Hull front composite level: **b** (stock 300/600), **c** (reads 500/850), **d** (reads 592/960).
- Paint schemes: Bronzegrun / Sand / Tricolor.
- The auxiliary gunner sight no longer renders a black wall — the custom shell is hidden while that
  optic is in use.
- Automatic range spawn on TR01 / TankRange scenes.

---

## Configuration

`<GHPC>/Bin/UserData/MelonPreferences.cfg` → section `[Leo2A4M]`

| Entry | Default | Description |
|---|---|---|
| `WT Running Gear` | `false` | Use the War Thunder wheels/tracks instead of the animated vanilla gear |
| `Vanilla Running Gear` | `true` | Keep the animated vanilla running gear under the WT hull |
| `Take Control On Spawn` | `true` | Put the player in the spawned 2A4M immediately |
| `CITV` | `true` | Commander's independent thermal viewer |
| `Smooth CITV Panning` | `true` | Smoothed panning for the CITV |
| `CITV Aim Sensitivity` | `0.32` | CITV pan speed (`1` = untouched) |
| `CITV Free Look Key` | `LeftAlt` | Extra free-look key |
| `Paint Scheme` | `Bronzegrun` | Bronzegrun / Sand / Tricolor |
| `Hull Armour Level` | `c` | Hull front composite level `b` / `c` / `d` |
| `AP Ammo` | `kewa1` | `kewa1` / `dm43` / `dm53` |

---

## Building from source

```powershell
cd src
dotnet build -c Release
```

`Leo2A4M.csproj` locates the game assemblies through `GHPC_DIR`:

```xml
<GHPC_DIR>E:\SteamLibrary\steamapps\common\Gunner HEAT PC</GHPC_DIR>
```

Override it when building elsewhere:

```powershell
dotnet build -c Release /p:GHPC_DIR="C:\Path\To\Gunner HEAT PC"
```

Requires the .NET Framework 4.7.2 targeting pack and the publicized GHPC assemblies (handled by the
`BepInEx.AssemblyPublicizer.MSBuild` package reference).

---

## Repository layout

```text
src/       mod sources + csproj
assets/    model, materials.tsv, parts.tsv, textures/
docs/      design notes (kept out of the published tree)
```

---

## Credits & disclaimer

- Vehicle model and textures originate from **War Thunder** (Gaijin Entertainment).
- The game and its prefabs belong to **Gunner, HEAT, PC!** (Radian Simulations).
- This repository contains **no game files**; you need to own both games to use it.
- Released for modding and educational purposes. If a rights holder objects, contact the author and
  the content will be removed.
