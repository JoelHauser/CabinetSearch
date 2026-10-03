# CabinetLooter -- working notes for Claude

Opening any filing-cabinet drawer shows every drawer of that cabinet in the one loot panel and
searches them one after another. Optional cluster mode adds the cabinets standing next to it.
Client only, one BepInEx plugin. Repo https://github.com/JoelHauser/CabinetSearch.git (the mod is
called Cabinet Looter, the repo CabinetSearch).

## State as it stands

**0.1.0 is built, installed into H:\SPT4.1.X\BepInEx\plugins and pushed (8490dc6, 2026-10-03), but has never been run in game.** Every claim below about what the
panel looks like is a prediction from the decompiled client and the prefab dump, not an
observation. Treat it that way until a raid test says otherwise.

```
scripts\pack.ps1 -SPTPath H:\SPT4.1.X            # build + zip into dist\
scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install   # also copy into BepInEx\plugins
```
Run through PowerShell, not Bash. Compiles against the **patched** Assembly-CSharp (the build
refuses to run if `Assembly-CSharp.dll.spt-bak` is missing).

## What the first raid test has to look at

1. `BepInEx\LogOutput.log` has `Cabinet Looter 0.1.0 loaded.` If instead it says the patches
   could not be applied, a target signature has changed.
2. Turn on **Log cabinet details** (F12). Opening a drawer should log `Opened card_file_box_0N ...`
   and `Showing N drawers from 1 cabinet(s).` No `Showing` line means the panel attach failed or
   the identity check (`item != pending.OpenedItem`) did not match.
3. The look of the panel: do headings and the extra 2x2 grids stack under the first drawer in the
   scroll area? Headings were given a fixed 320x24 rect and a LayoutElement because whether
   Content's VerticalLayoutGroup controls child width is not known.
4. Ctrl+click on an item in a non-opened drawer must go to the player, not into the opened drawer.
5. Switch Gear -> Health -> Gear tabs with a cabinet open: the drawers must come back.
6. Test with UIFixes on (it patches `ItemUiContext.QuickFindAppropriatePlace` and references
   `SimpleStashPanel`) and with maschine-AutoCorpseSearch on (it can start container searches too;
   both sides check `SearchOperations` before starting, so they should not double up).

## Verified facts this rests on (2026-10-03)

**Cabinet structure**, read with UnityPy from the scene files of all ten maps (byte-grep a drawer
id across `EscapeFromTarkov_Data\level*` to find the scene, then walk transforms):
- Drawer = `LootableContainer`, template `578f87b7245977356274f2cd`, 2x2 grid, on GameObject
  `card_file_box_0N` (layer 22, own BoxCollider). Cabinet = parent GameObject `card_file`.
- No drawer outside a `card_file`, no parent holding two cabinets. Most have 4 drawers; Lighthouse
  (Treatment), Ground Zero and Streets have some with 1-3.
- All four drawers share one pivot. Order comes from collider bounds, never transform position.
- Server `statics.json` groupIds are area spawn budgets, not cabinets. Drawer ids repeat across maps.

**Client code paths** (`ilspycmd -p` of the patched Assembly-CSharp):
- `InteractionContextHelper.OnContainerOpen(owner, callback, lootableContainer, initialDistance)`:
  every drawer open passes here, then `GamePlayerOwner.ShowInventoryScreenLoot(rootItem)`.
  `Player.Interact(IItemOwner)` is a no-op success outside live EFT.
- `ItemsPanel.Show` -> `SimpleStashPanel.Show(lootItem, ..., sourceContext.CreateChild(lootItem), ...)`
  -> `_simplePanel` (`SearchableItemView`, on the same GameObject as the panel) -> its
  `ContainedGridsView` parented into `_gridsContainer` = `Mask/Scroll Area/Content`, which has a
  VerticalLayoutGroup (prefab in level44, `Common UI/InventoryScreen/Items Panel/Stash Panel/Simple Panel`).
- **In raid the screen's context is `EmptyItemContext`, whose `CreateChild` returns a
  `DefaultItemContext` with no Source.** So `openedContext.Source` is null in raid; siblings get
  `new DefaultItemContext(item, viewType)`, which is what vanilla does there.
- `ItemUiContext.Configure` (12-arg overload) stores `rightPanelItems` in `_rightPanelItem`.
  `QuickFindAppropriatePlace` treats an item not under any of them as player-side, so with only the
  opened drawer registered, Ctrl+click on another drawer's item moves it INTO the opened drawer.
  All drawers are registered, opened one first.
- Search: `PlayerSearchController` keeps a per-raid HashSet of searched items plus per-item known
  flags. `SinglePlayerSearchContentOperation`: 2 s open on first search, 1-2 s per item (Attention
  speeds it). One at a time, two with elite Attention (`CanStartNewSearchOperation`).
  `SearchableView.Close` calls `StopSearching` on its drawer; found items stay found.
- Gear and Health tabs share one `ItemsPanel`, so a tab switch closes and re-shows the panel. The
  session is parked in `_pending` on close so the re-show finds it.

**Fika** (source cloned from project-fika/Fika-Plugin main @118cccb, not installed here):
search operations are not replicated (`ClientInventoryController.Execute` skips
`SinglePlayerSearchContentOperation`); item moves go to the host as descriptors, the host resolves
items by id and runs them, a lost race fails and resyncs that client. No "opened container" check
on the host (`ObservedInventoryController.CanExecute` returns true). Container door packets are
only for the drawer actually opened, which the mod leaves to vanilla.

## Cluster mode

`Cabinets.FindCluster`: one `Physics.OverlapBoxNonAlloc` per cabinet, around the union of its
drawer colliders expanded by the gap, on the opened drawer's own layer (taken from the drawer, not
a named mask). A neighbour is kept only if a ray between the two cabinets' centres
(`LayersMaskController.HighPolyWithTerrainMask`) hits nothing outside the two cabinets'
hierarchies, so a cabinet behind a wall is excluded. Nearest first, capped by config. Runs once per
drawer opening, never on a timer.

Not yet seen in game: whether the drawer BoxColliders are what the overlap finds (layer 22 was read
from the scene), and whether the wall test lets real side-by-side cabinets through.
