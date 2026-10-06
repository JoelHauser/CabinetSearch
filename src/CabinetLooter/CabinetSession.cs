using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CabinetLooter
{
    /// <summary>
    /// One opened cabinet (or cluster), from the moment a drawer is opened until the loot panel
    /// closes.
    ///
    /// How the loot panel is extended. Vanilla shows the opened drawer through the panel's
    /// <see cref="SearchableItemView"/>, which drops the drawer's <see cref="ContainedGridsView"/>
    /// into the scroll area's <c>Content</c> object. Content has a VerticalLayoutGroup (read off
    /// the prefab in level44), so every other drawer gets its own heading and its own grid view
    /// added to Content, and they stack and scroll with the first. Each grid view is bound to its
    /// real drawer item through its own item context, exactly as vanilla binds the first, so
    /// dragging, quick moves, filters and the hidden "?" items all behave as they do in vanilla.
    ///
    /// The search button and timer in the panel's top bar belong to the opened drawer and are left
    /// alone. The big "UNSEARCHED" overlay is hidden while the cabinet is shown, because it covers
    /// the whole scroll area and so every drawer, not just the one it is about; each drawer's
    /// heading says its own state instead, and clicking a heading starts or stops that drawer.
    /// </summary>
    internal sealed class CabinetSession
    {
        private sealed class Entry
        {
            public LootableContainer Container;
            public SearchableItemItemClass Item;
            public int CabinetIndex;
            public int DrawerIndex;
            public bool IsOpened;

            public DrawerHeading Heading;
            public ContainedGridsView Grids;
            public ItemContextAbstractClass RawContext;
            public GClass3458 Context;
            public bool GridsShown;

            /// <summary>Seen searching since the last tick; a search that then vanishes unfinished was stopped.</summary>
            public bool WasSearching;

            /// <summary>Stopped by the player, or failed. Not started again automatically while the panel is open.</summary>
            public bool Stopped;

            /// <summary>Started by this mod once already. A drawer is never auto-started twice per showing.</summary>
            public bool Started;

            public string Label => "DRAWER " + (DrawerIndex + 1) + (IsOpened ? " (OPENED)" : string.Empty);

            public string LogName => "cabinet " + (CabinetIndex + 1) + " drawer " + (DrawerIndex + 1);
        }

        // 4.0.x names. SPT 4.0's Assembly-CSharp keeps the views' serialized fields private (4.1
        // made them public) and leaves the plain fields obfuscated, so every one is reached here.
        // FieldRefAccess throws when the class loads if a name is wrong, so a mismatch fails loudly.
        private static readonly AccessTools.FieldRef<SearchableItemView, ContainedGridsView> ContainedGridsViewRef =
            AccessTools.FieldRefAccess<SearchableItemView, ContainedGridsView>("containedGridsView_0");

        private static readonly AccessTools.FieldRef<SearchableItemView, ContainedGridsView> ContainedGridsTemplateRef =
            AccessTools.FieldRefAccess<SearchableItemView, ContainedGridsView>("_containedGridsTemplate");

        private static readonly AccessTools.FieldRef<SearchableItemView, Transform> GridsContainerRef =
            AccessTools.FieldRefAccess<SearchableItemView, Transform>("_gridsContainer");

        private static readonly AccessTools.FieldRef<SimpleStashPanel, InventoryController> PanelInventoryControllerRef =
            AccessTools.FieldRefAccess<SimpleStashPanel, InventoryController>("inventoryController_0");

        private static readonly AccessTools.FieldRef<SimpleStashPanel, FilterPanel> PanelFilterRef =
            AccessTools.FieldRefAccess<SimpleStashPanel, FilterPanel>("_filterPanel");

        private static readonly AccessTools.FieldRef<SimpleStashPanel, SearchableItemView> PanelSimplePanelRef =
            AccessTools.FieldRefAccess<SimpleStashPanel, SearchableItemView>("_simplePanel");

        private static readonly AccessTools.FieldRef<SimpleStashPanel, TextMeshProUGUI> PanelContainerNameRef =
            AccessTools.FieldRefAccess<SimpleStashPanel, TextMeshProUGUI>("_containerName");

        private static readonly AccessTools.FieldRef<SearchableView, SearchButton> SearchButtonRef =
            AccessTools.FieldRefAccess<SearchableView, SearchButton>("_searchButton");

        private static readonly AccessTools.FieldRef<SearchableView, TimerText> SearchTimerRef =
            AccessTools.FieldRefAccess<SearchableView, TimerText>("_searchTimer");

        private static readonly AccessTools.FieldRef<SearchableView, UnityEngine.UI.Button> UnsearchedPanelRef =
            AccessTools.FieldRefAccess<SearchableView, UnityEngine.UI.Button>("_unsearchedPanel");

        /// <summary>Built when a drawer is opened, waiting for its loot panel.</summary>
        private static CabinetSession _pending;

        /// <summary>The session whose sections are on screen.</summary>
        private static CabinetSession _current;

        private readonly List<Entry> _display;
        private readonly List<Entry> _searchOrder;
        private readonly int _cabinetCount;

        /// <summary>Titles and spacers, everything added to Content that is not a drawer's heading or grids.</summary>
        private readonly List<GameObject> _decorations = new List<GameObject>();

        private SimpleStashPanel _panel;
        private SearchableView _searchableView;
        private InventoryController _inventoryController;
        private IPlayerSearchController _searcher;
        private FilterPanel _filterPanel;
        private Transform _content;

        /// <summary>Vanilla's grid view for the opened drawer, while it sits in one of our columns.</summary>
        private ContainedGridsView _openedGrids;

        /// <summary>Unsubscribes our listener from the top bar's search button and its X.</summary>
        private Action _unsubscribeSearchButton;

        /// <summary>The player pressed the top bar's X: no more automatic searches this showing.</summary>
        private bool _chainStopped;

        private const float ColumnSpacing = 8f;
        private const float MinColumnWidth = 132f;

        /// <summary>How far every heading bar is slid sideways to line up with the grids' frames.</summary>
        private float _headingShift;

        /// <summary>When AlignHeadings next measures.</summary>
        private float _nextAlignAt;

        /// <summary>When the one-shot layout dump is due (debug logging only); 0 once written.</summary>
        private float _layoutDumpAt;

        private CabinetSession(List<Entry> display, int cabinetCount)
        {
            _display = display;
            _cabinetCount = cabinetCount;
            Entry opened = display.First(e => e.IsOpened);
            _searchOrder = new List<Entry> { opened };
            _searchOrder.AddRange(display.Where(e => e != opened));
        }

        public SearchableItemItemClass OpenedItem => _display.First(e => e.IsOpened).Item;

        public CompoundItem[] AllItems => _searchOrder.Select(e => (CompoundItem)e.Item).ToArray();

        // ------------------------------------------------------------------ lifecycle

        /// <summary>From the OnContainerOpen prefix: the player is about to open this drawer's loot.</summary>
        public static void Prepare(LootableContainer opened)
        {
            _pending = null;
            if (!CabinetLooterPlugin.Enabled.Value || !Cabinets.IsDrawer(opened) || !Cabinets.IsUsable(opened))
            {
                return;
            }

            Cabinet cabinet = Cabinets.FindCabinet(opened);
            List<Cabinet> cabinets = CabinetLooterPlugin.IncludeNeighbours.Value
                ? Cabinets.FindCluster(cabinet, opened, CabinetLooterPlugin.NeighbourGap.Value, CabinetLooterPlugin.MaxCabinets.Value)
                : new List<Cabinet> { cabinet };

            var display = new List<Entry>();
            for (int c = 0; c < cabinets.Count; c++)
            {
                for (int d = 0; d < cabinets[c].Drawers.Count; d++)
                {
                    LootableContainer drawer = cabinets[c].Drawers[d];
                    display.Add(new Entry
                    {
                        Container = drawer,
                        Item = (SearchableItemItemClass)drawer.ItemOwner.RootItem,
                        CabinetIndex = c,
                        DrawerIndex = d,
                        IsOpened = drawer == opened
                    });
                }
            }

            CabinetLooterPlugin.Debug("Opened " + opened.name + " (" + opened.Id + "): "
                                      + string.Join("; ", cabinets.Select(Cabinets.Describe)));

            // A cabinet with a single usable drawer is just a container; leave it to vanilla.
            if (display.Count < 2)
            {
                return;
            }
            _pending = new CabinetSession(display, cabinets.Count);
        }

        /// <summary>
        /// From the ItemUiContext.Configure postfix. Vanilla registers only the opened drawer as
        /// the loot side. Quick move (Ctrl+click) treats any item not inside a registered loot
        /// container as player-side, so an item in another drawer would be "moved to the loot",
        /// that is, into the opened drawer. Registering every drawer makes Ctrl+click send it to
        /// the player. The opened drawer stays first, so Ctrl+click from the player still targets it.
        /// </summary>
        public static CompoundItem[] RightPanelItemsFor(CompoundItem[] rightPanelItems)
        {
            // Configure runs from the screen's OnShowStart, which should be before the panel
            // shows; the session on screen is checked as well so the order does not matter.
            if (rightPanelItems == null || rightPanelItems.Length != 1)
            {
                return null;
            }
            if (_pending != null && rightPanelItems[0] == _pending.OpenedItem)
            {
                return _pending.AllItems;
            }
            if (_current != null && rightPanelItems[0] == _current.OpenedItem)
            {
                return _current.AllItems;
            }
            // Some other loot screen: a drawer that was being opened never showed.
            _pending = null;
            return null;
        }

        /// <summary>From the SimpleStashPanel.Show postfix: the opened drawer is now on screen.</summary>
        public static void Attach(SimpleStashPanel panel, CompoundItem item, ItemContextAbstractClass itemContext)
        {
            CabinetSession pending = _pending;
            if (pending == null || item != pending.OpenedItem)
            {
                return;
            }
            _pending = null;

            _current?.Detach();
            _current = pending;
            try
            {
                pending.Build(panel, itemContext);
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Could not add the cabinet's other drawers to the loot panel: " + e);
                pending.Detach();
            }
        }

        /// <summary>From the SimpleStashPanel.Close prefix.</summary>
        public static void PanelClosing(SimpleStashPanel panel)
        {
            CabinetSession current = _current;
            if (current != null && current._panel == panel)
            {
                current.Detach();
                // The Gear and Health tabs share one items panel, so switching tab closes this
                // panel and shows it again for the same drawer. Keep the session for that
                // re-show. It is dropped as soon as anything else opens (Prepare, or Configure
                // with other loot), and a fresh opening of this drawer rebuilds it anyway.
                _pending = current;
            }
        }

        /// <summary>
        /// From the SearchableView.UpdateSearchState postfix. Vanilla re-shows the overlay on every
        /// change of the opened drawer's search state; hide it again while the cabinet is up.
        /// </summary>
        public static void SearchStateUpdated(SearchableView view)
        {
            CabinetSession current = _current;
            if (current != null && current._searchableView == view)
            {
                HideOverlay(view);
                current.ApplyTopBar();
            }
        }

        public static void TickCurrent()
        {
            CabinetSession current = _current;
            if (current == null)
            {
                return;
            }
            try
            {
                current.Tick();
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Cabinet search failed; the remaining drawers will not be searched automatically: " + e);
                current.Detach();
            }
        }

        // ------------------------------------------------------------------ building the panel

        private void Build(SimpleStashPanel panel, ItemContextAbstractClass openedContext)
        {
            // A re-show (tab switch) starts the chain again: closing the panel interrupted any
            // search, as vanilla's does. Only the player's own stops are kept.
            foreach (Entry entry in _display)
            {
                entry.Started = false;
                entry.WasSearching = false;
                entry.GridsShown = false;
            }

            _chainStopped = false;
            _panel = panel;
            _searchableView = panel.GetComponent<SearchableView>();
            if (_searchableView != null && SearchButtonRef(_searchableView) != null)
            {
                _unsubscribeSearchButton = SearchButtonRef(_searchableView).OnSearchStatusChanged.Subscribe(SearchButtonToggled);
            }
            _inventoryController = PanelInventoryControllerRef(panel);
            _searcher = _inventoryController?.SearchController as IPlayerSearchController;
            _filterPanel = PanelFilterRef(panel);

            SearchableItemView view = PanelSimplePanelRef(panel);
            Transform content = GridsContainerRef(view);
            _content = content;
            if (_searcher == null || content == null)
            {
                throw new InvalidOperationException(
                    $"panel not as expected: searcher {_searcher != null}, content {content != null}");
            }

            // The other drawers' contexts are made the way the screen made the opened drawer's.
            // In raid the screen's own context is an EmptyItemContext, whose CreateChild returns
            // a DefaultItemContext with no Source; then the same constructor is used directly.
            ItemContextAbstractClass screenContext = openedContext.ItemContextAbstractClass;

            // One row per cabinet, its drawers side by side, so a whole cabinet (and a cluster of
            // up to four) is visible without scrolling. Columns share the visible width of the
            // scroll area (Content's parent): four 2x2 drawers need 126 px each, and the panel
            // is about 630 px wide.
            float available = content.parent is RectTransform viewport ? viewport.rect.width - 8f : 0f;
            if (available < 300f)
            {
                available = 600f;
            }
            int perRow = Math.Max(1, _display.Max(e => e.DrawerIndex) + 1);
            float columnWidth = Mathf.Max(MinColumnWidth, Mathf.Floor((available - ColumnSpacing * (perRow - 1)) / perRow));

            TextMeshProUGUI fontSource = PanelContainerNameRef(panel);
            int index = 0;
            Transform row = null;
            for (int i = 0; i < _display.Count; i++)
            {
                Entry entry = _display[i];
                if (entry.DrawerIndex == 0)
                {
                    if (i > 0)
                    {
                        AddDecoration(DrawerHeading.CreateSpacer(content, 10f), ref index);
                    }
                    if (_cabinetCount > 1)
                    {
                        AddDecoration(DrawerHeading.CreateTitle(content, fontSource, available, "CABINET " + (entry.CabinetIndex + 1)), ref index);
                    }
                    GameObject rowObject = DrawerHeading.CreateRow(content, ColumnSpacing);
                    AddDecoration(rowObject, ref index);
                    row = rowObject.transform;
                }

                Transform column = DrawerHeading.CreateColumn(row, columnWidth).transform;
                entry.Heading = DrawerHeading.Create(column, fontSource, columnWidth, entry.IsOpened, () => HeadingClicked(entry));

                if (entry.IsOpened)
                {
                    // Vanilla's own grid view for the opened drawer, moved into its column. It is
                    // handed back to Content on Detach, before the column is destroyed, so vanilla
                    // still owns and disposes it.
                    _openedGrids = ContainedGridsViewRef(view);
                    if (_openedGrids != null)
                    {
                        _openedGrids.transform.SetParent(column, false);
                    }
                    continue;
                }

                entry.RawContext = screenContext != null
                    ? screenContext.CreateChild(entry.Item)
                    : new GClass3453(entry.Item, openedContext.ViewType);
                entry.Context = (entry.RawContext as GClass3458)
                                ?? GClass3458.CreateFromDefaultContext(entry.RawContext);

                ContainedGridsView grids = ContainedGridsView.CreateGrids(entry.Item, ContainedGridsTemplateRef(view));
                if (grids != null)
                {
                    grids.gameObject.SetActive(false);
                    grids.transform.SetParent(column, false);
                    entry.Grids = grids;
                }
            }

            // The scroll wheel reaches the scroll area only through whatever UI element the mouse is
            // over. Vanilla's single grid fills the panel, so that is always something; here the
            // space beside the 2x2 grids is empty and the wheel did nothing over it. An invisible
            // full-size target behind everything gives the wheel something to land on there; it
            // handles no events itself, so they bubble up to the scroll area.
            GameObject catcher = DrawerHeading.CreateScrollCatcher(content);
            catcher.transform.SetAsFirstSibling();
            _decorations.Add(catcher);

            if (_searchableView != null)
            {
                HideOverlay(_searchableView);
            }

            CabinetLooterPlugin.Debug($"Showing {_display.Count} drawers from {_cabinetCount} cabinet(s), {perRow} to a row, columns {columnWidth:F0} wide.");
            foreach (Entry entry in _display)
            {
                // What the drawer actually holds, straight from the item, whatever the panel shows.
                List<Item> items = entry.Item.GetFirstLevelItems().ToList();
                CabinetLooterPlugin.Debug($"  {entry.LogName}{(entry.IsOpened ? " (opened)" : string.Empty)}: "
                                          + $"{items.Count} item(s) [{string.Join(", ", items.Select(i => i.TemplateId.ToString()))}], "
                                          + $"searched {_searcher.IsSearched(entry.Item)}, unknown items {_searcher.ContainsUnknownItems(entry.Item)}");
            }

            _layoutDumpAt = CabinetLooterPlugin.DebugLogging.Value ? Time.unscaledTime + 3f : 0f;
            Tick();
        }

        private void AddDecoration(GameObject decoration, ref int index)
        {
            decoration.transform.SetSiblingIndex(index++);
            _decorations.Add(decoration);
        }

        private static void HideOverlay(SearchableView view)
        {
            if (UnsearchedPanelRef(view) != null && UnsearchedPanelRef(view).gameObject.activeSelf)
            {
                UnsearchedPanelRef(view).gameObject.SetActive(false);
            }
        }

        private void Detach()
        {
            if (_current == this)
            {
                _current = null;
            }

            _unsubscribeSearchButton?.Invoke();
            _unsubscribeSearchButton = null;

            // Vanilla's grid view goes back where vanilla put it before our rows are destroyed,
            // or it would be destroyed with them under vanilla's feet.
            if (_openedGrids != null && _content != null)
            {
                _openedGrids.transform.SetParent(_content, false);
            }
            _openedGrids = null;

            foreach (Entry entry in _display)
            {
                try
                {
                    // Closing the window interrupts a search, as vanilla does when its view
                    // closes. Found items stay found. The opened drawer's search is stopped by
                    // vanilla's own view, so it is left alone here.
                    if (!entry.IsOpened && _searcher != null && IsSearching(entry))
                    {
                        _searcher.StopSearching(entry.Item.Id);
                    }
                    if (entry.Grids != null)
                    {
                        if (entry.GridsShown)
                        {
                            entry.Grids.Close();
                        }
                        UnityEngine.Object.Destroy(entry.Grids.gameObject);
                    }
                    entry.Grids = null;
                    entry.Context?.Dispose();
                    entry.RawContext?.Dispose();
                    entry.Context = null;
                    entry.RawContext = null;
                    if (entry.Heading != null && entry.Heading.Root != null)
                    {
                        UnityEngine.Object.Destroy(entry.Heading.Root);
                    }
                    entry.Heading = null;
                }
                catch (Exception e)
                {
                    CabinetLooterPlugin.Log.LogError("Could not tidy up " + entry.LogName + ": " + e);
                }
            }

            foreach (GameObject decoration in _decorations)
            {
                if (decoration != null)
                {
                    UnityEngine.Object.Destroy(decoration);
                }
            }
            _decorations.Clear();

            // The overlay is not restored here: vanilla recomputes it on the next showing.
            _panel = null;
            _searchableView = null;
        }

        // ------------------------------------------------------------------ searching

        private void Tick()
        {
            if (_panel == null)
            {
                // Destroyed without closing (the raid ended under it).
                Detach();
                return;
            }
            if (!_panel.gameObject.activeInHierarchy)
            {
                // Hidden behind another tab; carry on when it shows again.
                return;
            }

            foreach (Entry entry in _display)
            {
                bool searching = IsSearching(entry);
                if (searching)
                {
                    entry.WasSearching = true;
                }
                else if (entry.WasSearching)
                {
                    entry.WasSearching = false;
                    if (NeedsSearch(entry))
                    {
                        // The search ended with items still hidden: the player pressed stop, or it
                        // failed. Either way, do not start it again by ourselves.
                        entry.Stopped = true;
                        CabinetLooterPlugin.Debug(entry.LogName + " stopped before it finished.");
                    }
                }

                if (!entry.GridsShown && entry.Grids != null && _searcher.IsSearched(entry.Item))
                {
                    entry.Grids.Show(entry.Item, entry.Context, _inventoryController, _filterPanel, ItemUiContext.Instance);
                    entry.GridsShown = true;
                }

                UpdateHeading(entry, searching);
            }

            AlignHeadings();

            if (_layoutDumpAt > 0f && Time.unscaledTime >= _layoutDumpAt)
            {
                _layoutDumpAt = 0f;
                DumpLayout();
            }

            if (CabinetLooterPlugin.AutoSearch.Value && !_chainStopped && CanStartSearch())
            {
                Entry next = _searchOrder.FirstOrDefault(e => !e.Started && !e.Stopped && !IsSearching(e) && NeedsSearch(e));
                if (next != null)
                {
                    StartSearch(next);
                }
            }
        }

        /// <summary>
        /// Lines every heading's left edge up with the left edge of what is drawn below it. The
        /// grid view's own box starts exactly at the column's left edge, same as the heading, but
        /// its frame is drawn a little outside that box, so measured against the box (1.0.1) the
        /// gap was zero and the frame still stuck out a pixel or two left of the heading. So this
        /// measures the leftmost thing actually drawn in the grid view. Every grid view comes from
        /// the one template, so the gap measured on any shown grid applies to every heading,
        /// including drawers whose grid is still hidden. Measured a few times a second, not every
        /// frame, as it walks the grid view's graphics.
        /// </summary>
        private void AlignHeadings()
        {
            if (Time.unscaledTime < _nextAlignAt)
            {
                return;
            }
            _nextAlignAt = Time.unscaledTime + 0.25f;

            foreach (Entry entry in _display)
            {
                ContainedGridsView grids = entry.IsOpened ? _openedGrids : entry.Grids;
                if (entry.Heading == null || grids == null || !grids.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Graphic leftmost = LeftmostGraphic(grids, out float left);
                if (leftmost == null)
                {
                    continue;
                }
                float gap = entry.Heading.LeftEdgeGap(left);
                if (Mathf.Abs(gap) > 20f)
                {
                    // Not a pixel of drift: something other than the plain grid frame. Leave it.
                    continue;
                }
                if (Mathf.Abs(gap - _headingShift) >= 0.01f)
                {
                    _headingShift = gap;
                    CabinetLooterPlugin.Debug($"Headings shifted {gap:F2} to line up with {leftmost.name} ({leftmost.GetType().Name}) of {entry.LogName}.");
                }
                break;
            }

            foreach (Entry entry in _display)
            {
                entry.Heading?.Shift(_headingShift);
            }
        }

        /// <summary>The leftmost visible graphic inside a grid view, and its left edge in world space.</summary>
        private static Graphic LeftmostGraphic(ContainedGridsView grids, out float left)
        {
            Graphic best = null;
            left = float.MaxValue;
            grids.GetComponentsInChildren(false, GraphicsBuffer);
            foreach (Graphic graphic in GraphicsBuffer)
            {
                if (!graphic.enabled || graphic.color.a <= 0.01f || graphic.canvasRenderer.cull)
                {
                    continue;
                }
                graphic.rectTransform.GetWorldCorners(Corners);
                if (Corners[0].x < left)
                {
                    left = Corners[0].x;
                    best = graphic;
                }
            }
            GraphicsBuffer.Clear();
            return best;
        }

        private static readonly List<Graphic> GraphicsBuffer = new List<Graphic>();
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// Debug only, once per showing, a few seconds in: what Content's layout does and where
        /// every child ended up. Written so a panel that looks empty can be told apart from
        /// drawers that are empty: items listed at Build with grids here sized 0 or off screen
        /// is a layout fault, not missing loot.
        /// </summary>
        private void DumpLayout()
        {
            if (_content == null)
            {
                return;
            }
            var content = (RectTransform)_content;
            var group = _content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            var fitter = _content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            CabinetLooterPlugin.Debug($"Layout: Content size {content.rect.size}, pivot {content.pivot}, anchored {content.anchoredPosition}; "
                                      + (group != null
                                          ? $"group controls width {group.childControlWidth} height {group.childControlHeight}, expands width {group.childForceExpandWidth} height {group.childForceExpandHeight}, spacing {group.spacing}; "
                                          : "no VerticalLayoutGroup; ")
                                      + (fitter != null ? $"fitter {fitter.horizontalFit}/{fitter.verticalFit}" : "no ContentSizeFitter"));
            if (_content.parent is RectTransform viewport)
            {
                CabinetLooterPlugin.Debug($"Layout: viewport {viewport.name} size {viewport.rect.size}");
            }
            DumpChildren(_content, "  ", 3);
        }

        private static void DumpChildren(Transform parent, string indent, int depth)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = (RectTransform)parent.GetChild(i);
                CabinetLooterPlugin.Debug($"Layout: {indent}[{i}] {child.name} active {child.gameObject.activeSelf} size {child.rect.size} at {child.anchoredPosition}");
                if (depth > 1 && child.name.StartsWith("CabinetLooter"))
                {
                    DumpChildren(child, indent + "  ", depth - 1);
                }
            }
        }

        /// <summary>
        /// Keeps the top bar's "SEARCHING", X and timer up for whichever drawer of the cabinet is
        /// being searched. Vanilla's bar only ever follows the opened drawer, so it vanished as soon
        /// as that one finished while the others were still being searched. Runs after every
        /// vanilla refresh of the bar (it refreshes on every search starting or ending); when the
        /// opened drawer itself is searching, or nothing is, vanilla's own state stands.
        /// </summary>
        private void ApplyTopBar()
        {
            if (_searchableView == null || _searcher == null || _panel == null)
            {
                return;
            }

            SearchContentOperation operation = null;
            Entry active = null;
            foreach (Entry entry in _searchOrder)
            {
                operation = _searcher.SearchOperations.FirstOrDefault(op => op.Item == entry.Item);
                if (operation != null)
                {
                    active = entry;
                    break;
                }
            }
            if (active != null && active.IsOpened)
            {
                return;
            }

            DateTime startTime;
            if (active != null)
            {
                startTime = operation.StartTime;
            }
            else if (ChainHasNext())
            {
                // Between two drawers: one search has ended and the chain starts the next on
                // its next tick. Keep the bar up through that switch, timer at zero.
                startTime = EFTDateTimeClass.UtcNow;
            }
            else
            {
                return;
            }

            // The same three calls, in the same order, that vanilla makes for a running search.
            SearchButton button = SearchButtonRef(_searchableView);
            if (button != null)
            {
                button.SetEnabled(true);
                button.gameObject.SetActive(true);
                button.SetSearchStatus(true);
            }
            if (SearchTimerRef(_searchableView) != null)
            {
                SearchTimerRef(_searchableView).Show(startTime);
            }
        }

        /// <summary>
        /// The chain will start another drawer on its next tick: the same test Tick applies,
        /// so the bar is never held up for a search that will not come.
        /// </summary>
        private bool ChainHasNext()
        {
            return CabinetLooterPlugin.AutoSearch.Value
                   && !_chainStopped
                   && CanStartSearch()
                   && _searchOrder.Any(e => !e.Started && !e.Stopped && !IsSearching(e) && NeedsSearch(e));
        }

        /// <summary>
        /// The top bar's button or its X. Vanilla's own handler only acts on the opened drawer;
        /// this one makes X stop the whole cabinet: the running searches and the rest of the chain.
        /// Pressing SEARCH again lets the chain carry on.
        /// </summary>
        private void SearchButtonToggled(bool search)
        {
            if (_searcher == null || _panel == null)
            {
                return;
            }
            if (!search)
            {
                _chainStopped = true;
                foreach (Entry entry in _display)
                {
                    if (!entry.IsOpened && IsSearching(entry))
                    {
                        entry.Stopped = true;
                        _searcher.StopSearching(entry.Item.Id);
                    }
                }
                CabinetLooterPlugin.Debug("Search stopped from the top bar; no more automatic searches this showing.");
            }
            else
            {
                _chainStopped = false;
                foreach (Entry entry in _display)
                {
                    entry.Stopped = false;
                    entry.Started = false;
                }
            }
        }

        /// <summary>
        /// A search can start: the searcher has a free slot, and the player is alive. A search
        /// operation yields a frame before it runs, so one started in the frame the player dies
        /// would run against a disposed player.
        /// </summary>
        private bool CanStartSearch()
        {
            if (!_searcher.CanSearch || !_searcher.CanStartNewSearchOperation())
            {
                return false;
            }
            Player player = (_inventoryController as Player.PlayerInventoryController)?.Player_0;
            return player == null || (player.HealthController != null && player.HealthController.IsAlive);
        }

        private void HeadingClicked(Entry entry)
        {
            if (_searcher == null || _panel == null)
            {
                return;
            }
            if (IsSearching(entry))
            {
                entry.Stopped = true;
                _searcher.StopSearching(entry.Item.Id);
            }
            else if (NeedsSearch(entry) && CanStartSearch())
            {
                entry.Stopped = false;
                StartSearch(entry);
            }
        }

        private void StartSearch(Entry entry)
        {
            entry.Started = true;
            entry.WasSearching = true;
            CabinetLooterPlugin.Debug("Searching " + entry.LogName + " (" + entry.Container.Id + ").");
            _searcher.SearchContents(entry.Item);
        }

        private bool IsSearching(Entry entry)
        {
            return _searcher.SearchOperations.Any(op => op.Item == entry.Item);
        }

        /// <summary>
        /// Never searched, or (with the resume option) searched but interrupted before every
        /// item was found. The same test the game's own search operation uses.
        /// </summary>
        private bool NeedsSearch(Entry entry)
        {
            if (!_searcher.IsSearched(entry.Item))
            {
                return true;
            }
            return CabinetLooterPlugin.ResumePartial.Value && _searcher.ContainsUnknownItems(entry.Item);
        }

        private void UpdateHeading(Entry entry, bool searching)
        {
            if (entry.Heading == null)
            {
                return;
            }

            if (searching)
            {
                entry.Heading.Set(entry.Label, "SEARCHING", DrawerHeading.SearchingColor);
            }
            else if (!_searcher.IsSearched(entry.Item))
            {
                entry.Heading.Set(entry.Label, "NOT SEARCHED", DrawerHeading.NotSearchedColor);
            }
            else if (_searcher.ContainsUnknownItems(entry.Item))
            {
                entry.Heading.Set(entry.Label, "PARTLY SEARCHED", DrawerHeading.PartlyColor);
            }
            else if (!entry.Item.GetFirstLevelItems().Any())
            {
                entry.Heading.Set(entry.Label, "EMPTY", DrawerHeading.EmptyColor);
            }
            else
            {
                int count = entry.Item.GetFirstLevelItems().Count();
                entry.Heading.Set(entry.Label, count == 1 ? "1 ITEM" : count + " ITEMS", DrawerHeading.FoundColor);
            }
        }
    }
}
