using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
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
    /// The search button, timer and "unsearched" overlay belong to the panel, not to a drawer, so
    /// they stay with the opened drawer. The other drawers say their state in their heading, and
    /// clicking a heading starts or stops that drawer's search.
    /// </summary>
    internal sealed class CabinetSession
    {
        private sealed class Entry
        {
            public LootableContainer Container;
            public SearchableItem Item;
            public string Label;
            public bool IsOpened;

            public TextMeshProUGUI Heading;
            public string HeadingText;
            public ContainedGridsView Grids;
            public ItemContext RawContext;
            public InventorySelectableItemContext Context;
            public bool GridsShown;

            /// <summary>Seen searching since the last tick; a search that then vanishes unfinished was stopped.</summary>
            public bool WasSearching;

            /// <summary>Stopped by the player, or failed. Not started again automatically while the panel is open.</summary>
            public bool Stopped;

            /// <summary>Started by this mod once already. A drawer is never auto-started twice per opening.</summary>
            public bool Started;
        }

        private static readonly AccessTools.FieldRef<SearchableItemView, ContainedGridsView> ContainedGridsViewRef =
            AccessTools.FieldRefAccess<SearchableItemView, ContainedGridsView>("_containedGridsView");

        private static readonly AccessTools.FieldRef<SimpleStashPanel, InventoryController> PanelInventoryControllerRef =
            AccessTools.FieldRefAccess<SimpleStashPanel, InventoryController>("_inventoryController");

        /// <summary>Built when a drawer is opened, waiting for its loot panel.</summary>
        private static CabinetSession _pending;

        /// <summary>The session whose sections are on screen.</summary>
        private static CabinetSession _current;

        private readonly List<Entry> _display;
        private readonly List<Entry> _searchOrder;
        private readonly int _cabinetCount;

        private SimpleStashPanel _panel;
        private InventoryController _inventoryController;
        private IPlayerSearchController _searcher;
        private FilterPanel _filterPanel;

        private CabinetSession(List<Entry> display, int cabinetCount)
        {
            _display = display;
            _cabinetCount = cabinetCount;
            Entry opened = display.First(e => e.IsOpened);
            _searchOrder = new List<Entry> { opened };
            _searchOrder.AddRange(display.Where(e => e != opened));
        }

        public SearchableItem OpenedItem => _display.First(e => e.IsOpened).Item;

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
                        Item = (SearchableItem)drawer.ItemOwner.RootItem,
                        Label = cabinets.Count > 1 ? $"CABINET {c + 1}, DRAWER {d + 1}" : $"DRAWER {d + 1}",
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
        public static void Attach(SimpleStashPanel panel, CompoundItem item, ItemContext itemContext)
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

        private void Build(SimpleStashPanel panel, ItemContext openedContext)
        {
            // A re-show (tab switch) starts the chain again: closing the panel interrupted any
            // search, as vanilla's does. Only the player's own stops are kept.
            foreach (Entry entry in _display)
            {
                entry.Started = false;
                entry.WasSearching = false;
                entry.GridsShown = false;
                entry.HeadingText = null;
            }

            _panel = panel;
            _inventoryController = PanelInventoryControllerRef(panel);
            _searcher = _inventoryController?.SearchController as IPlayerSearchController;
            _filterPanel = panel._filterPanel;

            SearchableItemView view = panel._simplePanel;
            Transform content = view._gridsContainer;
            // The other drawers' contexts are made the way the screen made the opened drawer's.
            // In raid the screen's own context is an EmptyItemContext, whose CreateChild returns
            // a DefaultItemContext with no Source; then the same constructor is used directly.
            ItemContext screenContext = openedContext.Source;
            if (_searcher == null || content == null)
            {
                throw new InvalidOperationException(
                    $"panel not as expected: searcher {_searcher != null}, content {content != null}");
            }

            TextMeshProUGUI fontSource = panel._containerName;
            int index = 0;
            foreach (Entry entry in _display)
            {
                entry.Heading = CreateHeading(content, fontSource, entry);
                entry.Heading.transform.SetSiblingIndex(index++);

                if (entry.IsOpened)
                {
                    // Vanilla's own grid view for the opened drawer; only moved into place.
                    ContainedGridsView openedGrids = ContainedGridsViewRef(view);
                    if (openedGrids != null)
                    {
                        openedGrids.transform.SetSiblingIndex(index++);
                    }
                    continue;
                }

                entry.RawContext = screenContext != null
                    ? screenContext.CreateChild(entry.Item)
                    : new DefaultItemContext(entry.Item, openedContext.ViewType);
                entry.Context = (entry.RawContext as InventorySelectableItemContext)
                                ?? InventorySelectableItemContext.CreateFromDefaultContext(entry.RawContext);

                ContainedGridsView grids = ContainedGridsView.CreateGrids(entry.Item, view._containedGridsTemplate);
                if (grids != null)
                {
                    grids.gameObject.SetActive(false);
                    grids.transform.SetParent(content, false);
                    grids.transform.SetSiblingIndex(index++);
                    entry.Grids = grids;
                }
            }

            CabinetLooterPlugin.Debug($"Showing {_display.Count} drawers from {_cabinetCount} cabinet(s).");
            Tick();
        }

        private TextMeshProUGUI CreateHeading(Transform content, TextMeshProUGUI fontSource, Entry entry)
        {
            var go = new GameObject("CabinetLooter Heading", typeof(RectTransform));
            go.transform.SetParent(content, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(320f, 24f);

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 24f;
            layout.preferredHeight = 24f;

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (fontSource != null)
            {
                text.font = fontSource.font;
                text.fontSharedMaterial = fontSource.fontSharedMaterial;
                text.color = fontSource.color;
            }
            text.fontSize = 15f;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = text;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => HeadingClicked(entry));
            return text;
        }

        private void Detach()
        {
            if (_current == this)
            {
                _current = null;
            }

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
                        entry.Grids = null;
                    }
                    entry.Context?.Dispose();
                    entry.RawContext?.Dispose();
                    entry.Context = null;
                    entry.RawContext = null;
                    if (entry.Heading != null)
                    {
                        UnityEngine.Object.Destroy(entry.Heading.gameObject);
                        entry.Heading = null;
                    }
                }
                catch (Exception e)
                {
                    CabinetLooterPlugin.Log.LogError("Could not tidy up drawer " + entry.Label + ": " + e);
                }
            }
            _panel = null;
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
                        CabinetLooterPlugin.Debug(entry.Label + " stopped before it finished.");
                    }
                }

                if (!entry.GridsShown && entry.Grids != null && _searcher.IsSearched(entry.Item))
                {
                    entry.Grids.Show(entry.Item, entry.Context, _inventoryController, _filterPanel, ItemUiContext.Instance);
                    entry.GridsShown = true;
                }

                UpdateHeading(entry, searching);
            }

            if (CabinetLooterPlugin.AutoSearch.Value && _searcher.CanSearch && _searcher.CanStartNewSearchOperation())
            {
                Entry next = _searchOrder.FirstOrDefault(e => !e.Started && !e.Stopped && !IsSearching(e) && NeedsSearch(e));
                if (next != null)
                {
                    StartSearch(next);
                }
            }
        }

        private void HeadingClicked(Entry entry)
        {
            if (_searcher == null || !_searcher.CanSearch)
            {
                return;
            }
            if (IsSearching(entry))
            {
                entry.Stopped = true;
                _searcher.StopSearching(entry.Item.Id);
            }
            else if (NeedsSearch(entry) && _searcher.CanStartNewSearchOperation())
            {
                entry.Stopped = false;
                StartSearch(entry);
            }
        }

        private void StartSearch(Entry entry)
        {
            entry.Started = true;
            entry.WasSearching = true;
            CabinetLooterPlugin.Debug("Searching " + entry.Label + " (" + entry.Container.Id + ").");
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

            string state;
            if (searching)
            {
                state = "SEARCHING...";
            }
            else if (!_searcher.IsSearched(entry.Item))
            {
                state = "NOT SEARCHED - CLICK TO SEARCH";
            }
            else if (_searcher.ContainsUnknownItems(entry.Item))
            {
                state = "PARTLY SEARCHED - CLICK TO RESUME";
            }
            else if (!entry.Item.GetFirstLevelItems().Any())
            {
                state = "EMPTY";
            }
            else
            {
                state = null;
            }

            string text = entry.Label
                          + (entry.IsOpened ? " (OPENED)" : string.Empty)
                          + (state != null ? "  -  " + state : string.Empty);
            if (text != entry.HeadingText)
            {
                entry.HeadingText = text;
                entry.Heading.text = text;
            }
        }
    }
}
