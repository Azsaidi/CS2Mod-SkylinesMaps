import { bindTrigger, bindTriggerWithArgs, bindValue, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { Panel } from "cs2/ui";
import { useEffect, useRef, useState } from "react";
import endIcon from "images/journey-end.svg";

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

interface JourneyRoute {
    kind: number;
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
    selected: number;
    routes: JourneyRoute[];
}

const plan$ = bindValue<JourneyPlan | null>(kGroup, "journeyPlan", null);
const selectRoute = bindTriggerWithArgs<[number]>(kGroup, "selectJourneyRoute");
const clearJourney = bindTrigger(kGroup, "clearJourney");

const kTrafficColours = ["#5BB974", "#F29900", "#EE675C"];
const kStartColour = "#4285F4";
const kSelectedColour = "#4285F4";

const kKinds = [
    { id: "SkylinesMaps.JourneyPlanner.KIND_FASTEST", fallback: "Fastest" },
    { id: "SkylinesMaps.JourneyPlanner.KIND_IGNORE_TRAFFIC", fallback: "Usual route" },
    { id: "SkylinesMaps.JourneyPlanner.KIND_FEWER_TURNS", fallback: "Fewer turns" },
    { id: "SkylinesMaps.JourneyPlanner.KIND_SHORTEST", fallback: "Shortest" },
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
        column: toOdd(18 * unit, 7),
        start,
        startInner: Math.max(1, start - border * 2),
        pin: toOdd(16 * unit, 5),
        dot: toOdd(3 * unit, 3),
    };
};

const Places = ({ from, to }: { from: string; to: string }) => {
    const containerRef = useRef<HTMLDivElement>(null);
    const unitRef = useRef<HTMLDivElement>(null);
    const startRef = useRef<HTMLDivElement>(null);
    const endRef = useRef<HTMLImageElement>(null);
    const [unit, setUnit] = useState(0);
    const [connector, setConnector] = useState<Connector | null>(null);

    const sizes = unit > 0 ? getMarkerSizes(unit) : null;

    useEffect(() => {
        const frame = requestAnimationFrame(() => {
            const reference = unitRef.current;
            if (reference) {
                const measured = reference.getBoundingClientRect().height / 100;
                if (measured > 0 && Math.abs(measured - unit) > 0.001) {
                    setUnit(measured);
                    return;
                }
            }

            const container = containerRef.current;
            const start = startRef.current;
            const end = endRef.current;
            if (!sizes || !container || !start || !end) {
                return;
            }

            const containerRect = container.getBoundingClientRect();
            const startRect = start.getBoundingClientRect();
            const endRect = end.getBoundingClientRect();
            if (endRect.height <= 0) {
                return;
            }

            const step = Math.max(sizes.dot + 2, Math.round(6 * unit));
            const margin = Math.round(3 * unit);
            const top = Math.round(startRect.bottom - containerRect.top) + margin;
            const bottom = Math.round(endRect.top - containerRect.top) - margin;
            const available = bottom - top;

            const count = available >= sizes.dot ? Math.floor((available - sizes.dot) / step) + 1 : 0;
            const next: Connector = {
                top: count > 0 ? top + Math.floor((available - ((count - 1) * step + sizes.dot)) / 2) : 0,
                step,
                count,
            };

            if (!connector ||
                connector.top !== next.top ||
                connector.step !== next.step ||
                connector.count !== next.count) {
                setConnector(next);
            }
        });

        return () => cancelAnimationFrame(frame);
    });

    const columnStyle: React.CSSProperties = sizes
        ? { ...markerColumnStyle, width: `${sizes.column}px`, height: `${sizes.column}px` }
        : markerColumnStyle;

    const startStyle: React.CSSProperties = sizes
        ? { ...startDotStyle, width: `${sizes.start}px`, height: `${sizes.start}px`, borderRadius: `${sizes.start / 2}px` }
        : startDotStyle;

    const startInnerStyle: React.CSSProperties = sizes
        ? { ...startDotInnerStyle, width: `${sizes.startInner}px`, height: `${sizes.startInner}px`, borderRadius: `${sizes.startInner / 2}px` }
        : startDotInnerStyle;

    const pinStyle: React.CSSProperties = sizes
        ? { width: `${sizes.pin}px`, height: `${sizes.pin}px` }
        : endPinStyle;

    return (
        <div ref={containerRef} style={placesStyle}>
            <div ref={unitRef} style={unitProbeStyle} />
            <div style={{ ...placeRowStyle, marginBottom: "12rem" }}>
                <div style={columnStyle}>
                    <div ref={startRef} style={startStyle}>
                        <div style={startInnerStyle} />
                    </div>
                </div>
                <span style={placeLabelStyle}>{from}</span>
            </div>
            <div style={placeRowStyle}>
                <div style={columnStyle}><img ref={endRef} src={endIcon} style={pinStyle} /></div>
                <span style={placeLabelStyle}>{to}</span>
            </div>
            {sizes && connector && connector.count > 0 && (
                <div
                    style={{
                        ...connectorStyle,
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
            )}
        </div>
    );
};

const RouteRow = ({
    route,
    index,
    selected,
    text,
}: {
    route: JourneyRoute;
    index: number;
    selected: boolean;
    text: Translate;
}) => {
    const kind = kKinds[route.kind] ?? kKinds[0];
    const kindLabel = text(kind.id, kind.fallback);
    const minutesSlower = Math.round(route.delta / 60);

    const note = index === 0
        ? text("SkylinesMaps.JourneyPlanner.BEST", "Best route")
        : minutesSlower > 0
            ? `+${formatDuration(route.delta, false)}`
            : text("SkylinesMaps.JourneyPlanner.SIMILAR", "Similar time");

    const via = route.via
        ? `${text("SkylinesMaps.JourneyPlanner.VIA", "via")} ${route.via}`
        : kindLabel;

    return (
        <div style={selected ? selectedRowStyle : rowStyle} onClick={() => selectRoute(index)}>
            <div style={rowLineStyle}>
                <div style={timeColumnStyle}>
                    <span style={{ ...durationStyle, color: kTrafficColours[route.traffic] ?? kTrafficColours[0] }}>
                        {formatDuration(route.gameDuration, false)}
                    </span>
                    <span style={realTimeStyle}>
                        {`${formatDuration(route.duration, true)} ${text("SkylinesMaps.JourneyPlanner.REAL_TIME", "real time")}`}
                    </span>
                </div>
                <span style={distanceStyle}>{formatDistance(route.distance)}</span>
            </div>
            <div style={rowLineStyle}>
                <span style={viaStyle}>{via}</span>
                <span style={noteStyle}>{note}</span>
            </div>
            {route.via && <div style={kindStyle}>{kindLabel}</div>}
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
                <Places from={plan.from} to={plan.to} />
                {plan.searching ? (
                    <div style={statusStyle}>
                        {text("SkylinesMaps.JourneyPlanner.SEARCHING_ROUTES", "Finding routes...")}
                    </div>
                ) : (
                    plan.routes.map((route, index) => (
                        <RouteRow
                            key={index}
                            route={route}
                            index={index}
                            selected={index === plan.selected}
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
    minHeight: "18rem",
};

const markerColumnStyle: React.CSSProperties = {
    width: "18rem",
    height: "18rem",
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
    left: "12rem",
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

const kindStyle: React.CSSProperties = {
    fontSize: "12rem",
    color: "var(--textColor)",
    opacity: 0.6,
    marginTop: "2rem",
};
