using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using SkylinesMaps.Systems;

namespace SkylinesMaps
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(SkylinesMaps)}").SetShowsErrorsInUI(false);

        public const string kToggleInfoviewActionName = "ToggleTrafficInfoview";

        public static ModSettings Settings { get; private set; }
        public static ProxyAction ToggleInfoviewAction { get; private set; }

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

            updateSystem.UpdateAt<CongestionInfomodeSystem>(SystemUpdatePhase.UIUpdate);

            // Must run after NetColorSystem, which rewrites every EdgeColor each rendering frame.
            updateSystem.UpdateAfter<LiveCongestionSystem, Game.Rendering.NetColorSystem>(SystemUpdatePhase.Rendering);

            updateSystem.UpdateAt<CityFlowUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<TrafficInfoviewToggleSystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
