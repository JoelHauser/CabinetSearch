# Cabinet Looter

Open any drawer of a filing cabinet and loot the whole cabinet from one window.

In vanilla every drawer is its own container: open it, search it, close it, open the next. With
Cabinet Looter, opening any drawer shows all of that cabinet's drawers side by side and searches
them one after another.

## Features

- **The whole cabinet in one window.** Every drawer gets its own grid, with a heading showing its
  state: not searched, searching, partly searched, empty, or how many items it holds.
- **Normal searching.** Drawers are searched at the usual speed with your Search and Attention
  skills. Items still turn up one by one.
- **The loot is untouched.** Each drawer keeps the loot the raid gave it. Nothing is rerolled,
  copied, deleted or moved into your inventory for you.
- **Progress is kept.** Closing the window interrupts searching, as in vanilla. Opening any drawer
  of that cabinet again picks up where it left off.
- **The search bar follows along.** SEARCHING and the timer stay up until the last drawer is done.
  The X stops the whole cabinet.
- **Click a drawer's heading** to start or stop that drawer on its own.
- **Cabinet clusters (optional, off by default).** Also show the cabinets standing right beside
  the one you opened, one row per cabinet. Cabinets on the other side of a wall are left out.

## Settings

Press F12 in game (BepInEx Configuration Manager).

| Setting | Default | |
| --- | --- | --- |
| Show the whole cabinet | on | Off restores vanilla. |
| Search every drawer automatically | on | Off: click a heading to search a drawer. |
| Resume half-searched drawers | on | Vanilla makes you press Search again. |
| Include cabinets standing next to it | off | Cluster mode. |
| Largest gap between cabinets (metres) | 0.3 | Measured side to side. |
| Most cabinets in a cluster | 4 | The nearest ones are kept. |
| Log cabinet details | off | For bug reports: writes what it found to `BepInEx\LogOutput.log`. |

## Install

Unzip over your SPT folder so `CabinetLooter.dll` lands in `BepInEx\plugins`. Client only, no
server part. Built for SPT 4.1.x.

## Good to know

- Only the drawer you opened slides open in the world; the others are looted while they stay shut.
- Ctrl+click on an item in any drawer moves it to your inventory. Ctrl+click from your inventory
  puts the item in the drawer you opened.
- Empty drawers in a scav raid are SPT, not this mod: when SPT cuts a scav raid's loot by half
  or more, every one-item container on the map (every drawer) spawns empty.
- Fika: not tested yet. Searching is per player and taking items goes through the host as usual,
  so it should behave like two players at one container always have.

## Building

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # build and zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # also copy into BepInEx\plugins
```

Run from PowerShell. Compiles against the SPT-patched `Assembly-CSharp.dll`, so start the game
through the SPT Launcher once before the first build.
