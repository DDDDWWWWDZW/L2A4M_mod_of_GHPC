# Changelog

## 0.1.40

- The CITV is now a **registered optic** and the trim is switched off by default (it would
  otherwise bias the very solution the fire control now computes for this sight).

## 0.1.36 - 0.1.39: giving the commander sight a real optic identity

Symptom: firing from the CITV always landed a constant angle below the crosshair (measured
20-28 px at 350 m *and* 950 m), while firing from the gunner sight was accurate.

What the telemetry established, step by step:

1. `AIMPROBE` (0.1.32) — `camVsFcs = 0.000`: the camera is rigidly slaved to the fire control's
   aim vector. Any aim-point push therefore rotates the *view* with it and cancels out; this is
   why every compensation attempt in 0.1.13-0.1.31 changed nothing.
2. Dump of the vehicle's optics (0.1.35/0.1.36) — the gunner sights (`FERO Z18`, `GPS/Optic`,
   `GPS/FLIR`) are `UsableOptic`s with an `FCS` and an `Alignment`; the commander slot
   (`commander head` -> `NODs`) has **none**, so the fire control never solves through it.
   That is the real difference from the M1A2C mod, whose CITV rides on
   `vic.DesignatedCameraSlots[0].LinkedNightSight`, i.e. a slot that already is a real optic.
3. 0.1.37 added a `UsableOptic` bound to the gun's FCS, but registration silently failed
   (`slots=3`).
4. 0.1.38 tried `_designatedCameraSlots` — still not found.
5. 0.1.39 walked the type hierarchy: the field lives on the base class **`Unit`**, not
   `Vehicle`. After writing it (and calling `RepopulateValidSlots`) the log shows
   `slots written via Unit._designatedCameraSlots` and `slots=4`, and `camVsFcs` went from
   0.000 to 0.233 — the sight is no longer a passive follower.

Ruled out along the way: pushing an aim point (0.1.13/0.1.17/0.1.23), pitching the sight
(0.1.28-0.1.30, all overwritten by the game before rendering), biasing the gun (0.1.33, which
also broke the ready-to-fire check), and moving the camera onto the gunner sight line
(0.1.12/0.1.15/0.1.27).

## 0.1.28

- Two screenshots (350 m and 950 m) showed the impact marker the **same ~32 px** below the
  crosshair at both ranges. A ballistic error grows with range, so this is a **constant angular
  offset of ~0.22 deg** — the gun simply points that much below the sight axis, and no amount of
  drop compensation can bend a constant angle.
- Fix: the sight is pitched down by that constant, `"CITV Aim Pitch Trim (Leo2A4M)"` (default
  0.22 deg), so the crosshair sits on the line the shell actually travels. One value fits every
  range, which is exactly the behaviour the measurements show.
- Re-apply it every frame so a preference change can be tuned without a restart.

## 0.1.27

- The 590 m screenshot settled the argument: the impact sat ~21 px (~2.6 mrad, ~1.5 m) low,
  which is the *uncorrected* bore+sight offset (~1.2 m), i.e. the drop compensation from
  0.1.24/0.1.26 changed nothing. Conclusion: **the commander override ignores
  `SetAimWorldPosition`** and lays the gun with its own solution, so nothing we push can reach
  that path. (It also explains the whole history: 0.1.17 was accurate only because no override
  was engaged there; the pushed point alone drove the FCS, which does compensate.)
- Therefore the fix cannot be a push — it has to be **coincidence of axes**. The CITV camera is
  placed on the fire control's own sight line `(0.55, 2.55, 2.75)`, so the crosshair sits exactly
  on the line the game solves for, and the push is off by default (`aiming = looking &&
  alwaysSlave`, which is `false`).
