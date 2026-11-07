# Leopard 2A4M for Gunner, HEAT, PC!

A playable **Leopard 2A4M** (War Thunder model) for **Gunner, HEAT, PC!**, built on the vanilla
`LEO2A3` donor: add-on armour, hull armour packages, selectable 120 mm rounds and a commander's
independent thermal viewer.

- **Author**: DDDDWWWWDZW
- **Requires**: MelonLoader 0.6.2+

> Model and textures come from War Thunder (Gaijin Entertainment),I performed retopology and re-segmented the components to adapt them for in-game functionality. Personal, non-commercial use only.

## Installation

Download these two files from [Releases](../../releases):

- `Leo2A4M.dll`
- `Leo2A4M.l2pack`

and place them like this:

```text
Bin/Mods/Leo2A4M.dll
Bin/Mods/Leo2A4M/Leo2A4M.l2pack
```

MelonLoader only loads DLLs that sit **directly** in `Bin/Mods`, so the DLL must not go into a
sub-folder. Everything the mod needs — model, textures, tables, CITV display — is inside the
`.l2pack` and is loaded into memory; nothing is written to disk. (Putting the `.l2pack` next to the
DLL also works.)

## Contents

- L2A4M visual model
- In commander view, press N to activate the CITV.
- L44 120 mm gun
- KEW-A1 / DM43/ DM53  AP
- Armor data and armor damage model adapted for the L2A4M
- Gen-3 gunner thermals 


Relevant entries:
```ini
[ Leo2A4M ]
"Turret Add-on Armour (Leo2A4M)" = true # Adds extra protection to the turret front add-on armour. Default: false
"CITV (Leo2A4M)" = true # Commander's independent thermal viewer (gen-1 imager)
"Paint Scheme (Leo2A4M)" = "Bronzegrun" # Bronzegrun (default), Sand, or Tricolor
"AP Ammo (Leo2A4M)" = "dm53" # kewa1 (608 mm), dm43 (562 mm), dm53 (668 mm, ignores 30% of ERA)
"Hull Armour Level (Leo2A4M)" = "d" # Choose the hull armour package: b / c / d. d is the strongest, b is the stock Leo 2A3 armour. Default: b
```

## Notes
- The mod models are sourced from War Thunder. I performed retopology and re-segmented the components to adapt them for in-game functionality.All rights reserved. Redistribution, modification and commercial use
are not permitted without written permission.
- This is a compiled distribution; modify/redistribute only with permission.
- If you wish to modify and release your own version based on my mod, please notify me.
- This mod was created with the assistance of AI   感谢蓝色大肥鱼
