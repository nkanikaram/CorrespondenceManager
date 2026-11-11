// Small multiples: one pie/donut per outer-key group (e.g. one per delivery method), each showing
// that group's inner breakdown (e.g. status) — the way to honor "pie chart" for a two-dimensional
// cross-tab, since a single pie can't hold two values per slice but several small ones can.

import { CHART_CATEGORY_COLORS } from './chartColors';

type Props = {
    title: string;
    /** outer key (e.g. delivery method) → inner key (e.g. status) → count */
    crossTab: Record<string, Record<string, number>>;
    variant: 'pie' | 'donut';
    maxGroups?: number;
};

export default function MiniSmallMultiples({ title, crossTab, variant, maxGroups = 6 }: Props) {
    const groups = Object.entries(crossTab)
        .map(([label, inner]) => ({ label, inner, total: Object.values(inner).reduce((a, b) => a + b, 0) }))
        .sort((a, b) => b.total - a.total)
        .slice(0, maxGroups);

    if (groups.length === 0) {
        return <div style={{ fontSize: 12, color: '#858585' }}>No data to chart.</div>;
    }

    // One fixed segment order (by combined volume across every group) shared by every mini chart
    // and the single legend — otherwise the same status could land on a different color per group.
    const segmentTotals: Record<string, number> = {};
    groups.forEach(g => Object.entries(g.inner).forEach(([k, n]) => { segmentTotals[k] = (segmentTotals[k] ?? 0) + n; }));
    const segments = Object.keys(segmentTotals).sort((a, b) => segmentTotals[b] - segmentTotals[a]);
    const segmentColor = (seg: string) => CHART_CATEGORY_COLORS[segments.indexOf(seg) % CHART_CATEGORY_COLORS.length];

    const size = 84;
    const r = size / 2;
    const innerR = variant === 'donut' ? r * 0.58 : 0;

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

            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 10 }}>
                {segments.map(seg => (
                    <div key={seg} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                        <span style={{ width: 9, height: 9, borderRadius: 2, background: segmentColor(seg), flexShrink: 0 }} />
                        <span style={{ fontSize: 10, color: '#d4d4d4' }}>{seg}</span>
                    </div>
                ))}
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16 }}>
                {groups.map(g => {
                    const total = g.total || 1;
                    let angle = -90;
                    const wedges = Object.entries(g.inner)
                        .sort((a, b) => segments.indexOf(a[0]) - segments.indexOf(b[0]))
                        .map(([seg, count]) => {
                            const startAngle = angle;
                            const endAngle = angle + (count / total) * 360;
                            angle = endAngle;
                            return { seg, count, startAngle, endAngle };
                        });
                    return (
                        <div key={g.label} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: size }}>
                            <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
                                {wedges.map(w => (
                                    <path key={w.seg} d={wedgePath(w.startAngle, w.endAngle)} fill={segmentColor(w.seg)} stroke="#1e1e1e" strokeWidth={1}>
                                        <title>{`${w.seg}: ${w.count}`}</title>
                                    </path>
                                ))}
                            </svg>
                            <div
                                style={{ fontSize: 10.5, color: '#d4d4d4', marginTop: 4, textAlign: 'center', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: size }}
                                title={g.label}
                            >
                                {g.label}
                            </div>
                            <div style={{ fontSize: 10, color: '#9cdcfe' }}>{g.total}</div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
