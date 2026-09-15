import { bindTrigger, bindTriggerWithArgs, bindValue, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { getModule } from "cs2/modding";
import { Panel } from "cs2/ui";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import endIcon from "images/journey-end.svg";
import carIcon from "images/journey-mode-car.svg";
import walkIcon from "images/journey-mode-walk.svg";
import bicycleIcon from "images/journey-mode-bicycle.svg";
import transitIcon from "images/journey-mode-transit.svg";
import tramIcon from "images/journey-mode-tram.svg";
import subwayIcon from "images/journey-mode-subway.svg";
import trainIcon from "images/journey-mode-train.svg";
import ferryIcon from "images/journey-mode-ferry.svg";
import changeIcon from "images/journey-change.svg";

const kCardOwnerKey = "__skylinesMapsJourneyCardOwner";

const useSingleCard = (): boolean => {
    const [id] = useState(() => `${Date.now()}-${Math.random()}`);
    const host = window as any;

    if (!host[kCardOwnerKey]) {
        host[kCardOwnerKey] = id;
    }

    useEffect(() => () => {
        if (host[kCardOwnerKey] === id) {
            host[kCardOwnerKey] = undefined;
        }
    }, [id]);

    return host[kCardOwnerKey] === id;
};

const kGroup = "skylinesMaps";

interface JourneyLeg {
    type: number;
    gameDuration: number;
    wait: number;
    distance: number;
    line: string;
    color: string;
    transport: number;
    from: string;
    to: string;
    stops: number;
    price: number;
}

interface JourneyMode {
    available: boolean;
    gameDuration: number;
}

interface JourneyRoute {
    legs: JourneyLeg[];
    cost: number;
    tags: number;
    via: string;
    duration: number;
    gameDuration: number;
    distance: number;
    traffic: number;
    delta: number;
}

interface JourneyPlan {
    searching: boolean;
    from: string;
    to: string;
    mode: number;
    modes: JourneyMode[];
    reason: string;
    selected: number;
    routes: JourneyRoute[];
}

const plan$ = bindValue<JourneyPlan | null>(kGroup, "journeyPlan", null);
const selectRoute = bindTriggerWithArgs<[number]>(kGroup, "selectJourneyRoute");
const clearJourney = bindTrigger(kGroup, "clearJourney");
const selectMode = bindTriggerWithArgs<[number]>(kGroup, "selectJourneyMode");

const kModes = [
    { icon: carIcon, id: "SkylinesMaps.JourneyPlanner.MODE_CAR", fallback: "Driving" },
    { icon: walkIcon, id: "SkylinesMaps.JourneyPlanner.MODE_WALK", fallback: "Walking" },
    { icon: bicycleIcon, id: "SkylinesMaps.JourneyPlanner.MODE_BICYCLE", fallback: "Cycling" },
    { icon: transitIcon, id: "SkylinesMaps.JourneyPlanner.MODE_TRANSIT", fallback: "Public transport" },
];

const kReasonFallbacks = [
    "No driving route was found between these places.",
    "No walking route was found between these places.",
    "No cycling route was found between these places.",
    "No public transport route was found between these places.",
];

const kTransportIcons: Record<number, string> = {
    0: transitIcon,
    1: trainIcon,
    3: tramIcon,
    4: ferryIcon,
    8: subwayIcon,
    11: ferryIcon,
};

const kTransportNames: Record<number, string> = {
    0: "Bus",
    1: "Train",
    3: "Tram",
    4: "Ship",
    8: "Subway",
    11: "Ferry",
};

interface TabUi {
    Tooltip: any;
    TintedIcon: any;
}

let cachedTabUi: TabUi | null = null;

const getTabUi = (): TabUi => {
    if (cachedTabUi) {
        return cachedTabUi;
    }

    cachedTabUi = {
        Tooltip: getModule("game-ui/common/tooltip/tooltip.tsx", "Tooltip"),
        TintedIcon: getModule("game-ui/common/image/tinted-icon.tsx", "TintedIcon"),
    };

    return cachedTabUi;
};

const getLabelColour = (hex: string): string => {
    const value = parseInt(hex.slice(1), 16);
    if (isNaN(value)) {
        return "#FFFFFF";
    }

    const luminance = (0.299 * ((value >> 16) & 255) + 0.587 * ((value >> 8) & 255) + 0.114 * (value & 255)) / 255;
    return luminance > 0.6 ? "#1B1B1B" : "#FFFFFF";
};

const kTrafficColours = ["#5BB974", "#F29900", "#EE675C"];
const kStartColour = "#4285F4";
const kSelectedColour = "#4285F4";

const kTags = [
    { flag: 1, id: "SkylinesMaps.JourneyPlanner.KIND_FASTEST", fallback: "Fastest" },
    { flag: 2, id: "SkylinesMaps.JourneyPlanner.KIND_SHORTEST", fallback: "Shortest" },
    { flag: 4, id: "SkylinesMaps.JourneyPlanner.KIND_FEWER_TURNS", fallback: "Fewer turns" },
    { flag: 8, id: "SkylinesMaps.JourneyPlanner.KIND_LESS_TRAFFIC", fallback: "Less traffic" },
    { flag: 16, id: "SkylinesMaps.JourneyPlanner.KIND_CHEAPEST", fallback: "Cheapest" },
    { flag: 32, id: "SkylinesMaps.JourneyPlanner.KIND_FEWER_CHANGES", fallback: "Fewer changes" },
    { flag: 64, id: "SkylinesMaps.JourneyPlanner.KIND_LESS_WALKING", fallback: "Less walking" },
];

type Translate = (id: string, fallback: string) => string;

const formatDuration = (seconds: number, allowSeconds: boolean): string => {
    if (allowSeconds && seconds < 60) {
        return `${Math.max(1, Math.round(seconds))} s`;
    }

    const minutes = Math.max(1, Math.round(seconds / 60));
    if (minutes < 60) {
        return `${minutes} min`;
    }

    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
};

const formatCost = (cost: number, text: Translate): string =>
    cost > 0 ? `¢${cost.toLocaleString()}` : text("SkylinesMaps.JourneyPlanner.FREE", "Free");

const formatDistance = (metres: number): string => {
    if (metres < 1000) {
        return `${Math.max(10, Math.round(metres / 10) * 10)} m`;
    }

    return `${(metres / 1000).toFixed(metres < 10000 ? 1 : 0)} km`;
};

interface MarkerSizes {
    column: number;
    start: number;
    startInner: number;
    pin: number;
    icon: number;
    dot: number;
}

interface Connector {
    top: number;
    step: number;
    count: number;
}

const toOdd = (value: number, minimum: number): number => Math.max(minimum, Math.floor(value / 2) * 2 + 1);

const getMarkerSizes = (unit: number): MarkerSizes => {
    const start = toOdd(14 * unit, 5);
    const border = Math.max(1, Math.round(2 * unit));
    return {
        column: toOdd(22 * unit, 9),
        start,
        startInner: Math.max(1, start - border * 2),
        pin: toOdd(16 * unit, 5),
        icon: toOdd(18 * unit, 7),
        dot: toOdd(3 * unit, 3),
    };
};

type MarkerKind = "start" | "end" | "icon";

interface TimelineItem {
    marker: MarkerKind;
    icon?: string;
    content: JSX.Element;
}

const Timeline = ({
    items,
    gap,
    inset,
    align,
    style,
}: {
    items: TimelineItem[];
    gap: string;
    inset: string;
    align: string;
    style: React.CSSProperties;
}) => {
    const containerRef = useRef<HTMLDivElement>(null);
    const unitRef = useRef<HTMLDivElement>(null);
    const markerRefs = useRef<(HTMLElement | null)[]>([]);
    const [unit, setUnit] = useState(0);
    const [connectors, setConnectors] = useState<Connector[]>([]);

    const sizes = unit > 0 ? getMarkerSizes(unit) : null;

    useLayoutEffect(() => {
        const measure = (): boolean => {
            const reference = unitRef.current;
            const container = containerRef.current;
            if (!reference || !container) {
                return false;
            }

            const measured = reference.getBoundingClientRect().height / 100;
            if (measured <= 0) {
                return false;
            }

            const measuredSizes = getMarkerSizes(measured);
            const containerRect = container.getBoundingClientRect();
            const step = Math.max(measuredSizes.dot + 2, Math.round(6 * measured));
            const margin = Math.round(3 * measured);
            const next: Connector[] = [];

            for (let index = 0; index + 1 < items.length; index++) {
                const upper = markerRefs.current[index];
                const lower = markerRefs.current[index + 1];
                if (!upper || !lower) {
                    continue;
                }

                const upperRect = upper.getBoundingClientRect();
                const lowerRect = lower.getBoundingClientRect();
                if (upperRect.height <= 0 || lowerRect.height <= 0) {
                    continue;
                }

                const top = Math.round(upperRect.bottom - containerRect.top) + margin;
                const bottom = Math.round(lowerRect.top - containerRect.top) - margin;
                const available = bottom - top;
                const count = available >= measuredSizes.dot ? Math.floor((available - measuredSizes.dot) / step) + 1 : 0;
                if (count > 0) {
                    next.push({
                        top: top + Math.floor((available - ((count - 1) * step + measuredSizes.dot)) / 2),
                        step,
                        count,
                    });
                }
            }

            if (Math.abs(measured - unit) > 0.001) {
                setUnit(measured);
            }

            const changed = next.length !== connectors.length || next.some((connector, index) =>
                connector.top !== connectors[index].top ||
                connector.step !== connectors[index].step ||
                connector.count !== connectors[index].count);

            if (changed) {
                setConnectors(next);
            }

            return true;
        };

        if (measure()) {
            return;
        }

        const frame = requestAnimationFrame(() => {
            measure();
        });

        return () => cancelAnimationFrame(frame);
    });

    const columnStyle: React.CSSProperties = sizes
        ? { ...markerColumnStyle, width: `${sizes.column}px`, height: `${sizes.column}px` }
        : markerColumnStyle;

    const renderMarker = (item: TimelineItem, index: number) => {
        const setRef = (element: HTMLElement | null) => {
            markerRefs.current[index] = element;
        };

        if (item.marker === "start") {
            const startStyle: React.CSSProperties = sizes
                ? { ...startDotStyle, width: `${sizes.start}px`, height: `${sizes.start}px`, borderRadius: `${sizes.start / 2}px` }
                : startDotStyle;
            const startInnerStyle: React.CSSProperties = sizes
                ? { ...startDotInnerStyle, width: `${sizes.startInner}px`, height: `${sizes.startInner}px`, borderRadius: `${sizes.startInner / 2}px` }
                : startDotInnerStyle;

            return (
                <div ref={setRef} style={startStyle}>
                    <div style={startInnerStyle} />
                </div>
            );
        }

        if (item.marker === "end") {
            const pinStyle: React.CSSProperties = sizes
                ? { width: `${sizes.pin}px`, height: `${sizes.pin}px` }
                : endPinStyle;

            return <img ref={setRef} src={endIcon} style={pinStyle} />;
        }

        const iconStyle: React.CSSProperties = sizes
            ? { width: `${sizes.icon}px`, height: `${sizes.icon}px` }
            : timelineIconStyle;

        return <img ref={setRef} src={item.icon} style={iconStyle} />;
    };

    return (
        <div ref={containerRef} style={{ ...style, position: "relative" }}>
            <div ref={unitRef} style={unitProbeStyle} />
            {items.map((item, index) => (
                <div
                    key={index}
                    style={{
                        ...placeRowStyle,
                        alignItems: align,
                        marginBottom: index + 1 < items.length ? gap : "0rem",
                    }}
                >
                    <div style={columnStyle}>{renderMarker(item, index)}</div>
                    <div style={timelineContentStyle}>{item.content}</div>
                </div>
            ))}
            {sizes && connectors.map((connector, connectorIndex) => (
                <div
                    key={connectorIndex}
                    style={{
                        ...connectorStyle,
                        left: inset,
                        width: `${sizes.column}px`,
                        paddingTop: `${connector.top}px`,
                    }}
                >
                    {Array.from({ length: connector.count }, (_, index) => (
                        <div
                            key={index}
                            style={{
                                ...connectorDotStyle,
                                marginTop: index === 0 ? "0px" : `${connector.step - sizes.dot}px`,
                                width: `${sizes.dot}px`,
                                height: `${sizes.dot}px`,
                                borderRadius: `${sizes.dot / 2}px`,
                            }}
                        />
                    ))}
                </div>
            ))}
        </div>
    );
};

const Places = ({ from, to }: { from: string; to: string }) => (
    <Timeline
        items={[
            { marker: "start", content: <span style={placeLabelStyle}>{from}</span> },
            { marker: "end", content: <span style={placeLabelStyle}>{to}</span> },
        ]}
        gap="12rem"
        inset="12rem"
        align="center"
        style={placesStyle}
    />
);

const ModeTab = ({
    mode,
    index,
    plan,
    text,
    ui,
}: {
    mode: typeof kModes[number];
    index: number;
    plan: JourneyPlan;
    text: Translate;
    ui: TabUi;
}) => {
    const [hovered, setHovered] = useState(false);
    const info = plan.modes[index];
    const available = !!info && info.available;
    const selected = plan.mode === index;
    const colour = selected ? kTabSelectedText : kTabText;

    const tab = (
        <div
            style={{
                ...tabStyle,
                backgroundColor: selected ? kTabSelected : hovered ? kTabHover : kTabIdle,
                opacity: available || selected ? 1 : 0.5,
                cursor: selected ? "default" : "pointer",
            }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
            onClick={() => {
                if (!selected) {
                    selectMode(index);
                }
            }}
        >
            {ui.TintedIcon
                ? <ui.TintedIcon src={mode.icon} style={{ ...tabIconStyle, backgroundColor: colour }} />
                : <img src={mode.icon} style={tabIconStyle} />}
            <span style={{ ...tabTimeStyle, color: colour }}>
                {available ? formatDuration(info.gameDuration, false) : text("SkylinesMaps.JourneyPlanner.NO_ROUTE", "No route")}
            </span>
        </div>
    );

    return (
        <div style={tabSlotStyle}>
            {ui.Tooltip ? <ui.Tooltip tooltip={text(mode.id, mode.fallback)}>{tab}</ui.Tooltip> : tab}
        </div>
    );
};

const ModeTabs = ({ plan, text }: { plan: JourneyPlan; text: Translate }) => {
    const ui = getTabUi();

    return (
        <div style={tabsRowStyle}>
            {kModes.map((mode, index) => (
                <ModeTab key={index} mode={mode} index={index} plan={plan} text={text} ui={ui} />
            ))}
        </div>
    );
};

const LegRow = ({ leg, text }: { leg: JourneyLeg; text: Translate }) => {
    if (leg.type === 0) {
        return (
            <div style={legRowStyle}>
                <span style={legTextStyle}>
                    {`${text("SkylinesMaps.JourneyPlanner.WALK", "Walk")} ${formatDuration(leg.gameDuration, false)} · ${formatDistance(leg.distance)}`}
                </span>
            </div>
        );
    }

    const name = leg.line || kTransportNames[leg.transport] || text("SkylinesMaps.JourneyPlanner.LINE", "Line");
    const stops = `${leg.stops} ${leg.stops === 1
        ? text("SkylinesMaps.JourneyPlanner.STOP", "stop")
        : text("SkylinesMaps.JourneyPlanner.STOPS", "stops")}`;
    const details = [
        leg.wait >= 60 ? `${text("SkylinesMaps.JourneyPlanner.WAIT", "Wait")} ${formatDuration(leg.wait, false)}` : "",
        stops,
        formatDuration(leg.gameDuration, false),
    ].filter((part) => part.length > 0).join(" · ");
    const places = leg.from && leg.to
        ? `${leg.from} ${text("SkylinesMaps.JourneyPlanner.TO", "to")} ${leg.to}`
        : leg.from || leg.to;

    return (
        <div>
            <div style={legRowStyle}>
                <span style={{ ...linePillStyle, backgroundColor: leg.color, color: getLabelColour(leg.color) }}>{name}</span>
                {places && <span style={legTextStyle}>{places}</span>}
            </div>
            <div style={legDetailStyle}>{details}</div>
        </div>
    );
};

const buildLegItems = (legs: JourneyLeg[], text: Translate): TimelineItem[] => {
    const items: TimelineItem[] = [];
    legs.forEach((leg, index) => {
        items.push({
            marker: "icon",
            icon: leg.type === 0 ? walkIcon : kTransportIcons[leg.transport] ?? transitIcon,
            content: <LegRow leg={leg} text={text} />,
        });

        if (leg.type !== 0 && legs.slice(index + 1).some((next) => next.type !== 0)) {
            const change = text("SkylinesMaps.JourneyPlanner.CHANGE", "Change");
            items.push({
                marker: "icon",
                icon: changeIcon,
                content: (
                    <div style={legRowStyle}>
                        <span style={legTextStyle}>
                            {leg.to ? `${change} ${text("SkylinesMaps.JourneyPlanner.AT", "at")} ${leg.to}` : change}
                        </span>
                    </div>
                ),
            });
        }
    });

    return items;
};

const RouteRow = ({
    route,
    index,
    selected,
    single,
    text,
}: {
    route: JourneyRoute;
    index: number;
    selected: boolean;
    single: boolean;
    text: Translate;
}) => {
    const tagLabel = kTags
        .filter((tag) => (route.tags & tag.flag) !== 0)
        .map((tag) => text(tag.id, tag.fallback))
        .join(" · ");
    const minutesSlower = Math.round(route.delta / 60);

    const note = single
        ? ""
        : index === 0
            ? text("SkylinesMaps.JourneyPlanner.BEST", "Best route")
            : minutesSlower > 0
                ? `+${formatDuration(route.delta, false)}`
                : text("SkylinesMaps.JourneyPlanner.SIMILAR", "Similar time");
    const timeColour = route.traffic >= 0
        ? kTrafficColours[route.traffic] ?? kTrafficColours[0]
        : "var(--textColor)";
    const hasLegs = !!route.legs && route.legs.length > 0;

    const via = route.via
        ? `${text("SkylinesMaps.JourneyPlanner.VIA", "via")} ${route.via}`
        : tagLabel;

    return (
        <div style={selected ? selectedRowStyle : rowStyle} onClick={() => selectRoute(index)}>
            <div style={rowLineStyle}>
                <div style={timeColumnStyle}>
                    <span style={{ ...durationStyle, color: timeColour }}>
                        {formatDuration(route.gameDuration, false)}
                    </span>
                    <span style={realTimeStyle}>
                        {`${formatDuration(route.duration, true)} ${text("SkylinesMaps.JourneyPlanner.REAL_TIME", "real time")}`}
                    </span>
                </div>
                <div style={sideColumnStyle}>
                    <span style={distanceStyle}>{formatDistance(route.distance)}</span>
                    {hasLegs && <span style={costStyle}>{formatCost(route.cost, text)}</span>}
                </div>
            </div>
            {hasLegs && tagLabel && <div style={kindStyle}>{tagLabel}</div>}
            {hasLegs && selected ? (
                <Timeline
                    items={buildLegItems(route.legs, text)}
                    gap="10rem"
                    inset="0rem"
                    align="flex-start"
                    style={legsStyle}
                />
            ) : hasLegs ? (
                <div style={rowLineStyle}>
                    <div style={chipsStyle}>
                        {route.legs.filter((leg) => leg.type !== 0).map((leg, legIndex) => (
                            <div key={legIndex} style={chipStyle}>
                                {legIndex > 0 && <img src={changeIcon} style={changeChipIconStyle} />}
                                <img src={kTransportIcons[leg.transport] ?? transitIcon} style={chipIconStyle} />
                                <span style={{ ...linePillStyle, backgroundColor: leg.color, color: getLabelColour(leg.color) }}>
                                    {leg.line || kTransportNames[leg.transport] || text("SkylinesMaps.JourneyPlanner.LINE", "Line")}
                                </span>
                            </div>
                        ))}
                    </div>
                    <span style={noteStyle}>{note}</span>
                </div>
            ) : (
                <div style={rowLineStyle}>
                    <span style={viaStyle}>{via}</span>
                    <span style={noteStyle}>{note}</span>
                </div>
            )}
            {!hasLegs && route.via && tagLabel && <div style={kindStyle}>{tagLabel}</div>}
        </div>
    );
};

export const JourneyCard = () => {
    const isOwner = useSingleCard();
    const plan = useValue(plan$);
    const { translate } = useLocalization();

    if (!plan || !isOwner) {
        return null;
    }

    const text: Translate = (id, fallback) => translate(id, fallback) ?? fallback;

    return (
        <div style={containerStyle}>
            <Panel header={text("SkylinesMaps.JourneyPlanner.CARD_TITLE", "Journey")} onClose={clearJourney}>
                {!plan.searching && plan.modes && <ModeTabs plan={plan} text={text} />}
                <Places from={plan.from} to={plan.to} />
                {plan.searching ? (
                    <div style={statusStyle}>
                        {text("SkylinesMaps.JourneyPlanner.SEARCHING_ROUTES", "Finding routes...")}
                    </div>
                ) : plan.routes.length === 0 ? (
                    <div style={statusStyle}>
                        {plan.reason
                            ? text(plan.reason, kReasonFallbacks[plan.mode] ?? kReasonFallbacks[0])
                            : kReasonFallbacks[plan.mode] ?? kReasonFallbacks[0]}
                    </div>
                ) : (
                    plan.routes.map((route, index) => (
                        <RouteRow
                            key={index}
                            route={route}
                            index={index}
                            selected={index === plan.selected}
                            single={plan.routes.length === 1}
                            text={text}
                        />
                    ))
                )}
            </Panel>
        </div>
    );
};

const containerStyle: React.CSSProperties = {
    position: "absolute",
    top: "70rem",
    right: "16rem",
    width: "340rem",
    pointerEvents: "auto",
};

const placesStyle: React.CSSProperties = {
    position: "relative",
    padding: "8rem 12rem",
    borderBottom: "1rem solid rgba(255, 255, 255, 0.15)",
};

const placeRowStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    marginTop: "3rem",
    marginBottom: "3rem",
    minHeight: "22rem",
};

const markerColumnStyle: React.CSSProperties = {
    width: "22rem",
    height: "22rem",
    marginRight: "8rem",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
};

const unitProbeStyle: React.CSSProperties = {
    position: "absolute",
    top: "0rem",
    left: "0rem",
    width: "0rem",
    height: "100rem",
    opacity: 0,
    pointerEvents: "none",
};

const connectorStyle: React.CSSProperties = {
    position: "absolute",
    top: "0rem",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    pointerEvents: "none",
};

const connectorDotStyle: React.CSSProperties = {
    flexShrink: 0,
    backgroundColor: "rgba(255, 255, 255, 0.6)",
};

const startDotStyle: React.CSSProperties = {
    width: "14rem",
    height: "14rem",
    borderRadius: "7rem",
    backgroundColor: "#FFFFFF",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
};

const startDotInnerStyle: React.CSSProperties = {
    width: "10rem",
    height: "10rem",
    borderRadius: "5rem",
    backgroundColor: kStartColour,
};

const endPinStyle: React.CSSProperties = {
    width: "16rem",
    height: "16rem",
};

const placeLabelStyle: React.CSSProperties = {
    fontSize: "14rem",
    color: "var(--textColor)",
    overflow: "hidden",
    flex: 1,
};

const statusStyle: React.CSSProperties = {
    padding: "12rem",
    fontSize: "14rem",
    color: "var(--textColor)",
    opacity: 0.8,
};

const rowStyle: React.CSSProperties = {
    padding: "8rem 12rem 8rem 8rem",
    borderLeft: "4rem solid rgba(0, 0, 0, 0)",
    borderBottom: "1rem solid rgba(255, 255, 255, 0.1)",
    cursor: "pointer",
};

const selectedRowStyle: React.CSSProperties = {
    ...rowStyle,
    borderLeft: `4rem solid ${kSelectedColour}`,
    backgroundColor: "rgba(66, 133, 244, 0.18)",
};

const rowLineStyle: React.CSSProperties = {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
};

const timeColumnStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
};

