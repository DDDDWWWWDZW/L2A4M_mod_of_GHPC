# Leopard 2A4M CAN (WT visual swap) for GHPC

Private personal mod for **Gunner, HEAT, PC!** — swaps the vanilla Leopard 2 for a
War Thunder-derived **Leopard 2A4M CAN** (2A4 hull/turret with the M-package add-on
armour, cage armour, camo nets and the independent commander thermal sight).

- Version: **0.4.1**
- Author: DDDDWWWWDZW
- Unity: GHPC (Unity 2022.3.x) + MelonLoader 0.6.2 (net35)
- Donor unit: **LEO2A3** (the 2A4-family prefab that carries the EMES-15 thermal sight)

> The model and textures are Gaijin assets from War Thunder. Private use only —
> do not redistribute or publish.

## Install

1. Copy `Leo2A4M.dll` to:

   ```text
   <GHPC>/Bin/Mods/Leo2A4M.dll
   ```

2. Copy the whole `Leo2A4M/` asset folder to:

   ```text
   <GHPC>/Bin/Mods/Leo2A4M/
   ```

   The folder must contain:

   ```text
   Leo2A4M_GHPC.obj
   Leo2A4M_GHPC.mtl
   materials.tsv      material -> textures
   parts.tsv          object -> rig anchor, original order, tri count
   citv_monitor
   textures/
   ```

3. Start GHPC. **Leopard 2A4M** is listed in the **Customizer** (German / Blue).

## How it works

- On load the mod registers a new unit `LEO2A4M` by cloning the `UnitPrefabMetadata`
  of a Leopard 2 donor (`LEO2A3` first, then 2A2 / 2A1 / 2 / 2A0 / 2A4). Gun,
  ammunition, fire control, damage model, suspension and running gear stay vanilla.
- On spawn, `Convert()` hides every vanilla renderer except the animated running
  gear, the track skinned meshes and the interior (whitelist), then attaches **all
  127 WT objects** separately, each parented to the rig transform its anchor names:

  | anchor | parent transform |
  |---|---|
  | Hull | vehicle root |
  | Turret | `leo2_rig/hull/turret` |
  | Gun | `leo2_rig/hull/turret/mantlet` |
  | RunningGear | vehicle root (only when the WT gear pref is on) |

  Every GameObject keeps its **original WT name** (`Leo2A4M vehicle#ex_armor_l_01_9`),
  so a single armour block can be found and given armour values / AAR visuals later
  without touching the mesh.
- Meshes are parsed from the OBJ at runtime (`ObjLoader`), vertices already in
  vehicle space (+X right, +Y up, +Z forward). `parts.tsv` supplies the anchor.
- **CITV is kept.** The WT `optic_commander` head sits at vehicle
  `(0.83, 2.70, 0.95)`; `CITV.Setup()` moves the donor's `commander head/NODs`
  camera slot onto that point and attaches the gen-1 imager (`Citv.cs`).
- The gunner / thermal cameras are lifted in front of the WT add-on armour
  (`Gunner Sight *` prefs), otherwise the pictures start inside the armour block.
- `LEO2A4M_AUTOTEST=1` before launch makes the mod load `TankRange01_Scene`, spawn
  the tank, take the QC screenshots into `Bin/Mods/Leo2A4M/shot_*.png`, write
  `leodebug.txt` and quit. Development only.
- On a range scene the mod auto-spawns one test unit, offset **10 m to the left and
  5 m forward** of the spot the Leopard 2A5 mod uses (both mods hook the same
  `PlayerReady` trigger with the same 18 m / 9 m offsets).

## Calibration (measured, not guessed)

The WT 2A4M export does **not** share the 2A5 export's origin, so everything was
re-measured with `tools/ring_fit.py` and `tools/analyze_2a4m.py`:

| reference | 2A5 export | 2A4M export | GHPC rig (`LEO2A3`) |
|---|---:|---:|---:|
| turret ring centre (fwd) | 0.379 (r 0.977) | **0.342 (r 0.999)** | 0.816 |
| road wheel centre height | 0.4245 | **0.4245** | 0.402 |
| road wheel 1 (fwd) | 2.303 | 2.511 | 2.739 |
| road wheel 7 (fwd) | -2.641 | -2.444 | -2.187 |

=> `BaseY = -0.02` (wheel centre height, same as the 2A5 build)
=> `BaseZ = 0.819 - 0.342 = 0.477` (turret ring on the pivot)

The ring is the reference that matters: it is what keeps the turret on the hull
during traverse. The model's own running gear sits ~0.24 m further forward
relative to its ring than the vanilla gear does — see limitations.

## Configuration

`<GHPC>/Bin/UserData/MelonPreferences.cfg`, section `[Leo2A4M]`:

