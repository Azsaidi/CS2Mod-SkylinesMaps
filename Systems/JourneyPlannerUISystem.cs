using Colossal.UI.Binding;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace SkylinesMaps.Systems
{
    public partial class JourneyPlannerUISystem : UISystemBase
    {
        private JourneyPlannerSystem m_PlannerSystem;
        private ToolSystem m_ToolSystem;
        private SelectedInfoUISystem m_InfoUISystem;
        private RawValueBinding m_PlanBinding;
        private int m_PlanVersion = -1;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlannerSystem = World.GetOrCreateSystemManaged<JourneyPlannerSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_InfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();

            AddUpdateBinding(new GetterValueBinding<int>(
                CityFlowUISystem.kGroup, "journeyButton", () => (int)m_PlannerSystem.GetButtonMode()));

            AddUpdateBinding(new GetterValueBinding<bool>(
                CityFlowUISystem.kGroup, "journeyStepAddresses",
                () => Mod.Settings == null || Mod.Settings.ShowStepAddresses));

            AddUpdateBinding(new GetterValueBinding<int>(
                CityFlowUISystem.kGroup, "journeyTimes",
                () => (int)(Mod.Settings != null ? Mod.Settings.JourneyTimes : ModSettings.TimeDisplay.RealAndGame)));

            AddUpdateBinding(new GetterValueBinding<int>(
                CityFlowUISystem.kGroup, "journeyArrivalClock",
                () => (int)(Mod.Settings != null ? Mod.Settings.ArrivalClock : ModSettings.ClockFormat.MatchGame)));

            AddBinding(m_PlanBinding = new RawValueBinding(CityFlowUISystem.kGroup, "journeyPlan", m_PlannerSystem.WritePlan));

            AddBinding(new TriggerBinding(CityFlowUISystem.kGroup, "journeyAction", m_PlannerSystem.QueueAction));
            AddBinding(new TriggerBinding<int>(CityFlowUISystem.kGroup, "selectJourneyRoute", m_PlannerSystem.QueueRouteSelection));
            AddBinding(new TriggerBinding<int>(CityFlowUISystem.kGroup, "selectJourneyMode", m_PlannerSystem.QueueModeSelection));
            AddBinding(new TriggerBinding(CityFlowUISystem.kGroup, "clearJourney", m_PlannerSystem.QueueClear));
            AddBinding(new TriggerBinding(CityFlowUISystem.kGroup, "swapJourney", m_PlannerSystem.QueueSwap));
            AddBinding(new TriggerBinding<bool>(CityFlowUISystem.kGroup, "focusJourneyPlace", FocusPlace));
        }

        private void FocusPlace(bool end)
        {
            if (!m_PlannerSystem.TryGetPlace(end, out Entity place, out int index))
            {
                return;
            }

            if (m_ToolSystem.selected != place)
            {
                m_ToolSystem.selected = place;
                m_ToolSystem.selectedIndex = index;
            }

            bool focusing = SelectedInfoUISystem.s_CameraController != null
                && SelectedInfoUISystem.s_CameraController.controllerEnabled
                && SelectedInfoUISystem.s_CameraController.followedEntity == place;

            m_InfoUISystem.Focus(focusing ? Entity.Null : place);
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (m_PlannerSystem.planVersion != m_PlanVersion)
            {
                m_PlanVersion = m_PlannerSystem.planVersion;
                m_PlanBinding.Update();
            }
        }
    }
}
