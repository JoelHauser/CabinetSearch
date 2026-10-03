using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Insurance;
using HarmonyLib;

namespace CabinetLooter
{
    /// <summary>
    /// Five hooks, each on the one place in the client where its job happens:
    ///
    /// 1. <see cref="InteractionContextHelper.OnContainerOpen"/>, which every container's
    ///    Search/Open goes through, is where the cabinet is worked out.
    /// 2. <see cref="ItemUiContext.Configure"/> takes the loot side as a CompoundItem[] already;
    ///    every drawer is put in it so Ctrl+click knows they are all loot.
    /// 3. <see cref="SimpleStashPanel.Show"/> is where the opened drawer reaches the screen; the
    ///    other drawers are added beside it.
    /// 4. <see cref="SimpleStashPanel.Close"/> takes them away again.
    /// 5. <see cref="SearchableView.UpdateSearchState"/> re-shows the "unsearched" overlay over
    ///    the whole scroll area; it is hidden again while the cabinet is shown.
    ///
    /// Nothing here skips or replaces the game's code; every patch runs alongside it.
    /// </summary>
    internal static class Patches
    {
        private static readonly AccessTools.FieldRef<ItemUiContext, CompoundItem[]> RightPanelItemsRef =
            AccessTools.FieldRefAccess<ItemUiContext, CompoundItem[]>("_rightPanelItem");

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                Require(AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.OnContainerOpen),
                    new[] { typeof(GamePlayerOwner), typeof(Action), typeof(LootableContainer), typeof(float) })),
                prefix: new HarmonyMethod(typeof(Patches), nameof(OnContainerOpenPrefix)));

            // Configure has two overloads; this is the one that takes the loot side.
            harmony.Patch(
                Require(AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.Configure),
                    new[]
                    {
                        typeof(ItemController), typeof(Profile), typeof(IEftSession), typeof(InsuranceCompany),
                        typeof(Trader), typeof(IHealthController), typeof(CompoundItem[]), typeof(EItemUiContextType),
                        typeof(ECursorResult), typeof(CompoundItem), typeof(InventoryEquipment), typeof(QuestController)
                    })),
                postfix: new HarmonyMethod(typeof(Patches), nameof(ConfigurePostfix)));

            harmony.Patch(
                Require(AccessTools.Method(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Show),
                    new[]
                    {
                        typeof(CompoundItem), typeof(InventoryController), typeof(ItemContext), typeof(bool),
                        typeof(SortingTable), typeof(SimpleStashPanel.EStashSearchAvailability),
                        typeof(InventoryController), typeof(ItemsPanel.EItemsTab)
                    })),
                postfix: new HarmonyMethod(typeof(Patches), nameof(ShowPostfix)));

            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Close), Type.EmptyTypes)),
                prefix: new HarmonyMethod(typeof(Patches), nameof(ClosePrefix)));

            harmony.Patch(
                Require(AccessTools.DeclaredMethod(typeof(SearchableView), nameof(SearchableView.UpdateSearchState), Type.EmptyTypes)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(UpdateSearchStatePostfix)));
        }

        private static MethodInfo Require(MethodInfo method)
        {
            if (method == null)
            {
                throw new MissingMethodException("A patch target was not found in this client.");
            }
            return method;
        }

        private static void OnContainerOpenPrefix(GamePlayerOwner owner, LootableContainer lootableContainer)
        {
            try
            {
                if (owner != null && owner.Player != null && owner.Player.IsYourPlayer)
                {
                    CabinetSession.Prepare(lootableContainer);
                }
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Could not work out the cabinet; this drawer opens as vanilla: " + e);
            }
        }

        private static void ConfigurePostfix(ItemUiContext __instance, CompoundItem[] rightPanelItems)
        {
            try
            {
                CompoundItem[] all = CabinetSession.RightPanelItemsFor(rightPanelItems);
                if (all != null)
                {
                    RightPanelItemsRef(__instance) = all;
                }
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Could not register the cabinet's drawers for quick move: " + e);
            }
        }

        private static void ShowPostfix(SimpleStashPanel __instance, CompoundItem item, ItemContext itemContext)
        {
            CabinetSession.Attach(__instance, item, itemContext);
        }

        private static void UpdateSearchStatePostfix(SearchableView __instance)
        {
            try
            {
                CabinetSession.SearchStateUpdated(__instance);
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Could not hide the unsearched overlay: " + e);
            }
        }

        private static void ClosePrefix(SimpleStashPanel __instance)
        {
            try
            {
                CabinetSession.PanelClosing(__instance);
            }
            catch (Exception e)
            {
                CabinetLooterPlugin.Log.LogError("Could not close the cabinet's drawers cleanly: " + e);
            }
        }
    }
}
