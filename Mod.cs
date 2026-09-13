using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using System;
using System.Linq;
using SkylinesMaps.Systems;

namespace SkylinesMaps
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(SkylinesMaps)}").SetShowsErrorsInUI(false);

        public const string kToggleInfoviewActionName = "ToggleTrafficInfoview";

        public const string kHarmonyId = "Azsaidi." + nameof(SkylinesMaps);

        public static ModSettings Settings { get; private set; }
        public static ProxyAction ToggleInfoviewAction { get; private set; }

        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new ModSettings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            Settings.RegisterKeyBindings();

            ToggleInfoviewAction = Settings.GetAction(kToggleInfoviewActionName);
            ToggleInfoviewAction.shouldBeEnabled = true;

            AssetDatabase.global.LoadSettings(nameof(SkylinesMaps), Settings, new ModSettings(this));

            PatchTrafficFlowChart();

            updateSystem.UpdateAt<CongestionInfomodeSystem>(SystemUpdatePhase.UIUpdate);

            // Must run after NetColorSystem, which rewrites every EdgeColor each rendering frame.
            updateSystem.UpdateAfter<LiveCongestionSystem, Game.Rendering.NetColorSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAt<CityFlowUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<TrafficInfoviewToggleSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<JourneyPlannerSystem>(SystemUpdatePhase.Modification2);
            updateSystem.UpdateAt<JourneyPlannerUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateBefore<JourneyMarkerSystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<JourneyTooltipSystem>(SystemUpdatePhase.UITooltip);
        }

        private void PatchTrafficFlowChart()
        {
            try
            {
                m_Harmony = new Harmony(kHarmonyId);
                m_Harmony.PatchAll(typeof(Mod).Assembly);
                log.Info($"Patched traffic flow chart ({m_Harmony.GetPatchedMethods().Count()} method(s))");
            }
            catch (Exception e)
            {
                log.Warn($"Could not patch the traffic flow chart, it will keep the vanilla data: {e.Message}");
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            if (m_Harmony != null)
            {
                m_Harmony.UnpatchAll(kHarmonyId);
                m_Harmony = null;
            }

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