```ini
"Hull Offset X/Y/Z (Leo2A4M)" = 0
"Turret Offset X/Y/Z (Leo2A4M)" = 0
"Gun Offset X/Y/Z (Leo2A4M)" = 0
"Model Scale (Leo2A4M)" = 1
"Vanilla Running Gear (Leo2A4M)" = true
"Paint Scheme (Leo2A4M)" = "Bronzegrun"
"Texture Saturation (Leo2A4M)" = 1
"Texture Mip Bias (Leo2A4M)" = 0
"Texture Brightness (Leo2A4M)" = 1.15
"Texture Contrast (Leo2A4M)" = 0.98
"Texture Clean (Leo2A4M)" = 0
"Texture Green Strength (Leo2A4M)" = 0
"CITV (Leo2A4M)" = true
"CITV X (Leo2A4M)" = 0.55
"CITV Y (Leo2A4M)" = 2.55
"CITV Z (Leo2A4M)" = 2.75
"CITV Aim Sensitivity (Leo2A4M)" = 0.4
"CITV Resolution (Leo2A4M)" = 1280
"Gunner Sight X (Leo2A4M)" = 0.55
"Gunner Sight Y (Leo2A4M)" = 2.55
"Gunner Sight Z (Leo2A4M)" = 2.75
"Gunner Sight Gen-2 Thermal (Leo2A4M)" = true
"Gunner Thermal Resolution Scale (Leo2A4M)" = 2.5
"Gunner Thermal Contrast (Leo2A4M)" = 1.9
"Gunner Thermal Brightness (Leo2A4M)" = 1.5
"Gunner Thermal Scanline Brightness (Leo2A4M)" = 0
```

Offsets are metres in vehicle space and are the knob for realigning the body if a
game patch moves the rig.

### Thermal generation

The donor (LEO2A3) only carries the first-generation EMES-15 imager. The 2A4M's
gunner sight is second generation, so `SetupGunnerThermal()` replaces the donor's
look on a **private copy** of the FLIR blit material (the donor material is shared
with every other Leopard in the scene):

| knob | donor (gen-1) | 2A4M (gen-2) |
|---|---|---|
| FLIR buffer | 480x240 | **1200x600** (`Resolution Scale` 2.5) |
| pixel cookie (chunky pixel grid) | on | **off** |
| wobble / blur | small | **0** |
| colour ramp exponent | 1.5 | **1.9** |
| exposure | 1.0 | **1.5** |
| scanlines | 0.1 | **0** |

Set `Gunner Sight Gen-2 Thermal = false` to get the donor picture back. If the
picture reads too dark, raise `Brightness`; if washed out, raise `Contrast`.

### CITV aiming

`Citv.cs` is the 2A5 file (same sight logic), but the 2A5's own sensitivity knob never
actually reached the optic: `CameraFollow.aimSensitivity3P` only feeds the third-person
camera, and `freeCamSpeedSensitivity` turned out to be the wrong axis as well.

The pan is applied through **`BufferedCameraFollow.TraverseBy` / `ElevateBy`**, so the
mod puts a Harmony prefix on both and multiplies the incoming distance by
`CITV Aim Sensitivity` **while the player is in the CITV**:

| pref | effect |
|---:|---|
| 1.0 | unchanged |
| **0.55** (default) | 45 % slower |
| 0.35 | 65 % slower |
| 0.2 | 80 % slower |

The rest of the time the prefix is inert (flag is false). The log prints
`CITV active=.. pref=.. scale=.. scaledCalls=..` every 10 s — `scaledCalls` climbing
while you pan in the sight is the proof the value is being applied.

### Net colour

The WT export ships two **brown** net sheets (`brown_camo_net_b/c`) that read as tan
tarpaulin next to the bronzegruen body. `tools/tint_nets.py` rewrites them: albedo
re-hued with the body sheet's mean colour (rgb 81,78,67 -> net ~89,84,71), brightness
re-levelled to the body, specular scaled to 45 % so the net stops reading as plastic
up close. `materials.tsv` is repointed at `*_green.jpg` / `*_n_s_soft.jpg`. It is
idempotent and is **step 2** of the pipeline (prep rewrites `materials.tsv`).

## Workflow rule: check offline first

```powershell
E:\Blender 4.5\blender.exe -b -P tools/prep_2a4m.py   # rebuild assets + previews
python tools/tint_nets.py                             # green nets, soft specular
python tools/parts_table.py                           # refresh parts.tsv
python tools/check_assets_2a4m.py                     # per-anchor gate, split leaks,
                                                      # materials/textures
```

Only when that prints `RESULT: PASS` does the DLL get built and exercised with
`LEO2A4M_AUTOTEST=1`. Close the game before replacing `Bin/Mods/Leo2A4M.dll` —
overwriting it while GHPC is running makes the mod fail to load (no unit, no spawn).

