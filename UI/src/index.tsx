    import { ModRegistrar } from "cs2/modding";
import { CityTrafficIndicator } from "mods/city-traffic-indicator";
import { DayFlowChart } from "mods/day-flow-chart";

const kChartPath =
    "game-ui/game/components/selected-info-panel/shared-components/traffic-charts/traffic-chart.tsx";
const kChartExport = "TrafficFlowChart";

const kVanillaPoints = 5;

// The map legend is drawn after this panel by its container, so rendering directly after it places the readout between the chart and the legend.
const kTrafficPanelPath =
    "game-ui/game/components/infoviews/active-infoview-panel/panels/traffic-infoview-panel.tsx";
const kTrafficPanelExport = "TrafficInfoviewPanel";

const register: ModRegistrar = (moduleRegistry) => {
    if (moduleRegistry.get(kChartPath, kChartExport)) {
        moduleRegistry.extend(
            kChartPath,
            kChartExport,
            (Original: any) => (props: any) => {
                const data = props?.data;

                return Array.isArray(data) && data.length > kVanillaPoints
                    ? <DayFlowChart data={data} className={props.className} />
                    : <Original {...props} />;
            }
        );
    } else {
        console.warn(`[SkylinesMaps] ${kChartPath} not found, the chart keeps its vanilla axis`);
    }

    const target = moduleRegistry.get(kTrafficPanelPath, kTrafficPanelExport);

    if (target) {
        moduleRegistry.extend(
            kTrafficPanelPath,
            kTrafficPanelExport,
            (Original: any) => (props: any) => (
                <>
                    <Original {...props} />
                    <CityTrafficIndicator />
                </>
            )
        );

        return;
    }

    console.warn(`[SkylinesMaps] ${kTrafficPanelPath} not found, falling back to GameTopLeft`);
    moduleRegistry.append("GameTopLeft", CityTrafficIndicator);
};

export default register;
