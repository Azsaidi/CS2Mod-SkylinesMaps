using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using UnityEngine;

namespace SkylinesMaps
{
    [FileLocation("ModsSettings/" + nameof(SkylinesMaps) + "/" + nameof(SkylinesMaps))]
    [SettingsUIGroupOrder(kAppearanceGroup, kThresholdGroup, kKeybindingGroup, kAboutGroup)]
    [SettingsUIShowGroupName(kAppearanceGroup, kThresholdGroup, kKeybindingGroup, kAboutGroup)]
    [SettingsUIKeyboardAction(Mod.kToggleInfoviewActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage })]
    public class ModSettings : ModSetting
    {
        public const string kSection = "Main";

        public const string kAppearanceGroup = "Appearance";
        public const string kThresholdGroup = "Thresholds";
        public const string kKeybindingGroup = "KeyBinding";
        public const string kAboutGroup = "About";

        public enum ColorPreset
        {
            GoogleMaps,
            CS1Classic,
            HighContrast,
        }

        public ModSettings(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kAppearanceGroup)]
        public ColorPreset Palette { get; set; } = ColorPreset.GoogleMaps;

        [SettingsUISection(kSection, kAppearanceGroup)]
        public bool ShowCityTraffic { get; set; } = true;

        [SettingsUISection(kSection, kAppearanceGroup)]
        public bool DetailedFlowChart { get; set; } = true;

        [SettingsUISlider(min = 1, max = 20, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(kSection, kAppearanceGroup)]
        public int MinimumTraffic { get; set; } = 4;

        [SettingsUISlider(min = 5, max = 100, step = 5, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kAppearanceGroup)]
        public int Responsiveness { get; set; } = 50;

        [SettingsUISlider(min = 0, max = 12, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(kSection, kAppearanceGroup)]
        public int GradientSteps { get; set; } = 3;

        [SettingsUISlider(min = 0, max = 90, step = 5, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kThresholdGroup)]
        public int JammedBelow { get; set; } = 20;

        [SettingsUISlider(min = 10, max = 100, step = 5, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kThresholdGroup)]
        public int FreeFlowingAbove { get; set; } = 75;

        [SettingsUISection(kSection, kAboutGroup)]
        public string Version => ModAssemblyInfo.Version;

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kAboutGroup)]
        public bool ResetSettings
        {
            set
            {
                Mod.log.Info("Reset settings to defaults");
                SetDefaults();
                ApplyAndSave();
            }
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.T, Mod.kToggleInfoviewActionName, ctrl: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ToggleInfoviewBinding { get; set; }

        [SettingsUISection(kSection, kKeybindingGroup)]
        public bool ResetBindings
        {
            set
            {
                Mod.log.Info("Reset key bindings");
                ResetKeyBindings();
            }
        }

        public void GetGradient(out Color low, out Color medium, out Color high)
        {
            switch (Palette)
            {

                case ColorPreset.CS1Classic:
                    low = new Color(0.486f, 0.165f, 0.125f);
                    medium = new Color(0.663f, 0.635f, 0.267f);
                    high = new Color(0.510f, 0.670f, 0.330f);
                    break;

                case ColorPreset.HighContrast:
                    low = new Color(0.60f, 0.00f, 0.00f);
                    medium = new Color(1.00f, 0.55f, 0.00f);
                    high = new Color(0.00f, 0.95f, 0.35f);
                    break;

                default: // GoogleMaps
                    low = new Color(0.70f, 0.08f, 0.07f);
                    medium = new Color(0.98f, 0.74f, 0.02f);
                    high = new Color(0.20f, 0.66f, 0.33f);
                    break;
            }
        }

        public void GetRange(out float min, out float max)
        {
            min = Mathf.Clamp01(JammedBelow / 100f);
            max = Mathf.Clamp01(FreeFlowingAbove / 100f);
            if (max <= min)
            {
                max = Mathf.Min(1f, min + 0.05f);
            }
        }

        public override void SetDefaults()
        {
            Palette = ColorPreset.GoogleMaps;
            ShowCityTraffic = true;
            DetailedFlowChart = true;
            MinimumTraffic = 4;
            Responsiveness = 50;
            GradientSteps = 3;
            JammedBelow = 20;
            FreeFlowingAbove = 75;
        }
    }
}
