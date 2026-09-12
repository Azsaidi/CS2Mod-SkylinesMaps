import { bindValue, useValue } from "cs2/api";

/** Live city wide congestion, 0 (jammed) to 100 (free flowing). */
const cityFlow$ = bindValue<number>("skylinesMaps", "cityFlow", 100);

/** Mean of the stored day of readings, so the last 24 in game hours. */
const cityFlowAverage$ = bindValue<number>("skylinesMaps", "cityFlowAverage", 100);

/** Whether the readout should be shown at all, from the mod settings. */
const show$ = bindValue<boolean>("skylinesMaps", "showCityTraffic", false);

/** Left to right 0 to 100% scale with a marker at the current city wide average. */
export const CityTrafficIndicator = () => {
    const show = useValue(show$);
    const flow = useValue(cityFlow$);
    const average = useValue(cityFlowAverage$);


    if (!show) {
        return null;
    }

    const percent = Math.max(0, Math.min(100, Math.round(flow)));
    const averagePercent = Math.max(0, Math.min(100, Math.round(average)));

    return (
        <div style={panelStyle}>
            <div style={headerStyle}>
                <span style={titleStyle}>CITY TRAFFIC</span>
                <span style={valueStyle}>{percent}%</span>
            </div>

            <div style={barStyle}>
                <div style={{ ...markerStyle, left: `${percent}%` }} />
            </div>

            <div style={scaleStyle}>
                <span>0%</span>
                <span>Live Traffic Flow</span>
                <span>100%</span>
            </div>

            <div style={averageStyle}>
                <span style={titleStyle}>24 HOUR AVERAGE</span>
                <span style={valueStyle}>{averagePercent}%</span>
            </div>
        </div>
    );
};

const panelStyle: React.CSSProperties = {
    padding: "10rem 12rem 12rem 12rem",
    background: "rgba(0, 0, 0, 0.25)",
    color: "#e6edf3",
    fontSize: "12rem",
    pointerEvents: "auto",
};

// The title and the percentage are different sizes, so anything that lines up their boxes
// (align-items baseline or center) lands the glyphs by font metrics and they disagree. Both
// spans instead get the same height and a line height to match it: a single line then centres
// itself in its own box, and because the two boxes are the same height the two texts end up on
// the same centre line. Needs no flex behaviour, just line height.
const kRowHeight = "22rem";

const kRowTextStyle: React.CSSProperties = {
    height: kRowHeight,
    lineHeight: kRowHeight,
};

const headerStyle: React.CSSProperties = {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: "6rem",
};

// Copied from the game's own InfoviewPanelLabel, the component that draws TRAFFIC FLOW and
// MAP LEGEND in this panel: .labels_L7Q sets the size and colour, .left_Lgw the case. The font
// size is a variable rather than a fixed rem so it follows the game's font scale. Only the line
// height differs from vanilla, see kRowTextStyle.
const titleStyle: React.CSSProperties = {
    ...kRowTextStyle,
    fontSize: "var(--fontSizeS)",
    color: "var(--textColor)",
    textTransform: "uppercase",
};

const valueStyle: React.CSSProperties = {
    ...kRowTextStyle,
    fontSize: "18rem",
    fontWeight: "bold",
};

const barStyle: React.CSSProperties = {
    position: "relative",
    height: "10rem",
    borderRadius: "5rem",
    background: "linear-gradient(to right, #b31412 0%, #fbbc04 50%, #34a853 100%)",
};

const markerStyle: React.CSSProperties = {
    position: "absolute",
    top: "-3rem",
    width: "3rem",
    height: "16rem",
    marginLeft: "-1.5rem",
    borderRadius: "2rem",
    background: "#ffffff",
    boxShadow: "0 0 4rem rgba(0, 0, 0, 0.8)",
    // The value is only republished every few seconds, so glide rather than teleport.
    transition: "left 0.8s ease-out",
};

const scaleStyle: React.CSSProperties = {
    display: "flex",
    justifyContent: "space-between",
    marginTop: "5rem",
    fontSize: "13rem",
    opacity: 0.85,
};

// The header row again, so the label and figure line up under CITY TRAFFIC and its percentage.
// Only the spacing differs: a hairline above rather than a gap below.
const averageStyle: React.CSSProperties = {
    ...headerStyle,
    marginBottom: 0,
    marginTop: "7rem",
    paddingTop: "6rem",
    borderTop: "1rem solid rgba(255, 255, 255, 0.15)",
};
