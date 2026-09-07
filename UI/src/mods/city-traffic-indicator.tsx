import { bindValue, useValue } from "cs2/api";

/** Live city wide congestion, 0 (jammed) to 100 (free flowing). */
const cityFlow$ = bindValue<number>("skylinesMaps", "cityFlow", 100);

/** Whether the readout should be shown at all, from the mod settings. */
const show$ = bindValue<boolean>("skylinesMaps", "showCityTraffic", false);

/** Left to right 0 to 100% scale with a marker at the current city wide average. */
export const CityTrafficIndicator = () => {
    const show = useValue(show$);
    const flow = useValue(cityFlow$);


    if (!show) {
        return null;
    }

    const percent = Math.max(0, Math.min(100, Math.round(flow)));

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

const headerStyle: React.CSSProperties = {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "baseline",
    marginBottom: "6rem",
};

const titleStyle: React.CSSProperties = {
    letterSpacing: "1rem",
    opacity: 0.75,
};

const valueStyle: React.CSSProperties = {
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
