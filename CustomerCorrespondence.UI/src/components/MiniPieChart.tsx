// A small dependency-free pie/donut chart for rendering a count breakdown (status / customer /
// delivery) inline in the AI Assistant's Conversation tab — the round-shape sibling of MiniBarChart.

import { CHART_CATEGORY_COLORS } from './chartColors';

type Props = {
    title: string;
    counts: Record<string, number>;
    variant: 'pie' | 'donut';
    maxSlices?: number;
};

export default function MiniPieChart({ title, counts, variant, maxSlices = 8 }: Props) {
    const sorted = Object.entries(counts).sort((a, b) => b[1] - a[1]);
    const entries = sorted.slice(0, maxSlices);
    const overflow = sorted.slice(maxSlices).reduce((sum, [, n]) => sum + n, 0);
    if (overflow > 0) entries.push(['Other', overflow]);

    if (entries.length === 0) {
        return <div style={{ fontSize: 12, color: '#858585' }}>No data to chart.</div>;
    }

    const total = entries.reduce((sum, [, n]) => sum + n, 0) || 1;
    const size = 120;
    const r = size / 2;
    const innerR = variant === 'donut' ? r * 0.6 : 0;

    // Walk the circle clockwise from 12 o'clock, one wedge per entry.
    let angle = -90;
    const wedges = entries.map(([label, count], i) => {
        const fraction = count / total;
        const startAngle = angle;
        const endAngle = angle + fraction * 360;
        angle = endAngle;
        return { label, count, color: CHART_CATEGORY_COLORS[i % CHART_CATEGORY_COLORS.length], startAngle, endAngle };
    });

    function arcPoint(deg: number, radius: number) {
        const rad = (deg * Math.PI) / 180;
        return [r + radius * Math.cos(rad), r + radius * Math.sin(rad)];
    }

    function wedgePath(startAngle: number, endAngle: number) {
        const largeArc = endAngle - startAngle > 180 ? 1 : 0;
        const [x1, y1] = arcPoint(startAngle, r);
        const [x2, y2] = arcPoint(endAngle, r);
        if (innerR === 0) {
            return `M ${r} ${r} L ${x1} ${y1} A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2} Z`;
        }
        const [ix2, iy2] = arcPoint(endAngle, innerR);
        const [ix1, iy1] = arcPoint(startAngle, innerR);
        return `M ${x1} ${y1} A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2} L ${ix2} ${iy2} A ${innerR} ${innerR} 0 ${largeArc} 0 ${ix1} ${iy1} Z`;
    }

    return (
        <div style={{ border: '1px solid #3c3c3c', borderRadius: 4, padding: 10, marginBottom: 8 }}>
            <div style={{ fontSize: 11, color: '#858585', marginBottom: 8, fontWeight: 600 }}>{title}</div>
            <div style={{ display: 'flex', gap: 14, alignItems: 'center', flexWrap: 'wrap' }}>
                <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ flexShrink: 0 }}>
                    {wedges.map(w => (
                        <path key={w.label} d={wedgePath(w.startAngle, w.endAngle)} fill={w.color} stroke="#1e1e1e" strokeWidth={1.5}>
                            <title>{`${w.label}: ${w.count}`}</title>
                        </path>
                    ))}
                </svg>
                <div style={{ flex: 1, minWidth: 110 }}>
                    {wedges.map(w => (
                        <div key={w.label} style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
                            <span style={{ width: 9, height: 9, borderRadius: 2, background: w.color, flexShrink: 0 }} />
                            <span
                                style={{ fontSize: 11, color: '#d4d4d4', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}
                                title={w.label}
                            >
                                {w.label}
                            </span>
                            <span style={{ fontSize: 11, color: '#9cdcfe', marginLeft: 'auto' }}>{w.count}</span>
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
}
