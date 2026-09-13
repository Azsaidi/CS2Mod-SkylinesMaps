using Game.UI.Localization;
using Game.UI.Tooltip;

namespace SkylinesMaps.Systems
{
    public partial class JourneyTooltipSystem : TooltipSystemBase
    {
        public const string kSelectDestinationLocaleID = "SkylinesMaps.JourneyPlanner.SELECT_DESTINATION";

        public const string kSearchingLocaleID = "SkylinesMaps.JourneyPlanner.SEARCHING";

        public const string kFailedLocaleID = "SkylinesMaps.JourneyPlanner.FAILED";

        private JourneyPlannerSystem m_PlannerSystem;
        private StringTooltip m_SelectDestinationTooltip;
        private StringTooltip m_SearchingTooltip;
        private StringTooltip m_FailedTooltip;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlannerSystem = World.GetOrCreateSystemManaged<JourneyPlannerSystem>();

            m_SelectDestinationTooltip = new StringTooltip
            {
                path = "skylinesMapsJourneySelectDestination",
                color = TooltipColor.Info,
                value = LocalizedString.Id(kSelectDestinationLocaleID),
            };

            m_SearchingTooltip = new StringTooltip
            {
                path = "skylinesMapsJourneySearching",
                color = TooltipColor.Info,
                value = LocalizedString.Id(kSearchingLocaleID),
            };

            m_FailedTooltip = new StringTooltip
            {
                path = "skylinesMapsJourneyFailed",
                color = TooltipColor.Error,
                value = LocalizedString.Id(kFailedLocaleID),
            };
        }

        protected override void OnUpdate()
        {
            switch (m_PlannerSystem.state)
            {
                case JourneyPlannerSystem.JourneyState.PickingDestination:
                    AddMouseTooltip(m_SelectDestinationTooltip);
                    break;

                case JourneyPlannerSystem.JourneyState.Pathfinding:
                    AddMouseTooltip(m_SearchingTooltip);
                    break;
            }

            if (m_PlannerSystem.failed)
            {
                AddMouseTooltip(m_FailedTooltip);
            }
        }
    }
}
