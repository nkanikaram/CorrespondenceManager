// A small dependency-free stacked bar chart for a two-dimensional breakdown (e.g. delivery method
// by status) inline in the AI Assistant's Conversation tab — the cross-tab sibling of MiniBarChart.

import { CHART_CATEGORY_COLORS } from './chartColors';

type Props = {
    title: string;
    /** outer key → inner key → count, e.g. { Mail: { Completed: 10, Failed: 2 } } */
    crossTab: Record<string, Record<string, number>>;
    maxRows?: number;
};

export default function MiniStackedBarChart({ title, crossTab, maxRows = 8 }: Props) {
    const rows = Object.entries(crossTab)
        .map(([label, inner]) => ({ label, inner, total: Object.values(inner).reduce((a, b) => a + b, 0) }))
        .sort((a, b) => b.total - a.total)
        .slice(0, maxRows);

    if (rows.length === 0) {
        return <div style={{ fontSize: 12, color: '#858585' }}>No data to chart.</div>;
    }

    // Fixed segment order across all rows, by overall volume, so a given status keeps the same
    // color in every bar instead of being colored relative to just that row's own values.
    const segmentTotals: Record<string, number> = {};
    rows.forEach(r => Object.entries(r.inner).forEach(([k, n]) => { segmentTotals[k] = (segmentTotals[k] ?? 0) + n; }));
    const segments = Object.keys(segmentTotals).sort((a, b) => segmentTotals[b] - segmentTotals[a]);
    const segmentColor = (seg: string) => CHART_CATEGORY_COLORS[segments.indexOf(seg) % CHART_CATEGORY_COLORS.length];

    const max = Math.max(...rows.map(r => r.total), 1);

    return (
        <div style={{ border: '1px solid #3c3c3c', borderRadius: 4, padding: 10, marginBottom: 8 }}>
            <div style={{ fontSize: 11, color: '#858585', marginBottom: 8, fontWeight: 600 }}>{title}</div>

            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginBottom: 8 }}>
                {segments.map(seg => (
                    <div key={seg} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
                        <span style={{ width: 9, height: 9, borderRadius: 2, background: segmentColor(seg), flexShrink: 0 }} />
                        <span style={{ fontSize: 10, color: '#d4d4d4' }}>{seg}</span>
                    </div>
                ))}
            </div>

            {rows.map(r => (
                <div key={r.label} style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 5 }}>
                    <div
                        style={{ width: 96, flexShrink: 0, fontSize: 11, color: '#d4d4d4', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}
                        title={r.label}
                    >
                        {r.label}
                    </div>
                    <div style={{ flex: 1, display: 'flex', background: '#2d2d2d', borderRadius: 3, overflow: 'hidden', height: 14 }}>
                        {segments.filter(s => r.inner[s]).map(seg => (
                            <div
                                key={seg}
                                title={`${seg}: ${r.inner[seg]}`}
                                style={{ width: `${Math.max(2, (r.inner[seg] / max) * 100)}%`, height: '100%', background: segmentColor(seg) }}
                            />
                        ))}
                    </div>
                    <div style={{ width: 36, flexShrink: 0, fontSize: 11, color: '#9cdcfe', textAlign: 'right' }}>{r.total}</div>
                </div>
            ))}
        </div>
    );
}
