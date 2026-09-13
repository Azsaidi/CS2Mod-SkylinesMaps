    import { ModRegistrar, ModuleRegistry } from "cs2/modding";
import { cloneElement } from "react";
import { CityTrafficIndicator } from "mods/city-traffic-indicator";
import { DayFlowChart } from "mods/day-flow-chart";
import { JourneyButton } from "mods/journey-button";
import { JourneyCard } from "mods/journey-card";

const kSectionComponentsPath =
    "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
const kSectionComponentsExport = "selectedInfoSectionComponents";
const kActionsSectionKey = "Game.UI.InGame.ActionsSection";

const kJourneyButtonKey = "skylinesMapsJourney";
const kJourneyWrapperFlag = "__skylinesMapsJourney";
const kTrafficRoutesIcon = "TrafficRoutes.svg";
const kDeleteIcon = "Trash.svg";
const kMaxSearchDepth = 8;

const containsIcon = (node: any, icon: string, depth: number = 0): boolean => {
    if (!node || typeof node !== "object" || depth > kMaxSearchDepth) {
        return false;
    }

    if (Array.isArray(node)) {
        return node.some((child) => containsIcon(child, icon, depth + 1));
    }

    const props = node.props;
    if (!props) {
        return false;
    }

    if (typeof props.src === "string" && props.src.includes(icon)) {
        return true;
    }

    return containsIcon(props.children, icon, depth + 1);
};

const insertJourneyButton = (rendered: any): any => {
    const focusBoundary = rendered?.props?.children;
    const buttonGroup = focusBoundary?.props?.children;
    const buttons = buttonGroup?.props?.children;

    if (!Array.isArray(buttons)) {
        return null;
    }

    if (buttons.some((button: any) => button?.key === kJourneyButtonKey)) {
        return rendered;
    }

    const routesIndex = buttons.findIndex((button: any) => containsIcon(button, kTrafficRoutesIcon));
    const deleteIndex = buttons.findIndex((button: any) => containsIcon(button, kDeleteIcon));
    const index = routesIndex >= 0 ? routesIndex + 1 : deleteIndex >= 0 ? deleteIndex : buttons.length;

    const withJourney = [
        ...buttons.slice(0, index),
        <JourneyButton key={kJourneyButtonKey} />,
        ...buttons.slice(index),
    ];

    return cloneElement(
        rendered,
        undefined,
        cloneElement(focusBoundary, undefined, cloneElement(buttonGroup, undefined, ...withJourney))
    );
};

const registerJourneyPlanner = (moduleRegistry: ModuleRegistry) => {
    moduleRegistry.append("Game", JourneyCard);
    wrapActionsSection(moduleRegistry);
};

const wrapActionsSection = (moduleRegistry: ModuleRegistry) => {
    const sectionComponents = moduleRegistry.get(kSectionComponentsPath, kSectionComponentsExport);
    const OriginalActionsSection = sectionComponents?.[kActionsSectionKey];

    if (typeof OriginalActionsSection !== "function") {
        console.warn(`[SkylinesMaps] ${kActionsSectionKey} not found, the journey planner is unavailable`);
        return;
    }

    if (OriginalActionsSection[kJourneyWrapperFlag]) {
        return;
    }

    const JourneyActionsSection = (props: any) => {
        const rendered = OriginalActionsSection(props);
        return insertJourneyButton(rendered) ?? (
            <>
                {rendered}
                <JourneyButton />
            </>
        );
    };

    (JourneyActionsSection as any)[kJourneyWrapperFlag] = true;
    sectionComponents[kActionsSectionKey] = JourneyActionsSection;
};

const kChartPath =
    "game-ui/game/components/selected-info-panel/shared-components/traffic-charts/traffic-chart.tsx";
const kChartExport = "TrafficFlowChart";

const kVanillaPoints = 5;

// The map legend is drawn after this panel by its container, so rendering directly after it places the readout between the chart and the legend.
const kTrafficPanelPath =
    "game-ui/game/components/infoviews/active-infoview-panel/panels/traffic-infoview-panel.tsx";
const kTrafficPanelExport = "TrafficInfoviewPanel";

const register: ModRegistrar = (moduleRegistry) => {
    registerJourneyPlanner(moduleRegistry);

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
