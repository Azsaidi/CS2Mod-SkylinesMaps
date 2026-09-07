using Colossal.UI.Binding;
using Game.UI.InGame;
using HarmonyLib;
using SkylinesMaps.Systems;

namespace SkylinesMaps.Patches
{
    /// The base game chart is four six hour buckets, each an unweighted mean over every road on
    /// the map, with a fifth point that is a copy of the first. It is flat whatever the city does.
    /// This feeds the same chart the mod's own quarter hour history instead.
    [HarmonyPatch(typeof(TrafficInfoviewUISystem), "UpdateTrafficFlowBinding")]
    internal static class TrafficFlowChartPatch
    {
        /// Values are written on the same 0 to 100 scale the base game uses.
        private static bool Prefix(IJsonWriter writer)
        {
            float[] history = LiveCongestionSystem.FlowHistory;
            if (history == null)
            {
                // Nothing measured yet, so let the base game draw its own line.
                return true;
            }

            writer.ArrayBegin(history.Length);
            for (int i = 0; i < history.Length; i++)
            {
                writer.Write(history[i]);
            }

            writer.ArrayEnd();
            return false;
        }
    }
}
