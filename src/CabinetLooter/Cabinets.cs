using System.Collections.Generic;
using System.Linq;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace CabinetLooter
{
    /// <summary>One physical filing cabinet: its usable drawers, top to bottom.</summary>
    internal sealed class Cabinet
    {
        /// <summary>
        /// The cabinet's own GameObject (named <c>card_file</c> on every map), or the drawer itself
        /// for a drawer that has no parent.
        /// </summary>
        public Transform Root;

        public List<LootableContainer> Drawers;

        /// <summary>World bounds of the drawers' interaction colliders: the cabinet's front face and depth.</summary>
        public Bounds Bounds;
    }

    /// <summary>
    /// Finds the drawers of a cabinet, and optionally the cabinets standing next to it.
    ///
    /// What this relies on was read off the scene files of all ten maps (2026-10-03), not assumed:
    /// every drawer is a <see cref="LootableContainer"/> on a GameObject <c>card_file_box_0N</c>,
    /// and the drawers of one cabinet are the direct children of one <c>card_file</c> GameObject.
    /// No map has a drawer outside a cabinet or a parent holding two cabinets. Most cabinets have
    /// four drawers; some on Lighthouse, Ground Zero and Streets have one to three.
    ///
    /// The server's container group ids are no use here: they are spawn budgets spanning a whole
    /// area (Customs' <c>card_files</c> covers 14 cabinets). Drawer ids are no use either:
    /// <c>Lootable_00005..8</c> occur on every map.
    ///
    /// All four drawer GameObjects share one pivot; each drawer's height is in its child meshes
    /// and its interaction BoxCollider. So drawers are ordered by collider bounds, never by
    /// transform position.
    /// </summary>
    internal static class Cabinets
    {
        /// <summary>The "Drawer" container template. Every filing-cabinet drawer uses it.</summary>
        public const string DrawerTemplateId = "578f87b7245977356274f2cd";

        private static readonly Collider[] OverlapBuffer = new Collider[128];
        private static readonly RaycastHit[] RayBuffer = new RaycastHit[32];

        public static bool IsDrawer(LootableContainer container)
        {
            if (container == null)
            {
                return false;
            }
            if (container.Template == DrawerTemplateId)
            {
                return true;
            }
            Item root = container.ItemOwner?.RootItem;
            return root != null && root.TemplateId.ToString() == DrawerTemplateId;
        }

        /// <summary>
        /// A drawer that can be shown: it has its loot (its item owner is created when the raid's
        /// loot is spawned), it is in the scene, and it is not locked.
        /// </summary>
        public static bool IsUsable(LootableContainer container)
        {
            return container != null
                   && container.ItemOwner != null
                   && container.ItemOwner.RootItem is SearchableItem
                   && container.gameObject.activeInHierarchy
                   && container.DoorState != EDoorState.Locked;
        }

        public static Cabinet FindCabinet(LootableContainer drawer)
        {
            Transform parent = drawer.transform.parent;
            var drawers = new List<LootableContainer>();

            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    LootableContainer sibling = parent.GetChild(i).GetComponent<LootableContainer>();
                    if (IsDrawer(sibling) && IsUsable(sibling))
                    {
                        drawers.Add(sibling);
                    }
                }
            }
            if (!drawers.Contains(drawer))
            {
                drawers.Add(drawer);
            }

            drawers = drawers
                .OrderByDescending(d => DrawerBounds(d).center.y)
                .ThenBy(d => d.name)
                .ToList();

            Bounds bounds = DrawerBounds(drawers[0]);
            for (int i = 1; i < drawers.Count; i++)
            {
                bounds.Encapsulate(DrawerBounds(drawers[i]));
            }

            return new Cabinet
            {
                Root = parent != null ? parent : drawer.transform,
                Drawers = drawers,
                Bounds = bounds
            };
        }

        /// <summary>
        /// The opened cabinet first, then the cabinets touching it (and touching those), nearest
        /// first, up to <paramref name="maxCabinets"/>. A neighbour must be within
        /// <paramref name="gap"/> metres side to side, and nothing but the two cabinets themselves
        /// may lie on the line between their centres, so a cabinet behind a wall is left out.
        ///
        /// Cost: one small box overlap per cabinet in the cluster, once, when the drawer is opened.
        /// </summary>
        public static List<Cabinet> FindCluster(Cabinet start, LootableContainer openedDrawer, float gap, int maxCabinets)
        {
            var cluster = new List<Cabinet> { start };
            var seen = new HashSet<Transform> { start.Root };
            var queue = new Queue<Cabinet>();
            queue.Enqueue(start);

            // The layer the drawers' interaction colliders are on, taken from the drawer the player
            // just opened rather than from a named mask, so a mask that silently excludes it
            // cannot turn this into a no-op.
            int drawerMask = 1 << openedDrawer.gameObject.layer;

            // A long row of cabinets would otherwise be walked to its end; only the nearest
            // maxCabinets are kept, so a few more than that is enough to choose from.
            while (queue.Count > 0 && cluster.Count < maxCabinets * 2)
            {
                Cabinet current = queue.Dequeue();
                Bounds search = current.Bounds;
                search.Expand(gap * 2f);

                int count = Physics.OverlapBoxNonAlloc(
                    search.center, search.extents, OverlapBuffer, Quaternion.identity,
                    drawerMask, QueryTriggerInteraction.Collide);
                if (count == OverlapBuffer.Length)
                {
                    CabinetLooterPlugin.Debug("Neighbour search filled its buffer; some neighbours may be missed.");
                }

                var found = new List<Cabinet>();
                for (int i = 0; i < count; i++)
                {
                    LootableContainer container = OverlapBuffer[i].GetComponentInParent<LootableContainer>();
                    if (!IsDrawer(container) || !IsUsable(container))
                    {
                        continue;
                    }
                    Transform root = container.transform.parent != null ? container.transform.parent : container.transform;
                    if (!seen.Add(root))
                    {
                        continue;
                    }

                    Cabinet neighbour = FindCabinet(container);
                    if (!neighbour.Bounds.Intersects(search))
                    {
                        continue;
                    }
                    if (!NothingBetween(current, neighbour))
                    {
                        CabinetLooterPlugin.Debug("Left out cabinet " + Describe(neighbour) + ": something is between it and " + Describe(current) + ".");
                        continue;
                    }
                    found.Add(neighbour);
                }

                foreach (Cabinet neighbour in found)
                {
                    cluster.Add(neighbour);
                    queue.Enqueue(neighbour);
                }
            }

            Vector3 origin = start.Bounds.center;
            return cluster
                .Take(1)
                .Concat(cluster.Skip(1).OrderBy(c => (c.Bounds.center - origin).sqrMagnitude))
                .Take(maxCabinets)
                .ToList();
        }

        private static bool NothingBetween(Cabinet a, Cabinet b)
        {
            Vector3 from = a.Bounds.center;
            Vector3 to = b.Bounds.center;
            Vector3 direction = to - from;
            float distance = direction.magnitude;
            if (distance < 0.001f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(
                from, direction / distance, RayBuffer, distance,
                LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = RayBuffer[i].transform;
                if (!hit.IsChildOf(a.Root) && !hit.IsChildOf(b.Root))
                {
                    return false;
                }
            }
            return true;
        }

        private static Bounds DrawerBounds(LootableContainer drawer)
        {
            Collider collider = drawer.GetComponent<Collider>();
            if (collider != null && collider.enabled)
            {
                return collider.bounds;
            }
            return new Bounds(drawer.transform.position, Vector3.zero);
        }

        public static string Describe(Cabinet cabinet)
        {
            return cabinet.Root.name + " at " + cabinet.Bounds.center.ToString("F2")
                   + " (" + cabinet.Drawers.Count + " drawers)";
        }
    }
}
