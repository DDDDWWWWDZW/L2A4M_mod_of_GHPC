# Leopard 2A4M for Gunner, HEAT, PC!

A playable **Leopard 2A4M** (War Thunder model) for **Gunner, HEAT, PC!**, built on the vanilla
`LEO2A3` donor: add-on armour, hull armour packages, selectable 120 mm rounds and a commander's
independent thermal viewer.

- **Version**: 2.0.0
- **Author**: DDDDWWWWDZW
- **Requires**: MelonLoader 0.6.2 (x64, net35)

> Model and textures come from War Thunder (Gaijin Entertainment); the game belongs to Radian
> Simulations. No game files are included. Personal, non-commercial use only.

## Installation

Download `Leo2A4M-<version>-pack.zip` from [Releases](../../releases) and extract it into
`<GHPC>/Bin/Mods/` so that you get:

```text
Bin/Mods/Leo2A4M.dll
Bin/Mods/Leo2A4M/Leo2A4M.l2pack
```

(Dropping the `.l2pack` next to the DLL also works.) Everything the mod needs — model, textures,
tables, CITV display — is inside the pack and is loaded into memory; nothing is written to disk.

## Configuration

`Bin/UserData/MelonPreferences.cfg`, section `[Leo2A4M]`:

```ini
"Turret Add-on Armour (Leo2A4M)" = true # Adds extra protection to the turret front add-on armour. Default: false
"CITV (Leo2A4M)" = true                 # Commander's independent thermal viewer. Default: true
"Paint Scheme (Leo2A4M)" = "Bronzegrun" # Bronzegrun / Sand / Tricolor. Default: Bronzegrun
"AP Ammo (Leo2A4M)" = "dm53"            # kewa1 / dm43 / dm53. Default: kewa1
"Hull Armour Level (Leo2A4M)" = "d"     # Choose the hull armour package: b / c / d. d is the strongest, b is the stock Leo 2A3 armour. Default: b
```

Round data, armour values and the complete change list are in [CHANGELOG.md](CHANGELOG.md).

## Controls

The commander's thermal viewer is a free-observation sight (it does not move the turret); hold the
right mouse button to take over the gun with it.
