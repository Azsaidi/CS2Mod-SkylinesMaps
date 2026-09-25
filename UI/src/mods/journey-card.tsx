import { bindTrigger, bindTriggerWithArgs, bindValue, useValue } from "cs2/api";
import { time } from "cs2/bindings";
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
    duration: number;
    gameDuration: number;
    realWait: number;
    wait: number;
    distance: number;
    line: string;
    color: string;
    transport: number;
    from: string;
    to: string;
    fromShort: string;
    toShort: string;
    stops: number;
    price: number;
}

interface JourneyMode {
    available: boolean;
    duration: number;
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
    realDelta: number;
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
const labelRects$ = bindValue<number[][]>(kGroup, "journeyLabelRects", []);
const times$ = bindValue<number>(kGroup, "journeyTimes", 0);
const stepAddresses$ = bindValue<boolean>(kGroup, "journeyStepAddresses", true);
const arrivalClock$ = bindValue<number>(kGroup, "journeyArrivalClock", 0);

enum TimeDisplay {
    RealAndGame = 0,
    RealTimeOnly = 1,
    GameTimeOnly = 2,
}

interface CardView {
    game: boolean;
    real: boolean;
    addresses: boolean;
}

const getCardView = (display: number, addresses: boolean): CardView => ({
    game: display !== TimeDisplay.RealTimeOnly,
    real: display !== TimeDisplay.GameTimeOnly,
    addresses,
});

const kSeparator = ", ";

const kLegLineHeight = "18rem";

const kGameTwelveHours = 1;

interface UnitSettings {
    timeFormat: number;
}

const unitSettings$ = bindValue<UnitSettings | null>("options", "unitSettings", null);

enum ClockFormat {
    MatchGame = 0,
    TwentyFourHour = 1,
    TwelveHour = 2,
}
const selectRoute = bindTriggerWithArgs<[number]>(kGroup, "selectJourneyRoute");
const clearJourney = bindTrigger(kGroup, "clearJourney");
const swapJourney = bindTrigger(kGroup, "swapJourney");
const focusJourneyPlace = bindTriggerWithArgs<[boolean]>(kGroup, "focusJourneyPlace");
const selectMode = bindTriggerWithArgs<[number]>(kGroup, "selectJourneyMode");
const hoverLabel = bindTriggerWithArgs<[number]>(kGroup, "hoverJourneyLabel");

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

const useGameMinutes = (): number => {
    const ticks = useValue(time.ticks$);
    const settings = useValue(time.timeSettings$);

    if (!settings || !settings.ticksPerDay) {
        return -1;
    }

    const minutes = time.calculateMinutesSinceMidnightFromTicks(settings, ticks);
    return Number.isFinite(minutes) ? minutes : -1;
};

const pad2 = (value: number): string => (value < 10 ? `0${value}` : `${value}`);

const formatClock = (minutes: number, twelveHour: boolean, text: Translate): string => {
    const wrapped = ((Math.round(minutes) % 1440) + 1440) % 1440;
    const hour = Math.floor(wrapped / 60);
    const minute = wrapped % 60;

    if (!twelveHour) {
        return text("Common.TIME_FORMAT", "{HOUR}:{MINUTE}")
            .replace("{HOUR}", pad2(hour))
            .replace("{MINUTE}", pad2(minute));
    }

    const period = hour < 12
        ? text("Common.TIME_PERIOD_AM", "AM")
        : text("Common.TIME_PERIOD_PM", "PM");

    return text("Common.TIME_FORMAT_12", "{HOUR}:{MINUTE} {PERIOD}")
        .replace("{HOUR}", pad2(hour % 12 || 12))
        .replace("{MINUTE}", pad2(minute))
        .replace("{PERIOD}", period);
};

const formatLegTime = (leg: JourneyLeg, view: CardView, text: Translate): string => {
    const game = view.game ? formatDuration(leg.gameDuration, false) : "";
    if (!view.real) {
        return game;
    }

    const real = formatDuration(leg.duration, true);
    return game ? `${game} / ${real} ${text("SkylinesMaps.JourneyPlanner.REAL_SHORT", "real")}` : real;
};

const ArrivalTime = ({ route, text }: { route: JourneyRoute; text: Translate }) => {
    const minutes = useGameMinutes();
    const clock = useValue(arrivalClock$);
    const units = useValue(unitSettings$);

    if (minutes < 0) {
        return null;
    }

    const twelveHour = clock === ClockFormat.MatchGame
        ? !!units && units.timeFormat === kGameTwelveHours
        : clock === ClockFormat.TwelveHour;

    return (
        <span style={etaStyle}>
            {`${text("SkylinesMaps.JourneyPlanner.ETA", "ETA")} ${formatClock(minutes + route.gameDuration / 60, twelveHour, text)}`}
        </span>
    );
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
    markerHeight,
    style,
}: {
    items: TimelineItem[];
    gap: string;
    inset: string;
    align: string;
    markerHeight?: string;
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
        ? { ...markerColumnStyle, width: `${sizes.column}px`, height: markerHeight ?? `${sizes.column}px` }
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

const SwapButton = ({ text }: { text: Translate }) => {
    const [hovered, setHovered] = useState(false);
    const ui = getTabUi();

    const button = (
        <div
            style={{ ...swapButtonStyle, backgroundColor: hovered ? kTabHover : "rgba(0, 0, 0, 0)" }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
            onClick={() => swapJourney()}
        >
            {ui.TintedIcon
                ? <ui.TintedIcon src={changeIcon} style={{ ...swapIconStyle, backgroundColor: kTabText }} />
                : <img src={changeIcon} style={swapIconStyle} />}
        </div>
    );

    return ui.Tooltip
        ? <ui.Tooltip tooltip={text("SkylinesMaps.JourneyPlanner.SWAP", "Swap start and end")}>{button}</ui.Tooltip>
        : button;
};

const PlaceLabel = ({ name, end, text }: { name: string; end: boolean; text: Translate }) => {
    const [hovered, setHovered] = useState(false);
    const ui = getTabUi();

    const label = (
        <div
            style={{ ...placeButtonStyle, backgroundColor: hovered ? kTabHover : "rgba(0, 0, 0, 0)" }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
            onClick={() => focusJourneyPlace(end)}
        >
            <span style={placeLabelStyle}>{name}</span>
        </div>
    );

    return ui.Tooltip
        ? <ui.Tooltip tooltip={text("SkylinesMaps.JourneyPlanner.FOCUS_PLACE", "Focus the camera on this place")}>{label}</ui.Tooltip>
        : label;
};

const Places = ({ from, to, text }: { from: string; to: string; text: Translate }) => (
    <div style={placesStyle}>
        <Timeline
            items={[
                { marker: "start", content: <PlaceLabel name={from} end={false} text={text} /> },
                { marker: "end", content: <PlaceLabel name={to} end={true} text={text} /> },
            ]}
            gap="12rem"
            inset="0rem"
            align="center"
            style={placesTimelineStyle}
        />
        <SwapButton text={text} />
    </div>
);

const ModeTab = ({
    mode,
    index,
    plan,
    view,
    text,
    ui,
}: {
    mode: typeof kModes[number];
    index: number;
    plan: JourneyPlan;
    view: CardView;
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
                {available
                    ? formatDuration(view.game ? info.gameDuration : info.duration, !view.game)
                    : text("SkylinesMaps.JourneyPlanner.NO_ROUTE", "No route")}
            </span>
        </div>
    );

    return (
        <div style={tabSlotStyle}>
            {ui.Tooltip ? <ui.Tooltip tooltip={text(mode.id, mode.fallback)}>{tab}</ui.Tooltip> : tab}
        </div>
    );
};

const ModeTabs = ({ plan, view, text }: { plan: JourneyPlan; view: CardView; text: Translate }) => {
    const ui = getTabUi();

    return (
        <div style={tabsRowStyle}>
            {kModes.map((mode, index) => (
                <ModeTab
                    key={index}
                    mode={mode}
                    index={index}
                    plan={plan}
                    view={view}
                    text={text}
                    ui={ui}
                />
            ))}
        </div>
    );
};

const LegRow = ({ leg, view, text }: { leg: JourneyLeg; view: CardView; text: Translate }) => {
    const total = formatLegTime(leg, view, text);

    if (leg.type === 0) {
        return (
            <div style={legRowStyle}>
                <span style={legTextStyle}>
                    {`${text("SkylinesMaps.JourneyPlanner.WALK", "Walk")} `}
                    <span style={legTotalStyle}>{total}</span>
                    {`${kSeparator}${formatDistance(leg.distance)}`}
                </span>
            </div>
        );
    }

    const name = leg.line || kTransportNames[leg.transport] || text("SkylinesMaps.JourneyPlanner.LINE", "Line");
    const stops = `${leg.stops} ${leg.stops === 1
        ? text("SkylinesMaps.JourneyPlanner.STOP", "stop")
        : text("SkylinesMaps.JourneyPlanner.STOPS", "stops")}`;
    const showWait = view.game ? leg.wait >= 60 : leg.realWait >= 1;
    const wait = showWait
        ? `${text("SkylinesMaps.JourneyPlanner.WAIT", "Wait")} ${formatDuration(view.game ? leg.wait : leg.realWait, !view.game)}`
        : "";
    const details = wait
        ? `${wait} ${text("SkylinesMaps.JourneyPlanner.THEN", "then")} ${stops}`
        : stops;
    const from = view.addresses ? leg.from : leg.fromShort || leg.from;
    const to = view.addresses ? leg.to : leg.toShort || leg.to;
    const places = from && to
        ? `${from} ${text("SkylinesMaps.JourneyPlanner.TO", "to")} ${to}`
        : from || to;

    return (
        <div>
            <div style={legRowStyle}>
                <span style={{ ...linePillStyle, backgroundColor: leg.color, color: getLabelColour(leg.color) }}>{name}</span>
            </div>
            {places && <div style={legPlacesStyle}>{places}</div>}
            <div style={legDetailStyle}>{details}</div>
            <div style={legTotalLineStyle}>{total}</div>
        </div>
    );
};

const buildLegItems = (legs: JourneyLeg[], view: CardView, text: Translate): TimelineItem[] => {
    const items: TimelineItem[] = [];
    legs.forEach((leg, index) => {
        items.push({
            marker: "icon",
            icon: leg.type === 0 ? walkIcon : kTransportIcons[leg.transport] ?? transitIcon,
            content: <LegRow leg={leg} view={view} text={text} />,
        });

        if (leg.type !== 0 && legs.slice(index + 1).some((next) => next.type !== 0)) {
            const change = text("SkylinesMaps.JourneyPlanner.CHANGE", "Change");
            const at = view.addresses ? leg.to : leg.toShort || leg.to;
            items.push({
                marker: "icon",
                icon: changeIcon,
                content: (
                    <div style={legRowStyle}>
                        <span style={legTextStyle}>
                            {at ? `${change} ${text("SkylinesMaps.JourneyPlanner.AT", "at")} ${at}` : change}
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
    view,
    text,
}: {
    route: JourneyRoute;
    index: number;
    selected: boolean;
    single: boolean;
    view: CardView;
    text: Translate;
}) => {
    const tagLabel = kTags
        .filter((tag) => (route.tags & tag.flag) !== 0)
        .map((tag) => text(tag.id, tag.fallback))
        .join(kSeparator);
    const delta = view.game ? route.delta : route.realDelta;
    const slower = view.game ? Math.round(delta / 60) : Math.round(delta);

    const note = single
        ? ""
        : index === 0
            ? text("SkylinesMaps.JourneyPlanner.BEST", "Best route")
            : slower > 0
                ? `+${formatDuration(delta, !view.game)}`
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
                        {formatDuration(view.game ? route.gameDuration : route.duration, !view.game)}
                    </span>
                    {view.game && <ArrivalTime route={route} text={text} />}
                    {view.game && view.real && (
                        <span style={realTimeStyle}>
                            {`${formatDuration(route.duration, true)} ${text("SkylinesMaps.JourneyPlanner.REAL_TIME", "real time")}`}
                        </span>
                    )}
                </div>
                <div style={sideColumnStyle}>
                    <span style={distanceStyle}>{formatDistance(route.distance)}</span>
                    {hasLegs && <span style={costStyle}>{formatCost(route.cost, text)}</span>}
                </div>
            </div>
            {hasLegs && tagLabel && <div style={kindStyle}>{tagLabel}</div>}
            {hasLegs && selected ? (
                <Timeline
                    items={buildLegItems(route.legs, view, text)}
                    gap="10rem"
                    inset="0rem"
                    align="flex-start"
                    markerHeight={kLegLineHeight}
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

const RouteLabelTargets = ({ plan }: { plan: JourneyPlan }) => {
    const rects = useValue(labelRects$);
    if (plan.searching || !rects || rects.length === 0) {
        return null;
    }

    return (
        <div style={labelLayerStyle}>
            {rects.map(([index, left, top, width, height]) => (
                <div
                    key={index}
                    style={{
                        ...labelTargetStyle,
                        left: `${left}px`,
                        top: `${top}px`,
                        width: `${width}px`,
                        height: `${height}px`,
                        cursor: index === plan.selected ? "default" : "pointer",
                    }}
                    onMouseEnter={() => hoverLabel(index)}
                    onMouseLeave={() => hoverLabel(-1)}
                    onClick={() => {
                        if (index !== plan.selected) {
                            selectRoute(index);
                        }
                    }}
                />
            ))}
        </div>
    );
};

export const JourneyCard = () => {
    const isOwner = useSingleCard();
    const plan = useValue(plan$);
    const view = getCardView(useValue(times$), useValue(stepAddresses$));
    const { translate } = useLocalization();

    if (!plan || !isOwner) {
        return null;
    }

    const text: Translate = (id, fallback) => translate(id, fallback) ?? fallback;

    return (
        <>
            <RouteLabelTargets plan={plan} />
            <div style={containerStyle}>
                <Panel header={text("SkylinesMaps.JourneyPlanner.CARD_TITLE", "Journey")} onClose={clearJourney}>
                    {!plan.searching && plan.modes && <ModeTabs plan={plan} view={view} text={text} />}
                    <Places from={plan.from} to={plan.to} text={text} />
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
                                view={view}
                                text={text}
                            />
                        ))
                    )}
                </Panel>
            </div>
        </>
    );
};

const labelLayerStyle: React.CSSProperties = {
    position: "absolute",
    top: "0px",
    left: "0px",
    width: "100%",
    height: "100%",
    zIndex: -1,
    pointerEvents: "none",
};

const labelTargetStyle: React.CSSProperties = {
    position: "absolute",
    pointerEvents: "auto",
};

const containerStyle: React.CSSProperties = {
    position: "absolute",
    top: "70rem",
    right: "16rem",
    width: "340rem",
    pointerEvents: "auto",
};

const placesStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    padding: "8rem 8rem 8rem 12rem",
    borderBottom: "1rem solid rgba(255, 255, 255, 0.15)",
};

const placesTimelineStyle: React.CSSProperties = {
    flex: 1,
    minWidth: 0,
};

const swapButtonStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "32rem",
    height: "32rem",
    marginLeft: "8rem",
    borderRadius: "6rem",
    flexShrink: 0,
    cursor: "pointer",
};

const swapIconStyle: React.CSSProperties = {
    width: "22rem",
    height: "22rem",
    transform: "rotate(90deg)",
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

const placeButtonStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    padding: "2rem 6rem",
    marginLeft: "-6rem",
    borderRadius: "4rem",
    cursor: "pointer",
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

const etaStyle: React.CSSProperties = {
    fontSize: "13rem",
    color: "var(--textColor)",
    opacity: 0.95,
};

const realTimeStyle: React.CSSProperties = etaStyle;

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
    alignItems: "flex-start",
    minHeight: kLegLineHeight,
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
    padding: "0rem 6rem",
    lineHeight: kLegLineHeight,
    borderRadius: "4rem",
    marginRight: "6rem",
    flexShrink: 0,
    whiteSpace: "nowrap",
};

const legTextStyle: React.CSSProperties = {
    fontSize: "12rem",
    lineHeight: kLegLineHeight,
    color: "var(--textColor)",
    opacity: 0.85,
    flex: 1,
    minWidth: 0,
    paddingRight: "2rem",
};

const legTotalStyle: React.CSSProperties = {
    fontWeight: "bold",
};

const legPlacesStyle: React.CSSProperties = {
    fontSize: "12rem",
    lineHeight: kLegLineHeight,
    color: "var(--textColor)",
    opacity: 0.85,
    marginTop: "1rem",
};

const legDetailStyle: React.CSSProperties = {
    fontSize: "11rem",
    color: "var(--textColor)",
    opacity: 0.6,
    marginTop: "1rem",
};

const legTotalLineStyle: React.CSSProperties = {
    ...legDetailStyle,
    fontSize: "12rem",
    opacity: 0.9,
    fontWeight: "bold",
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
