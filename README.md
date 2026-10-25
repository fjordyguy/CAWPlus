# CAW Plus

By fjordyguy, made with Claude. Version 1.0.0.

CAW Plus adds tools to The Sims 3 **Create a World Tool (CAW)** that build most of a world for you, in the style of EA's own worlds:

| Tool | What it does |
|---|---|
| **Generate Terrain** | Makes the land: hills, mountains, rivers, coasts and lakes |
| **Generate Roads** | Plans and builds the roads, bridges and lots |
| **Footpaths** | Adds footpaths round the roads |
| **Auto-Paint** | Paints the terrain (grass, rock, dirt, sand) in one click |
| **Auto-Place** | Places trees, bushes, rocks, road signs and streetlights |
| **Spawners** | Places fishing spots, seeds, gems, insects and other collectables |
| **Distant Terrain** | Makes the landscape you see beyond the edge of the map |
| **Create World** | Guides you through all of the above, in order |

Each tool can copy the look of a real Sims 3 world: pick a **World type** (Sunset Valley, Bridgeport, Isla Paradiso... every EA world is built in).

CAW's own files are never changed. Everything CAW Plus does can be undone.

---

## Install

1. Download `CAWPlus_v….zip` from the newest release on this repository's **Releases** page and unzip it.
2. Copy **everything** in it into your CAW folder:
   `C:\Program Files (x86)\Electronic Arts\The Sims 3 Create a World Tool`
   (Windows asks for administrator permission.)
3. Install Microsoft's **.NET Desktop Runtime 8** (or newer, x64) if you don't have it. CAW Plus works without it, but more slowly.
4. Start CAW with **CAWPlus.exe** from the CAW folder (a desktop shortcut is handy). Starting CAW the normal way still gives you plain CAW.

The tools are on a new **CAW Plus** tab next to CAW's Terrain tab, in the order you use them. **Dark mode** on that tab switches CAW's own window to dark colours (remembered for next time).

---

## Making a world

Click **Create World** (CAW Plus tab) to be guided through these steps, or use the tools one by one:

1. **Generate Terrain**: make the land.
2. **Generate Roads**: build the roads, then place the lots.
3. **Footpaths**
4. **Auto-Paint**
5. **Auto-Place**
6. **Spawners**
7. **Distant Terrain**: make it, restart CAW, place it.
8. Save the world.

> **Tip:** try things out on a **copy** of a world, or a new blank one, before using them on a world you care about.

---

## The tools

### Generate Terrain

1. **Choose the world size first.** CAW can't resize a world later. Pick Small, Medium or Large, click **Heightmap for New World...**, then in CAW use **File > New World** with that image. Open Generate Terrain again in the new world.
2. Pick a **World type**. **Any EA world** picks a different EA world for each seed.
3. Change the sliders or click **New map** until you like the preview. **3D view**: drag to turn, right-drag to move, mouse wheel to zoom, double-click to reset; the key in the corner shows the scale (road, lot, car, Sim). **Colours** shows the map in green, desert or the world type's own paint colours.
4. Click **Apply terrain**. **Undo terrain** puts the old land back.

### Generate Roads

1. Plans roads and lots for the land you have, like the chosen World type's town (downtown grid, neighbourhoods, country roads). **New layout** tries another.
2. **Build roads** lays them in CAW, with bridges over narrow water and end caps on dead ends. The ground under the roads is smoothed first.
3. **Place lots** puts the lots along the roads, with level pads on hillsides.

**Road look** chooses the road texture. CAW's Undo removes the roads and lots.

### Footpaths

Run it after the roads are built. **Plan footpaths** shows them, **Build footpaths** lays them. Paths keep out of lots and go round trees and rocks. **Amount** sets how much path.

### Auto-Paint

1. Click **Read terrain**.
2. Pick a **Style** (an EA world's way of painting) or leave the built-in rules. Check which texture is used for each type (rock, dirt, sand, meadow); **Smart pick** chooses for you.
   **Get world textures...** copies the paint textures from world files you have (for example the EA worlds' CAW files) into your SourceTextures folder, so each style can use its world's own textures.
3. The preview shows each texture's own colour. Tick **3D view** to look round it (drag to turn, wheel to zoom).
4. Click **Paint world**. **Undo paint** restores your old paint.

Touch-ups you paint by hand afterwards are kept the next time you click **Paint world**.
**World edges** blends the map's edge into the distant terrain. **Block steep ground** stops Sims walking up cliffs.

### Auto-Place

1. Click **Read world** and pick a **World type**.
2. Tick what to place: trees, rocks, road signs, streetlights, bushes.
3. The sliders change the amounts. At their defaults you get the World type's own amounts.
4. Click **Place**. **Remove last** takes them out again.

Objects go into tidy World Layers (Trees, Rocks...). **Max objects** keeps the world from getting too heavy.
**Waterfalls at pond steps** (test, off by default) adds EA's waterfall effects where a raised pond spills over a cliff.

### Spawners

**Read world**, pick a **World type**, adjust the counts, then **Place**. Fishing spots go on the water; the rest go on open ground away from roads and lots.

### Distant Terrain

Do this last: it copies the colours of your paint and trees.

1. Adjust the preview, then click **Make distant terrain**.
2. **Close and restart CAW**, open the world, open Distant Terrain again and click **Place in world**.
3. To share the world, players need the `CAWPlus_DistantTerrain_<world>.package` file in their `Documents\Electronic Arts\The Sims 3\Mods\Packages` folder.

---

## Help

- **Log and backups:** `Documents\CAWPlus` (the log is `CAWPlus.log`). Please include the log when reporting a problem.
- **Self-test** (CAW Plus tab): checks that CAW Plus works with your CAW, then undoes everything. Run it on a copy of a world.
- **CAW settings:** if your CAW folder has a `WorldBuilderCS.exe.config` or `CAW.exe.config` with your own settings, copy them into `CAWPlus.exe.config`.
- **Slow?** Restarting CAW helps after a long session.
- **"Smart App Control blocked an app"** (Windows 11): CAW Plus isn't signed with a paid certificate, so Smart App Control blocks it. It has no per-app exception: turn it off in Windows Security > App & browser control > Smart App Control settings (on some Windows versions it can't be turned back on without resetting Windows).

---

## Building from source

Needs the .NET 8 SDK (or newer); no Visual Studio or targeting pack is needed.

```
dotnet build -c Release
cd helper
dotnet publish -c Release -r win-x64 --self-contained false -o ../bin/Release/CAWPlusHelper
```

`bin/Release` then holds `CAWPlus.exe`, `CAWPlus.exe.config`, the `Library` folder and the `CAWPlusHelper` folder: copy them into the CAW folder.

CAW Plus is a .NET 3.5, 32-bit program (CAW runs on .NET 2.0). It loads CAW into its own process and reaches CAW's classes by reflection, so it builds without CAW installed. The helper is a separate 64-bit .NET program that does the heavy computing when the .NET runtime is installed.

## Licence

MIT (see `LICENSE`). The Sims 3 and Create a World Tool are trademarks of Electronic Arts Inc.; CAW Plus is not made or endorsed by Electronic Arts. It contains no EA game files: the built-in world libraries in `library/` are figures measured from the worlds (counts, shares, distances), not EA's content.
