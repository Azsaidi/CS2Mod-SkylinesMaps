using Colossal.UI.Binding;
using Game.UI;

namespace SkylinesMaps.Systems
{
    public partial class JourneyPlannerUISystem : UISystemBase
    {
        private JourneyPlannerSystem m_PlannerSystem;
        private RawValueBinding m_PlanBinding;
        private int m_PlanVersion = -1;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlannerSystem = World.GetOrCreateSystemManaged<JourneyPlannerSystem>();

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
