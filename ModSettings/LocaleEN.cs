using Colossal;
using System.Collections.Generic;

namespace SkylinesMaps
{
    public class LocaleEN : IDictionarySource
    {
        private readonly ModSettings m_Setting;

        public LocaleEN(ModSettings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), ModAssemblyInfo.Title },
                { m_Setting.GetOptionTabLocaleID(ModSettings.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(ModSettings.kAppearanceGroup), "Appearance" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.kThresholdGroup), "Thresholds" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.kJunctionGroup), "Junctions" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.kJourneyGroup), "Journey planner" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.kKeybindingGroup), "Key bindings" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.kAboutGroup), "About" },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.Version)), "Version" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.Version)), "Installed version of this mod." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.ResetSettings)), "Reset settings" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.ResetSettings)), "Return every setting to its default value." },
                { m_Setting.GetOptionWarningLocaleID(nameof(ModSettings.ResetSettings)), "Reset all settings to their defaults?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.Palette)), "Color palette" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.Palette)), "Colors used to shade roads from jammed to free flowing." },

                { m_Setting.GetEnumValueLocaleID(ModSettings.ColorPreset.GoogleMaps), "Google Maps" },
                { m_Setting.GetEnumValueLocaleID(ModSettings.ColorPreset.CS1Classic), "Cities: Skylines 1" },
                { m_Setting.GetEnumValueLocaleID(ModSettings.ColorPreset.HighContrast), "High contrast" },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.ShowCityTraffic)), "Show city traffic readout" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.ShowCityTraffic)), "Shows the city wide average above the map legend in the traffic panel." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.DetailedFlowChart)), "Detailed traffic flow chart" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.DetailedFlowChart)), "Draws the traffic flow graph from this mod's own readings, one every 15 in-game minutes, instead of the base game's four six-hour averages. Turn it off to get the base game graph back. Readings keep being recorded and saved either way, so the detailed chart still shows a full day if you turn it back on." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.MinimumTraffic)), "Minimum traffic" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.MinimumTraffic)), "How many vehicles per 100 m of road are needed before congestion is shown at full strength. Raise it if a couple of slow cars turn a whole road red." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.Responsiveness)), "Responsiveness" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.Responsiveness)), "How quickly colors react to changing traffic. Higher updates faster but flickers more." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.GradientSteps)), "Gradient steps" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.GradientSteps)), "Number of discrete color bands. Set to 0 to match the base game traffic view." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.JammedBelow)), "Jammed below" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.JammedBelow)), "Roads moving at or below this share of their free-flow speed are drawn fully jammed. Raise it to make congestion show up sooner." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.FreeFlowingAbove)), "Free flowing above" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.FreeFlowingAbove)), "Roads moving at or above this share of their free-flow speed are drawn fully free flowing. Lower it to be more forgiving of busy but moving traffic." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.JunctionJamFactor)), "Junction jam factor" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.JunctionJamFactor)), "How long vehicles can wait at a junction before it counts as jammed. At 100% a wait of about 8 in-game seconds still counts as normal, and the junction is fully jammed by twice that. Raise it if junctions turn red at ordinary red lights; a full traffic light cycle takes roughly 12 to 32 seconds. Set it to 0% to judge junctions by speed alone." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.RoundaboutJamFactor)), "Roundabout jam factor" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.RoundaboutJamFactor)), "The same for roundabouts, where vehicles only wait for a gap in the traffic. At 100% a wait of about 3 in-game seconds still counts as normal. Set it to 0% to judge roundabouts by speed alone." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.ToggleInfoviewBinding)), "Toggle traffic infoview" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.ToggleInfoviewBinding)), "Opens and closes the traffic infoview." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.ResetBindings)), "Reset key bindings" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.ResetBindings)), "Reset all key bindings of the mod." },

                { m_Setting.GetBindingKeyLocaleID(Mod.kToggleInfoviewActionName), "Toggle key" },
                { m_Setting.GetBindingMapLocaleID(), ModAssemblyInfo.Title },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.EnableJourneyPlanner)), "Enable journey planner" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.EnableJourneyPlanner)), "Adds a button to building and road info panels for planning a journey between two places by car, on foot, by bike or by public transport, with live traffic and alternative driving routes. Turn it off to hide the button and clear any journey being shown." },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.DisableJourneyCameraZoom)), "Disable camera zoom" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.DisableJourneyCameraZoom)), "Stops the camera moving and zooming to fit a journey when routes are found or when you pick a different route or way of travelling." },

                { Systems.JourneyTooltipSystem.kSelectDestinationLocaleID, "Select the destination building or road for this journey" },
                { Systems.JourneyTooltipSystem.kSearchingLocaleID, "Finding the best route..." },
                { Systems.JourneyTooltipSystem.kFailedLocaleID, "No route found between these locations" },

                { "SkylinesMaps.JourneyPlanner.START_TITLE", "Plan a journey" },
                { "SkylinesMaps.JourneyPlanner.START_DESCRIPTION", "Start a journey from here, then select the building or road you want to go to." },
                { "SkylinesMaps.JourneyPlanner.CANCEL_TITLE", "Cancel journey" },
                { "SkylinesMaps.JourneyPlanner.CANCEL_DESCRIPTION", "Stop choosing a destination for this journey." },
                { "SkylinesMaps.JourneyPlanner.END_TITLE", "End journey here" },
                { "SkylinesMaps.JourneyPlanner.END_DESCRIPTION", "Find the best routes to here by car, on foot, by bike and by public transport, based on travel time and live traffic." },
                { "SkylinesMaps.JourneyPlanner.CLEAR_TITLE", "Clear route" },
                { "SkylinesMaps.JourneyPlanner.CLEAR_DESCRIPTION", "Remove this journey's route from the map." },
                { "SkylinesMaps.JourneyPlanner.CARD_TITLE", "Journey" },
                { "SkylinesMaps.JourneyPlanner.SEARCHING_ROUTES", "Finding routes..." },
                { "SkylinesMaps.JourneyPlanner.BEST", "Best route" },
                { "SkylinesMaps.JourneyPlanner.SIMILAR", "Similar time" },
                { "SkylinesMaps.JourneyPlanner.VIA", "via" },
                { "SkylinesMaps.JourneyPlanner.REAL_TIME", "real time" },
                { "SkylinesMaps.JourneyPlanner.KIND_FASTEST", "Fastest" },
                { "SkylinesMaps.JourneyPlanner.KIND_LESS_TRAFFIC", "Less traffic" },
                { "SkylinesMaps.JourneyPlanner.KIND_CHEAPEST", "Cheapest" },
                { "SkylinesMaps.JourneyPlanner.KIND_FEWER_CHANGES", "Fewer changes" },
                { "SkylinesMaps.JourneyPlanner.KIND_LESS_WALKING", "Less walking" },
                { "SkylinesMaps.JourneyPlanner.CHANGE", "Change" },
                { "SkylinesMaps.JourneyPlanner.AT", "at" },
                { "SkylinesMaps.JourneyPlanner.NO_ROUTE", "No route" },
                { "SkylinesMaps.JourneyPlanner.REASON_CAR", "No driving route was found between these places." },
                { "SkylinesMaps.JourneyPlanner.REASON_WALK", "No walking route was found between these places." },
                { "SkylinesMaps.JourneyPlanner.REASON_BIKE_START", "No cycling route: there is no road or bike lane that allows bikes near the start." },
                { "SkylinesMaps.JourneyPlanner.REASON_BIKE_END", "No cycling route: there is no road or bike lane that allows bikes near the destination." },
                { "SkylinesMaps.JourneyPlanner.REASON_BIKE_NETWORK", "No cycling route: bikes can't get between these places, because the roads in between are highways or don't allow bikes." },
                { "SkylinesMaps.JourneyPlanner.REASON_TRANSIT", "No public transport route: no line running at this time connects these places within walking distance." },
                { "SkylinesMaps.JourneyPlanner.MODE_CAR", "Driving" },
                { "SkylinesMaps.JourneyPlanner.MODE_WALK", "Walking" },
                { "SkylinesMaps.JourneyPlanner.MODE_BICYCLE", "Cycling" },
                { "SkylinesMaps.JourneyPlanner.MODE_TRANSIT", "Public transport" },
                { "SkylinesMaps.JourneyPlanner.WALK", "Walk" },
                { "SkylinesMaps.JourneyPlanner.WAIT", "Wait" },
                { "SkylinesMaps.JourneyPlanner.STOP", "stop" },
                { "SkylinesMaps.JourneyPlanner.STOPS", "stops" },
                { "SkylinesMaps.JourneyPlanner.TO", "to" },
                { "SkylinesMaps.JourneyPlanner.LINE", "Line" },
                { "SkylinesMaps.JourneyPlanner.FREE", "Free" },
                { "SkylinesMaps.JourneyPlanner.KIND_FEWER_TURNS", "Fewer turns" },
                { "SkylinesMaps.JourneyPlanner.KIND_SHORTEST", "Shortest" },

                // Infomode shown in the vanilla Traffic infoview.
                { Systems.CongestionInfomodeSystem.kInfomodeLocaleID, "Live Congestion" },
                { Systems.CongestionInfomodeSystem.kInfomodeTooltipLocaleID, "Colors roads by how fast traffic is actually moving compared to the speed limit, measured from the vehicles on the road right now." },
                { Systems.CongestionInfomodeSystem.kLowLabelLocaleID, "Jammed" },
                { Systems.CongestionInfomodeSystem.kMediumLabelLocaleID, "Slow" },
                { Systems.CongestionInfomodeSystem.kHighLabelLocaleID, "Free flowing" },
            };
        }

        public void Unload()
        {
        }
    }
}
