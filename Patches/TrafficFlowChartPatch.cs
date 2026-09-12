using Colossal.UI.Binding;
using Game.UI.InGame;
using HarmonyLib;
using SkylinesMaps.Systems;

namespace SkylinesMaps.Patches
{

    [HarmonyPatch(typeof(TrafficInfoviewUISystem), "UpdateTrafficFlowBinding")]
    internal static class TrafficFlowChartPatch
    {
        private static bool Prefix(IJsonWriter writer)
        {
            ModSettings settings = Mod.Settings;
            if (settings != null && !settings.DetailedFlowChart)
            {
                return true;
            }

            float[] history = LiveCongestionSystem.FlowHistory;
            if (history == null)
            {
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
