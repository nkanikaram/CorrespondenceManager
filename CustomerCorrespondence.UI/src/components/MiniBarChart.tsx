// A small dependency-free horizontal bar chart for rendering a count breakdown
// (status / customer / delivery) inline in the AI Assistant's Conversation tab.

import { CHART_CATEGORY_COLORS } from './chartColors';

type Props = {
    title: string;
    counts: Record<string, number>;
    maxBars?: number;
};

export default function MiniBarChart({ title, counts, maxBars = 8 }: Props) {
    const entries = Object.entries(counts)
        .sort((a, b) => b[1] - a[1])
        .slice(0, maxBars);

    if (entries.length === 0) {
        return <div style={{ fontSize: 12, color: '#858585' }}>No data to chart.</div>;
    }

    const max = Math.max(...entries.map(([, n]) => n), 1);

    return (
        <div style={{ border: '1px solid #3c3c3c', borderRadius: 4, padding: 10, marginBottom: 8 }}>
            <div style={{ fontSize: 11, color: '#858585', marginBottom: 8, fontWeight: 600 }}>{title}</div>
            {entries.map(([label, count], i) => (
                <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 5 }}>
                    <div
                        style={{
                            width: 96,
                            flexShrink: 0,
                            fontSize: 11,
                            color: '#d4d4d4',
                            whiteSpace: 'nowrap',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                        }}
                        title={label}
                    >
                        {label}
                    </div>
                    <div style={{ flex: 1, background: '#2d2d2d', borderRadius: 3, overflow: 'hidden', height: 14 }}>
                        <div
                            style={{
                                width: `${Math.max(2, (count / max) * 100)}%`,
                                height: '100%',
                                background: CHART_CATEGORY_COLORS[i % CHART_CATEGORY_COLORS.length],
                                borderRadius: 3,
                            }}
                        />
                    </div>
                    <div style={{ width: 36, flexShrink: 0, fontSize: 11, color: '#9cdcfe', textAlign: 'right' }}>{count}</div>
                </div>
            ))}
        </div>
    );
}
