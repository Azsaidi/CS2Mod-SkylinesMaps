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
