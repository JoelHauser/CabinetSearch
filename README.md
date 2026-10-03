# Cabinet Looter

Open any drawer of a filing cabinet and loot the whole cabinet from one window.

In vanilla Tarkov every drawer of a filing cabinet is its own container: open it, search it, close
it, open the next one. With Cabinet Looter, opening any drawer shows every drawer of that cabinet
in the loot panel, one under the other, and searches them one after another.

- **Normal searching.** Drawers are searched at the usual speed, with your Search and Attention
  skills, one at a time (two with elite Attention). Items still appear one by one.
- **The loot is untouched.** Each drawer stays its own container with the loot the raid gave it.
  Nothing is rerolled, copied, deleted or moved into your inventory for you.
- **Progress is kept.** Closing the window interrupts searching, as in vanilla. Reopening any
  drawer of that cabinet carries on with the unfinished drawers.
- **One cabinet at a time.** Only drawers of the cabinet you opened are shown, even when cabinets
  stand side by side. The optional cluster mode below changes that on purpose.
- **Click a drawer's heading** to start or stop that drawer's search yourself.

## Cabinet clusters (optional, off by default)

Turn on **Include cabinets standing next to it** to also show the filing cabinets standing directly
beside or on top of the one you opened, as one cluster: three cabinets in a row are looted from one
window. A cabinet on the other side of a wall is never included. The gap allowed between cabinets
and the size of a cluster can be set.

## Settings

Press F12 in game (BepInEx Configuration Manager).

| Setting | Default | |
| --- | --- | --- |
| Show the whole cabinet | on | Off restores vanilla. |
| Search every drawer automatically | on | Off: the drawers are shown, you click a heading to search. |
| Resume half-searched drawers | on | Vanilla makes you press Search again on an interrupted drawer. |
| Include cabinets standing next to it | off | Cluster mode. |
| Largest gap between cabinets (metres) | 0.3 | Measured side to side. |
| Most cabinets in a cluster | 4 | The nearest are kept. |
| Log cabinet details | off | Writes what was found to `BepInEx\LogOutput.log`. |

## Install

Unzip over your SPT folder, so that `CabinetLooter.dll` lands in `BepInEx\plugins`. Client only;
there is no server part.

Built for SPT 4.1.x (EFT 0.16.9).

## Notes

- Only the drawer you opened slides open on screen. The others are looted while they stay shut.
- **Ctrl+click** on an item in any drawer moves it to your inventory. Ctrl+click on an item in your
  inventory moves it into the drawer you opened.
- **Fika:** searching is per player, as in vanilla Fika, and taking items goes through the host as
  usual, so two players at one cabinet behave as two players at one container always have. Not
  yet tested in a Fika raid.