const durationStyle: React.CSSProperties = {
    fontSize: "20rem",
    fontWeight: "bold",
};

const realTimeStyle: React.CSSProperties = {
    fontSize: "12rem",
    color: "var(--textColor)",
    opacity: 0.7,
};

const distanceStyle: React.CSSProperties = {
    fontSize: "14rem",
    color: "var(--textColor)",
    opacity: 0.8,
};

const viaStyle: React.CSSProperties = {
    fontSize: "13rem",
    color: "var(--textColor)",
    opacity: 0.85,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    marginRight: "8rem",
};

const noteStyle: React.CSSProperties = {
    fontSize: "13rem",
    color: "var(--textColor)",
    flexShrink: 0,
};

const kTabIdle = "rgba(255, 255, 255, 0.1)";
const kTabHover = "rgba(255, 255, 255, 0.2)";
const kTabSelected = "rgba(74, 155, 232, 1)";
const kTabText = "rgba(217, 217, 217, 1)";
const kTabSelectedText = "rgba(255, 255, 255, 1)";

const tabsRowStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "row",
    width: "100%",
    padding: "8rem 12rem 0rem 12rem",
};

const tabSlotStyle: React.CSSProperties = {
    flex: 1,
    width: "0rem",
    marginLeft: "2rem",
    marginRight: "2rem",
};

const tabStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    width: "100%",
    padding: "6rem 0rem 5rem 0rem",
    borderRadius: "6rem",
};

const tabIconStyle: React.CSSProperties = {
    width: "28rem",
    height: "28rem",
};

const tabTimeStyle: React.CSSProperties = {
    fontSize: "11rem",
    marginTop: "3rem",
    whiteSpace: "nowrap",
};

const legsStyle: React.CSSProperties = {
    marginTop: "6rem",
};

const legRowStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    minHeight: "22rem",
};

const timelineIconStyle: React.CSSProperties = {
    width: "18rem",
    height: "18rem",
};

const timelineContentStyle: React.CSSProperties = {
    flex: 1,
    minWidth: 0,
};

const linePillStyle: React.CSSProperties = {
    fontSize: "12rem",
    fontWeight: "bold",
    padding: "1rem 6rem",
    borderRadius: "4rem",
    marginRight: "6rem",
    flexShrink: 0,
    whiteSpace: "nowrap",
};

const legTextStyle: React.CSSProperties = {
    fontSize: "12rem",
    color: "var(--textColor)",
    opacity: 0.85,
    flex: 1,
    minWidth: 0,
    paddingRight: "2rem",
};

const legDetailStyle: React.CSSProperties = {
    fontSize: "11rem",
    color: "var(--textColor)",
    opacity: 0.6,
    marginTop: "1rem",
};

const sideColumnStyle: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
};

const costStyle: React.CSSProperties = {
    fontSize: "13rem",
    color: "var(--textColor)",
    opacity: 0.85,
    marginTop: "2rem",
};

const chipsStyle: React.CSSProperties = {
    display: "flex",
    flexWrap: "wrap",
    alignItems: "center",
    flex: 1,
    minWidth: 0,
    marginTop: "4rem",
};

const chipStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    marginRight: "4rem",
    marginTop: "2rem",
};

const chipIconStyle: React.CSSProperties = {
    width: "18rem",
    height: "18rem",
    marginRight: "4rem",
};

const changeChipIconStyle: React.CSSProperties = {
    width: "14rem",
    height: "14rem",
    marginRight: "6rem",
    opacity: 0.8,
};

const kindStyle: React.CSSProperties = {
    fontSize: "12rem",
    color: "var(--textColor)",
    opacity: 0.6,
    marginTop: "2rem",
};
