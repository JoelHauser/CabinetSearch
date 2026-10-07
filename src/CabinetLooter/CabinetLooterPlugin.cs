using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CabinetLooter
{
    /// <summary>
    /// Opening any drawer of a filing cabinet shows every drawer of that cabinet in the one loot
    /// panel, and searches them one after another.
    ///
    /// Nothing about the loot changes. Each drawer stays its own container with its own items and
    /// its own search state; the panel just shows more than one of them. See
    /// <see cref="CabinetSession"/> for the panel and the search chain, and <see cref="Cabinets"/>
    /// for how a cabinet and its neighbours are found.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class CabinetLooterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.cabinetlooter";
        public const string PluginName = "Cabinet Looter";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.5.2";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AutoSearch;
        internal static ConfigEntry<bool> ResumePartial;
        internal static ConfigEntry<bool> IncludeNeighbours;
        internal static ConfigEntry<float> NeighbourGap;
        internal static ConfigEntry<int> MaxCabinets;
        internal static ConfigEntry<bool> DebugLogging;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "1. General",
                "Show the whole cabinet",
                true,
                "Opening any drawer of a filing cabinet shows every drawer of that cabinet in the "
                + "loot panel. Off restores vanilla for the next drawer you open.");

            AutoSearch = Config.Bind(
                "1. General",
                "Search every drawer automatically",
                true,
                "After the drawer you opened, search the cabinet's other drawers one after another, "
                + "at normal search speed. Off: the other drawers are shown, and you click a drawer's "
                + "heading to search it.");

            ResumePartial = Config.Bind(
                "1. General",
                "Resume half-searched drawers",
                true,
                "A drawer whose search was interrupted (you closed the window, or stopped it) is "
                + "searched again automatically when you reopen the cabinet. Vanilla makes you "
                + "press Search yourself. Items already found stay found either way.");

            IncludeNeighbours = Config.Bind(
                "2. Cabinet clusters",
                "Include cabinets standing next to it",
                false,
                "Also show filing cabinets standing directly beside or on top of the one you opened, "
                + "as one cluster. Cabinets on the other side of a wall are never included.");

            NeighbourGap = Config.Bind(
                "2. Cabinet clusters",
                "Largest gap between cabinets (metres)",
                0.3f,
                new ConfigDescription(
                    "How far apart two cabinets may stand and still count as one cluster. The gap is "
                    + "measured between the cabinets' sides, not their centres.",
                    new AcceptableValueRange<float>(0.05f, 1.5f)));

            MaxCabinets = Config.Bind(
                "2. Cabinet clusters",
                "Most cabinets in a cluster",
                4,
                new ConfigDescription(
                    "The cluster stops growing at this many cabinets, the one you opened included. "
                    + "The nearest ones are kept.",
                    new AcceptableValueRange<int>(2, 10)));

            DebugLogging = Config.Bind(
                "3. Debug",
                "Log cabinet details",
                false,
                "Write each cabinet and cluster found, and each search started, to LogOutput.log.");

            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                Patches.Apply(harmony);
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Logger.LogError(PluginName + " " + PluginVersion
                                + " could not apply its patches, so the game is unmodified: " + e);
                return;
            }

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Update()
        {
            CabinetSession.TickCurrent();
        }

        internal static void Debug(string message)
        {
            if (DebugLogging != null && DebugLogging.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
