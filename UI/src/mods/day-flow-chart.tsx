/**
 * The base game chart is a Chart.js line with five hard coded category labels, so it silently
 * drops everything past the fifth value. This draws the mod's full day of quarter hour readings
 * instead, in the same colours so it still reads as part of the panel.
 */

/** Readings per day, one per 15 in game minutes. Must match kHistorySlots in C#. */
const kSlots = 96;

// Lifted from the base game's traffic-chart.module.scss so the two look like one chart.
const kLine = "rgba(30, 179, 184, 1)";
const kFill = "rgba(30, 179, 184, 0.5)";
const kGrid = "rgba(255, 255, 255, 0.1)";
const kText = "rgba(255, 255, 255, 0.6)";

// Drawing space, in viewBox units.
const kWidth = 400;
const kHeight = 200;
const kLeft = 42;

// Wide enough for the midnight label at the right edge, which is centred on the axis and would
// otherwise be cut in half by the viewBox.
const kRight = 28;
const kTop = 10;
const kBottom = 26;

/// Length of the small stubs the base game draws outside each axis.
const kTick = 6;

const plotW = kWidth - kLeft - kRight;
const plotH = kHeight - kTop - kBottom;

const kYTicks = [0, 20, 40, 60, 80, 100];

/** Matches the base game's own axis, which marks each six hour quarter of the day. */
const kXTicks = [
    { at: 0, label: "12:00 AM" },
    { at: 24, label: "06:00 AM" },
    { at: 48, label: "12:00 PM" },
    { at: 72, label: "06:00 PM" },
    { at: 96, label: "12:00 AM" },
];

const x = (slot: number) => kLeft + (slot / kSlots) * plotW;
const y = (percent: number) => kTop + (1 - Math.max(0, Math.min(100, percent)) / 100) * plotH;

export const DayFlowChart = ({ data, className }: { data: number[]; className?: string }) => {
    // The day wraps, so midnight is repeated on the right edge the way the base game does it.
    const points: string[] = [];
    for (let i = 0; i < kSlots; i++) {
        points.push(`${x(i).toFixed(2)},${y(data[i]).toFixed(2)}`);
    }
    points.push(`${x(kSlots).toFixed(2)},${y(data[0]).toFixed(2)}`);

    const line = `M${points.join("L")}`;
    const area = `${line}L${x(kSlots).toFixed(2)},${y(0)}L${x(0).toFixed(2)},${y(0)}Z`;

    return (
        <svg
            className={className}
            viewBox={`0 0 ${kWidth} ${kHeight}`}
            preserveAspectRatio="none"
            style={{ width: "100%", height: "180rem", display: "block" }}
        >
            {kYTicks.map((tick) => (
                <g key={`y${tick}`}>
                    <line x1={kLeft} y1={y(tick)} x2={kLeft + plotW} y2={y(tick)} stroke={kGrid} strokeWidth={1.5} />
                    <line x1={kLeft - kTick} y1={y(tick)} x2={kLeft} y2={y(tick)} stroke={kGrid} strokeWidth={1.5} />
                    <text x={kLeft - kTick - 4} y={y(tick) + 4} textAnchor="end" fill={kText} fontSize={11} fontWeight="bold">
                        {tick} %
                    </text>
                </g>
            ))}

            {kXTicks.map((tick) => (
                <g key={`x${tick.at}`}>
                    <line x1={x(tick.at)} y1={kTop} x2={x(tick.at)} y2={kTop + plotH} stroke={kGrid} strokeWidth={1.5} />
                    <line
                        x1={x(tick.at)}
                        y1={kTop + plotH}
                        x2={x(tick.at)}
                        y2={kTop + plotH + kTick}
                        stroke={kGrid}
                        strokeWidth={1.5}
                    />
                    <text x={x(tick.at)} y={kHeight - 6} textAnchor="middle" fill={kText} fontSize={11} fontWeight="bold">
                        {tick.label}
                    </text>
                </g>
            ))}

            <path d={area} fill={kFill} />
            <path d={line} fill="none" stroke={kLine} strokeWidth={2} strokeLinejoin="round" />
        </svg>
    );
};
