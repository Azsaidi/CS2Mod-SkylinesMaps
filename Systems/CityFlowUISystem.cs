using Colossal.UI.Binding;
using Game.UI;

namespace SkylinesMaps.Systems
{
    /// City wide congestion is expressed as a percentage of the total road network that is congested.
    public partial class CityFlowUISystem : UISystemBase
    {
        /// Binding group shared with the UI module.
        public const string kGroup = "skylinesMaps";

        protected override void OnCreate()
        {
            base.OnCreate();

            // Update bindings push a fresh value each UI frame, so the readout tracks the roads.
            AddUpdateBinding(new GetterValueBinding<float>(
                kGroup, "cityFlow", () => LiveCongestionSystem.CityFlowPercent));

            AddUpdateBinding(new GetterValueBinding<bool>(
                kGroup, "showCityTraffic", () =>
                    Mod.Settings != null && Mod.Settings.ShowCityTraffic));
        }
    }
}