- Free look is unchanged (the sight still pans on the commander's head) and the commander
  override is completely untouched — the game does all the ballistics itself.

## 0.1.26

- 0.1.25 cut the error from "clearly right and low" down to ~10 px at 400 m (~0.6 m, ~1.5 mrad),
  so the ray axis is right and what is left is the **sight-height term**: the sight line sits
  ~0.55 m above the bore (GPS y 2.55, bore y ~2.0) and the override aims the *bore* at the
  sight ray, so the shell starts that far below the line and lands short by it at any range.
- The compensation is now `flight drop + bore offset`, with the offset exposed as
  `"CITV Bore Below Sight (Leo2A4M)"` (default 0.55 m). At 400 m that adds ~1.4 mrad — the
  measured residual.
- Measured convergence so far at 400 m: right/low offset gone, remaining error ~10 px.

## 0.1.25

- The in-game screenshot settled what the numbers could not: at 350 m the impact marker sat
  **right and below** the crosshair. A 350 m shot only drops ~0.22 m, which is a few pixels at
  that zoom, so the offset was not ballistics at all — the aim **ray** was not the crosshair
  axis.
- `CitvAimPoint` now rays along `BufferedCamera.transform.forward` (the optic's optical axis)
  instead of `CameraFollow.CurrentAimVector` (the stabilised aim vector, a different line).
  The angle between the two is logged as `aimVsCam=` so the size of that mismatch is on record.
- Drop compensation stays in place behind its preference; at close range it contributes
  fractions of a metre and is harmless.

## 0.1.24

- Free look and commander override are now correct (0.1.23), but shooting under the override
  lands low again. The session log pinned it: while holding the override the push is live
  (`looking=True aiming=True pushed=True pushDist=1907`), so the aim point reaches the fire
  control — yet the impact is short.
- Explanation: GHPC's commander override lays the **bore** straight at the commander's sight
  line, with no ballistic compensation, and 0.1.17 only looked accurate because that build
  never engaged the override (the pushed point alone drove the FCS, which does compensate).
- Fix: while the override drives the gun, the pushed point is raised by the flat-fire drop for
  the measured range (`0.5 * g * (R/v0)^2`, `v0 = 1650 m/s`), so the override's own laying
  becomes correct. `"CITV Override Drop Compensation (Leo2A4M)"` scales it: 1 = full drop,
  0 = off, 0.5 if it overshoots. At 1900 m the term is ~6.5 m — the right order for the
  observed error.
- The state line now logs `drop=` next to `pushDist=`.

## 0.1.23

- `IsCommanderMode` was the wrong gate: it reads true for the whole commander station (it never
  goes false while the sight is up), so the fire control was being fed every frame and the turret
  followed the sight even with the override released.
- The gate is now the right mouse button (plus `Smooth Aim` as the second candidate, since that
  is what the 2A5 mod binds for the same job). Panning with both released leaves the turret
  alone; holding either slaves the gun and makes the shot land on the crosshair.
- The state line prints all three signals (`camMode`, `rmb`, `smoothAim`) so the next report
  says exactly which one fires.

## 0.1.22

- **Correct control model: free observation, gun slaved only during commander override.**
  GHPC already has that state (`BufferedCameraFollow.IsCommanderMode`, with `canOverrideWeapon`
  / `NotifyAimOverride` in the same subsystem), so the aim-point push is now gated on it
  (plus a direct right-mouse check as a fallback):
  - pan in the CITV with the override released -> free look, turret untouched;
  - hold the override (right mouse) -> the sight drives the gun, so lase + fire lands on the
    crosshair;
  - releasing it calls `ResetLrfAimPoint()` immediately.
- `CITV Slaves Turret Always` is back to `false` and now means "slave all the time" as an
  option, not as the default.
- The state log line carries `camMode=` and `rmb=` so we can see which of the two fires.

## 0.1.21

- Read the M1A2C implementation again, line by line, because it does not have this bug. The
  difference is structural, not a parameter:
  - their CITV renders on a **monitor canvas** whose camera looks along the **fire control's
    aim line**, so what the player sees already *is* where the gun points;
  - ours is a plain `CameraSlot` on the commander's head, looking where the commander looks.
    Sight line and gun solution are two different things until the FCS is told otherwise.
  The equivalent port is therefore "the sight drives the gun", which is what 0.1.17 did — the
  turret following the sight is not a regression, it is that behaviour.
- Default changed to `CITV Slaves Turret Always = true`: aim in the CITV, the turret follows,
  the shot lands on the crosshair. Set it to `false` for observation-only free look with the
  settle logic from 0.1.19/0.1.20.
- Also adopted the two cosmetic touches M1A2C makes: the HUD **3P aim reticle is hidden** while
  the CITV is up, and `BufferedCameraFollow.SNAPPINESS` is eased to 0.2 (restored on exit).
- 0.1.20's rate-based settle test stays behind that preference (the previous 0.03-degree frame
  test never counted as "still" because of the optic's stabiliser jitter).

## 0.1.19

- 0.1.18 gated the aim-point push behind holding fine aim, which is the same as not pushing at
  all for normal use -> the low impact came back. The two behaviours are inseparable with this
  API: push = accurate but the turret follows the sight; no push = free look but the CITV has
  no ballistic solution.
- New behaviour: the sight is only slaved once it has been **still for 0.4 s** — pan freely and
  the turret stays put, settle on the target and the fire control takes that line, then lase and
  fire land on the crosshair. Holding fine aim still forces it.
- New preference `"CITV Slaves Turret Always (Leo2A4M)"` (default false) restores the 0.1.17
  every-frame behaviour if the settle logic feels wrong.
- The log line now carries `still=` so it is obvious which state the sight is in.

## 0.1.18

- **The commander keeps his free look again.** 0.1.17 fixed the aim by feeding the FCS the
  CITV's aim point *every frame*, which also slaved the turret to the commander's sight — so
  panning the CITV dragged the turret around.
- The push is now gated on the fine-aim (**Smooth Aim**) input, exactly like the M1A2C mod:
  - pan without holding it -> free look, turret untouched;
  - hold it -> the turret follows the commander sight and the shot is solved for that line
    (lase and fire while holding);
  - releasing it calls `ResetLrfAimPoint()` so control goes back immediately.

## 0.1.17

- **The CITV aim point is fed to the fire control again, this time with the right ray.**
  The field test narrowed it down: the error only appears when the shot is fired *while the
  CITV is the active sight* (aim with the CITV -> low; aim with the gunner -> switch to the
  CITV without moving -> dead on). So the FCS is fine and the CITV's own aim point is what
  is wrong.
- The 0.1.13 attempt failed because the ray was cast with `~0` and hit our own hull: a short
  range means a short shot, i.e. exactly the "lands low" symptom. This version is the M1A2C
  recipe verbatim:
  - ray from `CameraFollow.BufferedCamera` along `CurrentAimVector`
  - `ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask` (excludes own vehicle)
  - `MAX_AIM_DISTANCE` from `BufferedCameraFollow` via reflection, fallback 4000 m
- `ResetLrfAimPoint()` releases the FCS when the player leaves the sight (fixes the turret
  locking up again).
- Diagnostics: `CITV aim pushed (x,y,z) dist=NNN fov=..` once a second — `dist` should match
  the target range; a tiny value would mean the ray still hits our own tank.

## 0.1.16

- **Reverted the 0.1.15 trade-off.** Putting the CITV camera on the gunner sight line did not
  change the impact at all, so the camera is back on the cupola `(0.83, 2.70, 0.95)` — no
  reason to lose the view for nothing.
- The fire control is left alone (no `SetAimWorldPosition`, no `ResetLrfAimPoint`).
- Added a once-per-second diagnostic while the sight is active:

  ```text
  [Leo2A4M] CITV diag fov=12.0 citvPos=(..) citvFwd=(..) opticPos=(..) opticFwd=(..) mantletFwd=(..)
  ```

  Comparing `citvFwd` with `opticFwd` / `mantletFwd` shows whether the commander sight looks
  down a different line than the gun is solved for — the one thing left that can explain
  "same gun, same FCS, different impact".

## 0.1.15

- **The fire control is no longer touched from the CITV.** A field test settled it: aim at
  the same point, lase once, fire from the CITV -> impact low; switch to the gunner sight
  without moving, fire -> hit. The gun and the FCS were always right, so the
  `SetAimWorldPosition()` push added in 0.1.13 was itself corrupting the solution (the ray
  cast from the commander camera did not describe the same firing solution). That path and
  the release/reset code from 0.1.14 are removed.
- The CITV camera is back on the **gunner sight line** `(0.55, 2.55, 2.75)` — the line the
  fire control actually solves for — so both sights fire identically. Trade-off: the
  commander's view comes from the gunner's aperture instead of the cupola.

## 0.1.14

- CITV aim point is only pushed to the fire control while the commander sight is the slot
  the player is looking through, and `ResetLrfAimPoint()` is called on the way out — this
  fixed the turret locking up after switching back to the gunner's view.

## 0.1.13

- CITV pushes its own aim point into the FCS (`SetAimWorldPosition`) so the shot is solved
  from the commander's line (ported from the M1A2C mod). Superseded by 0.1.15.


- **CITV aim fixed the way the M1A2C mod does it** (credit: `GHPC_MOD/M1UPDATE/M1A2C`).
  Instead of moving the camera onto the gunner's sight, the CITV now pushes its **own**
  aim point into the fire control while it is the active sight:

  ```csharp
  var w = PlayerInput.Instance.CurrentPlayerWeapon;
  w.Weapon.FCS.SetAimWorldPosition(CitvAimPoint);   // raycast from the commander camera
  ```

  The ballistic solution is then computed to hit the point the commander crosshair
  covers, so the sight-height parallax (CITV is 15 cm above / 28 cm right of the gunner
  sight) no longer matters and the camera stays on the cupola at
  `(0.83, 2.70, 0.95)`.
- The 2A5 lineage already carried this call, but gated behind *holding* "Smooth Aim";
  it now runs every frame the CITV is active.
- The aim point is a raycast along `CameraFollow.CurrentAimVector`, skipping our own
  vehicle's colliders (M1A2C used the laser rangefinder layer mask for the same job).

## 0.1.12

- **CITV shots no longer land low.** GHPC computes the firing solution from the
  *gunner's* sight line (`GPS`), but the commander's `NODs` slot is only a camera — so
  a CITV mounted 0.15 m higher and 0.28 m right of the gunner sight always put the
  impact that far below/left of the crosshair. The CITV camera now sits at the gunner
  sight position `(0.55, 2.55, 2.75)`, i.e. the same line the fire control solves for.
  Trade-off: the commander's view now comes from the gunner's aperture (same vantage as
  the gunner's sight) instead of the cupola; the earlier cupola position
  `(0.83, 2.70, 0.95)` is still one pref change away if the view matters more than the
  aim.
- Measured first: lowering the camera to the gunner's *height* but keeping the cupola
  `z` buried it in the turret's roof nets (screenshot), which is why it moved the whole
  way to the gunner's aperture.

## 0.1.10

- The traverse patch was verified working (`scaledCalls` climbing to 418 in a live
  session), so the remaining problem was only the amount.  Default
  `CITV Aim Sensitivity` 0.55 -> **0.4**, and the scale now also follows the zoom:
  `scale = pref x clamp(fov/12, 0.35, 1.5)`, so the 6 degree step is scaled twice as
  hard as the 12 degree one (`pref 0.4` gives 0.40 / 0.27 / 0.20 at 12/8/6 degrees).

## 0.1.9

- **Fixed the 0.1.8 regression that turned the tank back into a stock LEO2A3.** The
  traverse patch declared `Prefix(ref float distance)`, but Harmony binds prefix
  parameters **by name** and the game method is
  `TraverseBy(float traverseAngle)`. The resulting `HarmonyException: IL Compile Error`
  was thrown during `HarmonyInit`, which aborted the rest of the mod's start-up: the
  unit was never registered/converted, so the range spawned the plain donor.
  Both prefixes now take the argument **positionally** (`ref float __0`), which cannot
  break when a game patch renames a parameter.
- Verified: zero Harmony exceptions, `attached parts hull=44 turret=36 gun=5`,
  `converted LEO2A4M donor=LEO2A3`, screenshots normal.

## 0.1.8

- **CITV sensitivity now actually moves the sight.** The 2A5's knob never worked either
  (`AimSensitivity3P` only feeds the third-person camera), so this uses a Harmony prefix
  on the two methods the optic really pans through:
  `BufferedCameraFollow.TraverseBy` and `ElevateBy`. While the player is in the CITV the
  input distance is multiplied by `CITV Aim Sensitivity` (0.55 = **45 % slower**); the
  rest of the time the prefix does nothing.
- The log prints `CITV active=.. pref=.. scale=.. scaledCalls=..` every 10 s, so
  `scaledCalls` > 0 proves the pan is going through the patched path.

## 0.1.7

- **CITV sensitivity is now literally the 2A5 mod's code.** `Citv.cs` is a straight copy
  of the working 2A5 file (renamed), and the preference is set to the 2A5's tuned value
  **0.55** (applied = 1.25 x (12/60) x 0.55 = 0.1375, the same number the 2A5 uses).
  My earlier 0.15 was *below* that, which in this formula means the sight pans faster,
  so every "make it slower" step I took was going the wrong way.
- **Reticle no longer jumps when a range is put in.** `MoveGunnerSight` moved only
  `GPS/Optic` and `GPS/FLIR`, leaving the `GPS` transform itself — the fire control's
  sight line — about 0.9 m behind the camera. On ranging the solution was applied to a
  sight that disagreed with the view, hence the hop. The whole `GPS` assembly is moved
  now (delta −0.809 / +0.452 / −0.185 in this rig), which also carries the reticle
  meshes and the sight's post volume along.

## 0.1.6

- **The belly plate is gone.** `add_belly_plate()` (carried over from the 2A5 pipeline)
  took the convex hull of everything in the lowest 0.95 m — including the fender
  corners at ±1.7 m — inset it 12 % and laid a flat plate at the bottom. On this model
  that hull is wider than the real floor (±1.5 vs ±1.2), so the plate stuck out under
  the nose like a shelf. The WT hull **already has a closed floor**, so the plate was
  unnecessary: removed, Hull 125,206 → **125,166 tris**. It is now opt-in
  (`prep_2a4m.py --belly`) for the case where a shell really is open underneath.
- Verified from below and from a low front quarter (`tools/verify_belly_2a4m.py`):
  no see-through, no protrusion.

## 0.1.5

- **The parts are no longer merged.** Every one of the 127 WT objects is exported as its
  own `o` block under its original name and attached as its own GameObject — the add-on
  armour blocks (`ex_armor_*`), cage panels, camo nets, hatches and MGs are individually
  addressable again, which is what per-piece armour values and AAR interiors need.
  Only the three base shells still get hole filling (and the hull the belly plate).
- New `parts.tsv` records the structure per object: `name / anchor / order / tris /
  base_shell` — anchor is the rig transform it rides on, order is the WT scene-graph
  index, so the flat OBJ stays recoverable. `tools/parts_table.py` rebuilds it from
  `manifest.json`; the offline gate (`check_assets_2a4m.py`) now validates per anchor
  instead of expecting four merged meshes.
- Runtime: `Convert()` loops over the parts, parents each to Hull/Turret/Gun via the
  table (log line `attached parts hull=44 turret=36 gun=5`).
- Coax MG (`bone_mg_gun_twin`, 2,451 tris) now rides the Gun anchor, so it elevates
  with the barrel instead of staying level in the turret.

## 0.1.4

- **The coaxial MG now elevates with the gun.** `vehicle#bone_mg_gun_twin` (2,451 tris,
  x 0.22..0.36 / y 1.97..2.20 / z 0.70..1.95 — the tube next to the barrel) matched
  the `bone_mg` turret rule and stayed level while the barrel elevated. It is now in
  the **Gun** part: Turret 102,301 -> **99,850**, Gun 12,327 -> **14,778**.
- **CITV pans through the free-look path, not `aimSensitivity3P`.** The session log
  showed `active=True` with `applied=0.038` and still no change, so the value never
  reached the optic. `freeCamSpeedSensitivity` (baseline 0.050) is now scaled while
  the sight is up and restored on the way out; the log carries `freeCamSens=` too.

## 0.1.3

- **Gunner thermal no longer black.** 0.1.2 shipped ramp 2.6 / exposure 1.1, which
  crushed the picture to near-black; now **ramp 1.9 / exposure 1.5** (donor: 1.5 / 1.0).
- **CITV sensitivity actually reaches the camera now.** The log from a real session
  showed `CITV active=False` for the whole run, so every frame fell back to the 5f
  default regardless of the preference. The activation test now also accepts the
  commander station *parent* (`head.IsChildOf(slot.transform.parent)`) and, as a
  last resort, "player is in this vehicle + thermal light mode + no slot owns the
  view". The log line now carries `head=`, `curSlot=` and `light=`.
- **Camo nets are the vehicle's colour.** New `tools/tint_nets.py` re-hues the two
  brown WT net sheets to the body's mean colour (rgb 81,78,67 → net ~89,84,71),
  re-levels their brightness to the body and scales their specular to 45 % so they
  stop reading as shiny tarpaulin up close. `materials.tsv` now points at
  `*_green.jpg` / `*_n_s_soft.jpg`.
- `DumpCameraApi` writes the `CameraManager` / `CameraSlot` /
  `BufferedCameraFollow` member list into `leodebug.txt` (used to chase the
  sensitivity field).

## 0.1.2

- **Gunner sight is now second generation.** The donor's FLIR look (480x240 buffer,
  chunky pixel cookie, wobble, ramp 1.5, 0.1 scanlines) is replaced on a private
  copy of the blit material: **1200x600** buffer (`Resolution Scale` 2.5), pixel
  cookie off, wobble/blur 0, colour ramp 2.6, scanlines 0. Switch:
  `"Gunner Sight Gen-2 Thermal (Leo2A4M)"` (default true).
- **CITV is sharper and slower.** `CITV Resolution` 640 -> **1280** (1280x720), and
  `CITV Aim Sensitivity` 0.3 -> **0.15** (lower = slower; applied 0.075 -> 0.038).
- CITV sensitivity is now clamped (pref 0.02-0.6, applied 0.005-0.35) and the
  activation test also accepts `commanderHead == slot` / `_currentCamSlot == slot`,
  so a slot the CITV component does not own cannot silently fall back to the 5f
  default. The sight logs `CITV active=.. fov=.. applied=..` while in use.

## 0.1.1 — range spawn separation

- The 2A4M and the 2A5 both spawned on the same point: with a player vehicle present
  both mods use the same `player + forward*18 - right*9` formula from the shared
  `PlayerReady` trigger. The 2A4M spawn is now shifted **10 m left and 5 m forward**
  in vehicle-local axes, so the two tanks stand ~11.2 m apart (verified in the
  scene dump).
- Offset is applied after both branches (player present / no player), so it holds
  in the autotest too.
- The 2A5 mod (0.1.69) additionally walks forward to the first patch that is level
  *and* off the 0.8 m gravel mound the range spawn point sits on; the 2A4M keeps the
  plain ground snap because its spot is already on the gentle part of the slope.

## 0.1.0

First build of the 2A4M port, derived from the Leopard 2A5 mod (0.1.68).

- New unit `LEO2A4M` cloned from the `LEO2A3` donor (2A4-family prefab with the
  EMES-15 thermal sight); vanilla running gear, tracks and interior are kept.
- WT 2A4M split into **Hull / Turret / Gun** from the seek&destroy export:
  125,206 / 102,301 / 12,327 tris, running gear dropped.
- Calibration re-measured for this export: `BaseY = -0.02`, `BaseZ = 0.477`
  (turret ring at mesh z 0.342, GHPC pivot 0.816).
- **CITV kept** — the donor's `commander head/NODs` slot is moved onto the WT
  `optic_commander` head at vehicle `(0.83, 2.70, 0.95)`.
- Gunner / thermal sight cameras moved in front of the WT add-on armour
  (`Gunner Sight *` = 0.55 / 2.55 / 2.75); without it both pictures start inside
  the armour block.
- Offline gate `tools/check_assets_2a4m.py` (split-leak checks included) passes.

Carried over unchanged from the 2A5 mod: `ObjLoader`, `Citv.cs`, `materials.tsv`
plumbing, `GHPC/VehicleShader` materials, hide-vanilla whitelist, autotest harness.
Removed for this tank: the A5 turret-armour patch and the sight-interior housing.
