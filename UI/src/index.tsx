import { ModRegistrar } from "cs2/modding";
import { CityTrafficIndicator } from "mods/city-traffic-indicator";

// The map legend is drawn after this panel by its container, so rendering directly after it places the readout between the chart and the legend.
const kTrafficPanelPath =
    "game-ui/game/components/infoviews/active-infoview-panel/panels/traffic-infoview-panel.tsx";
const kTrafficPanelExport = "TrafficInfoviewPanel";

const register: ModRegistrar = (moduleRegistry) => {
    // append() needs the target to expose an append hook, which this panel does not.
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