## Asset pipeline

`tools/prep_2a4m.py` (Blender 4.5, headless) does the whole conversion:

- imports `source/Leopard_2A4M/Leopard_2A4M.obj` (127 objects, 262,560 tris)
- **keeps every object as its own part.** The add-on armour blocks, the cage panels
  and the camo nets stay individually addressable: merging them into one
  Hull/Turret/Gun mesh (what the 2A5 pipeline did) makes per-piece armour values and
  AAR interiors impossible later. Nothing is joined — each object is only transformed,
  mirrored and smoothed.
- assigns each part an **anchor** (Hull / Turret / Gun / RunningGear):
  - the WT "seek & destroy" export writes objects in scene-graph order, so everything
    after `bone_turret` belongs to the turret subtree, plus a roof-height override for
    `ammo` / `antenna_04` (exported in front of the anchor)
  - name rules: `gun_barrel|bone_gun|gun_mask|mg_gun_twin` -> Gun;
    `wheel|suspension|track` -> RunningGear
  - the coaxial MG (`bone_mg_gun_twin`) rides the **Gun** part so it elevates with the
    barrel; the roof MG (`bone_mg_aa_v_01`) stays on the Turret
- closes holes only on the base shells (`x_root`, `bone_turret`, gun parts). The WT
  hull already has a floor, so **no belly plate is added** — the 2A5-era convex plate
  came out wider than the real floor and poked out under the nose. It is opt-in
  (`prep_2a4m.py --belly`) for shells that really are open underneath
- mirrors mesh-local X, flips the winding, shade-smooth 30deg, exports every object as
  a separate `o` block under its **original WT name**
- writes `parts.tsv`, `materials.tsv`, `manifest.json` and the preview renders

`parts.tsv` is the record of the original structure — one row per object:

```text
name                            anchor  order  tris  base_shell
vehicle#x_root_86               Hull    86     62416 1
vehicle#ex_armor_l_01_9         Hull    9      815   0
vehicle#bone_turret_88          Turret  88     42232 1
vehicle#bone_mg_gun_twin_111    Gun     111    2451  0
```

`anchor` = the rig transform the part is parented to, `order` = index in the WT
scene-graph order (the OBJ itself is flat), `base_shell` = closed shell that got hole
filling / belly plate. `tools/parts_table.py` regenerates the table from
`manifest.json` without a Blender run.

Totals: **Hull 44 parts / 125,166 · Turret 36 / 99,855 · Gun 5 / 14,796 ·
RunningGear 42 / 35,956 tris**.

Helper scripts: `analyze_2a4m.py` (per-object audit + split preview, no Blender),
`ring_fit.py` (turret ring fit), `verify_split_2a4m.py` (turret rotated about its own
ring), `verify_elevation_2a4m.py` (gun elevated about the trunnion), `obj_parts.py`.

## Materials

All opaque parts are built on **`GHPC/VehicleShader`**, the shader the game uses for
its own tanks, with the WT maps wired into its slots:

| slot | WT source |
|---|---|
| `_Albedo` | `_c` colour sheet (+ `_a` mask folded into alpha for glass/nets) |
| `_Normal` | `_n_n` normal map |
| `_Smoothness` | `_n_s` specular sheet |
| `_Occlusion` | `_ao` ambient occlusion |

The 2A4M's camo nets are **real geometry** with no alpha mask (28k + 18k + 12k tris),
so they stay opaque and need no sorting; two small net materials and the glass use
alpha cutout.

## Known limitations (0.4.1)

- **Running gear offset.** Relative to its own turret ring the WT model carries its
  wheels ~0.24 m further forward than the vanilla chassis does. The mod aligns by the
  ring (that is what keeps the turret on the hull during traverse), so the vanilla
  wheels sit slightly aft of the WT wheel arches.
- **Barrel length.** The WT 2A4M barrel ends at z 5.54, the donor muzzle is at 6.17 —
  about 15 cm short. Cosmetic only (the muzzle/ammo origin is vanilla).
- **Draw calls.** 85 attached pieces (127 with the WT running gear) means roughly 150
  draw calls instead of 25 for a merged mesh. That is the price of keeping the pieces
  addressable; a merged "performance" variant can be exported as well if wanted.
- The QC harness camera sometimes shows a bright/dark arc in the sky. That is the
  harness rendering the game's sky through a plain `Camera`, not the mod geometry.

## Layout

```text
assets/        runtime assets (OBJ, MTL, materials.tsv, parts.tsv, textures, citv_monitor)
dist/          packaged release zip
preview/       Blender previews + in-game QC renders
release/       built Leo2A4M.dll
source/        the WT export this was built from
src/           C# source (Main.cs, Citv.cs, ObjLoader.cs, Leo2A4M.csproj)
tools/         Blender / OBJ analysis scripts
```
