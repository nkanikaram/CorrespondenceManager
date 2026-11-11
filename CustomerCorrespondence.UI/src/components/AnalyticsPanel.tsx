import { useState } from 'react';
import { Spinner } from 'react-bootstrap';
import {
    PieChart, Pie, Cell, BarChart, Bar, AreaChart, Area,
    XAxis, YAxis, CartesianGrid, Tooltip, Legend,
    ResponsiveContainer,
} from 'recharts';
import type { AllAggregates } from '../api/kendoAiAdapter';

// ── Colour system ─────────────────────────────────────────────────────────────
const STATUS_COLORS: Record<string, string> = {
    Completed:  '#22d3a0',
    Processing: '#f59e0b',
    Submitted:  '#60a5fa',
    Failed:     '#f87171',
    Unknown:    '#6b7280',
};
const PALETTE  = ['#60a5fa', '#a78bfa', '#34d399', '#fb923c', '#f472b6', '#facc15', '#38bdf8'];
const AGE_COLORS = ['#22d3a0', '#f59e0b', '#fb923c', '#f87171'];
const AGE_GLOW   = ['rgba(34,211,160,0.15)', 'rgba(245,158,11,0.15)', 'rgba(251,146,60,0.15)', 'rgba(248,113,113,0.15)'];

// ── Design tokens ─────────────────────────────────────────────────────────────
const BG_PANEL = '#141416';
const BG_CARD  = '#1c1c1f';
const BG_CARD2 = '#212124';
const BORDER   = '#2a2a2e';
const TEXT_PRI = '#f0f0f2';
const TEXT_SEC = '#8b8b9a';
const ACCENT   = '#3b82f6';

const TOOLTIP_STYLE: React.CSSProperties = {
    background: '#18181b',
    border: `1px solid ${BORDER}`,
    borderRadius: 8,
    color: TEXT_PRI,
    fontSize: 12,
    boxShadow: '0 8px 24px rgba(0,0,0,0.5)',
};
const TICK = { fontSize: 10, fill: TEXT_SEC };
const GRID = '#262629';

const card = (extra?: React.CSSProperties): React.CSSProperties => ({
    background: BG_CARD, borderRadius: 12, border: `1px solid ${BORDER}`,
    padding: '14px 16px', boxShadow: '0 2px 8px rgba(0,0,0,0.3)', ...extra,
});
const cardLabel = (color?: string): React.CSSProperties => ({
    fontSize: 10, fontWeight: 600, letterSpacing: 1,
    textTransform: 'uppercase', color: color ?? TEXT_SEC, marginBottom: 4,
});
const kpiAccent = (color: string): React.CSSProperties => ({
    width: 3, borderRadius: 2, background: color, alignSelf: 'stretch', flexShrink: 0,
});

// ── Granularity helpers ───────────────────────────────────────────────────────
type Granularity = 'day' | 'week' | 'month';

function aggregateVolume(data: { date: string; count: number }[], g: Granularity) {
    if (g === 'day') return data.slice(-90);
    const grouped: Record<string, number> = {};
    data.forEach(({ date, count }) => {
        const d = new Date(date);
        let key: string;
        if (g === 'week') {
            const mon = new Date(d);
            mon.setDate(d.getDate() - ((d.getDay() + 6) % 7));
            key = mon.toISOString().split('T')[0];
        } else {
            key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
        }
        grouped[key] = (grouped[key] ?? 0) + count;
    });
    return Object.entries(grouped)
        .map(([date, count]) => ({ date, count }))
        .sort((a, b) => a.date.localeCompare(b.date));
}

// ── Shared tooltip ────────────────────────────────────────────────────────────
function ChartTooltip({ active, payload, label }: any) {
    if (!active || !payload?.length) return null;
    return (
        <div style={{ ...TOOLTIP_STYLE, padding: '8px 12px', minWidth: 120 }}>
            {label && <div style={{ fontSize: 11, color: TEXT_SEC, marginBottom: 4 }}>{label}</div>}
            {payload.map((p: any, i: number) => (
                <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: TEXT_PRI, marginBottom: 2 }}>
                    <span style={{ width: 8, height: 8, borderRadius: '50%', background: p.fill ?? p.color, flexShrink: 0 }} />
                    <span style={{ color: TEXT_SEC }}>{p.name}:</span>
                    <span style={{ fontWeight: 600 }}>{Number(p.value).toLocaleString()}</span>
                </div>
            ))}
        </div>
    );
}

// ── Pie outer label ───────────────────────────────────────────────────────────
function PieOuterLabel({ cx, cy, midAngle, outerRadius, percent, name }: any) {
    if (percent < 0.05) return null;
    const RAD = Math.PI / 180;
    const r = outerRadius + 18;
    const x = cx + r * Math.cos(-midAngle * RAD);
    const y = cy + r * Math.sin(-midAngle * RAD);
    return (
        <text x={x} y={y} textAnchor={x > cx ? 'start' : 'end'}
            dominantBaseline="central" fontSize={10} fill={TEXT_SEC}>
            {name} {(percent * 100).toFixed(0)}%
        </text>
    );
}

// ── Props ─────────────────────────────────────────────────────────────────────
type Props = {
    aggregates: AllAggregates | null;
    loading: boolean;
    filterLabel?: string | null;
};

const STATUS_ORDER = ['Completed', 'Processing', 'Submitted', 'Failed'];

export default function AnalyticsPanel({ aggregates, loading, filterLabel }: Props) {
    const [gran, setGran] = useState<Granularity>('day');

    if (loading) {
        return (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10, height: 88, background: BG_PANEL, borderBottom: `1px solid ${BORDER}`, color: TEXT_SEC, fontSize: 13 }}>
                <Spinner animation="border" size="sm" style={{ borderColor: ACCENT, borderRightColor: 'transparent' }} />
                Fetching all records for analytics…
            </div>
        );
    }
    if (!aggregates) {
        return (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: 88, background: BG_PANEL, borderBottom: `1px solid ${BORDER}`, color: TEXT_SEC, fontSize: 13 }}>
                Run a search to see analytics.
            </div>
        );
    }

    const { total, statusCounts, deliveryCounts, deliveryStatusCounts, volumeByDay, processingAgeBuckets, recordsFetched } = aggregates;

    // ── KPI values ───────────────────────────────────────────────────────────
    const completed  = statusCounts['Completed']  ?? 0;
    const processing = statusCounts['Processing'] ?? 0;
    const submitted  = statusCounts['Submitted']  ?? 0;
    const failed     = statusCounts['Failed']     ?? 0;
    const pct = (n: number) => total > 0 ? ((n / total) * 100).toFixed(1) : '0.0';

    // ── Chart data prep ──────────────────────────────────────────────────────
    const statusPieData = Object.entries(statusCounts).map(([name, value]) => ({ name, value }));
    const deliveryData  = Object.entries(deliveryCounts).map(([name, value]) => ({ name, value }));
    const volumeData    = aggregateVolume(volumeByDay, gran);

    // Delivery Performance — grouped bar, one row per delivery method
    // Scale from sample counts to full totals proportionally
    const allDeliveryStatuses = Array.from(
        new Set(Object.values(deliveryStatusCounts).flatMap(m => Object.keys(m)))
    ).sort((a, b) => STATUS_ORDER.indexOf(a) - STATUS_ORDER.indexOf(b));

    const deliveryPerfData = Object.entries(deliveryStatusCounts).map(([method, counts]) => {
        const rowTotal = Object.values(counts).reduce((s, n) => s + n, 0);
        const entry: Record<string, number | string> = {
            method: method.length > 16 ? method.slice(0, 16) + '…' : method,
        };
        allDeliveryStatuses.forEach(s => {
            entry[s] = rowTotal > 0 ? Math.round(((counts[s] ?? 0) / rowTotal) * 100) : 0;
        });
        return entry;
    });

    // Processing age
    const totalProcessing = processingAgeBuckets.reduce((s, b) => s + b.count, 0);
    const ageData = processingAgeBuckets.map((b, i) => ({
        ...b,
        pct: totalProcessing > 0 ? (b.count / totalProcessing * 100) : 0,
        fill: AGE_COLORS[i],
        glow: AGE_GLOW[i],
    }));

    // ── Granularity button ───────────────────────────────────────────────────
    const GranBtn = ({ g }: { g: Granularity }) => (
        <button onClick={() => setGran(g)} style={{
            fontSize: 10, fontWeight: 600, padding: '3px 10px', borderRadius: 20,
            border: 'none',
            background: gran === g ? ACCENT : BG_CARD2,
            color: gran === g ? '#fff' : TEXT_SEC,
            cursor: 'pointer',
        }}>
            {g[0].toUpperCase() + g.slice(1)}
        </button>
    );

    return (
        <div style={{ padding: '12px 16px', borderBottom: `1px solid ${BORDER}`, background: BG_PANEL, display: 'flex', flexDirection: 'column', gap: 12 }}>

            {/* ── Row 1: KPI cards (5 columns) ──────────────────────────────── */}
            <div style={{ display: 'grid', gridTemplateColumns: '1.3fr 1fr 1fr 1fr 1fr', gap: 10 }}>

                {/* Total */}
                <div style={{ ...card(), display: 'flex', gap: 12, alignItems: 'stretch' }}>
                    <div style={kpiAccent(ACCENT)} />
                    <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={cardLabel()}>Total Records</div>
                        <div style={{ fontSize: 26, fontWeight: 700, color: TEXT_PRI, lineHeight: 1.2 }}>{total.toLocaleString()}</div>
                        {filterLabel && <div style={{ fontSize: 10, color: ACCENT, marginTop: 3, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{filterLabel}</div>}
                        <div style={{ fontSize: 10, color: TEXT_SEC, marginTop: 2 }}>{recordsFetched.toLocaleString()} records analysed</div>
                    </div>
                </div>

                {[
                    { label: 'Completed',  value: completed,  color: '#22d3a0', sub: `${completed.toLocaleString()} records` },
                    { label: 'Processing', value: processing, color: '#f59e0b', sub: `${processing.toLocaleString()} in queue` },
                    { label: 'Submitted',  value: submitted,  color: '#60a5fa', sub: `${submitted.toLocaleString()} records` },
                    { label: 'Failed',     value: failed,     color: '#f87171', sub: `${failed.toLocaleString()} records` },
                ].map(({ label, value, color, sub }) => (
                    <div key={label} style={{ ...card(), display: 'flex', gap: 12, alignItems: 'stretch' }}>
                        <div style={kpiAccent(color)} />
                        <div>
                            <div style={cardLabel(color)}>{label}</div>
                            <div style={{ fontSize: 26, fontWeight: 700, color, lineHeight: 1.2 }}>{pct(value)}%</div>
                            <div style={{ fontSize: 11, color: TEXT_SEC, marginTop: 2 }}>{sub}</div>
                        </div>
                    </div>
                ))}
            </div>

            {/* ── Row 2: Status donut | Volume area | Delivery pie ──────────── */}
            <div style={{ display: 'grid', gridTemplateColumns: '210px 1fr 210px', gap: 10 }}>

                {/* Status donut */}
                <div style={card()}>
                    <div style={cardLabel()}>Status Distribution</div>
                    <ResponsiveContainer width="100%" height={200}>
                        <PieChart>
                            <Pie data={statusPieData} cx="50%" cy="47%"
                                innerRadius={50} outerRadius={74}
                                paddingAngle={3} dataKey="value" nameKey="name" stroke="none">
                                {statusPieData.map((e, i) => (
                                    <Cell key={i} fill={STATUS_COLORS[e.name] ?? PALETTE[i % PALETTE.length]} />
                                ))}
                            </Pie>
                            <Tooltip content={<ChartTooltip />} />
                            <Legend iconType="circle" iconSize={7}
                                wrapperStyle={{ fontSize: 11, color: TEXT_PRI, paddingTop: 4 }} />
                        </PieChart>
                    </ResponsiveContainer>
                </div>

                {/* Volume area */}
                <div style={card()}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                        <div style={cardLabel()}>Volume Over Time</div>
                        <div style={{ display: 'flex', gap: 4 }}>
                            <GranBtn g="day" /><GranBtn g="week" /><GranBtn g="month" />
                        </div>
                    </div>
                    <ResponsiveContainer width="100%" height={180}>
                        <AreaChart data={volumeData} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                            <defs>
                                <linearGradient id="volGrad" x1="0" y1="0" x2="0" y2="1">
                                    <stop offset="5%"  stopColor={ACCENT} stopOpacity={0.35} />
                                    <stop offset="95%" stopColor={ACCENT} stopOpacity={0} />
                                </linearGradient>
                            </defs>
                            <CartesianGrid strokeDasharray="3 3" stroke={GRID} vertical={false} />
                            <XAxis dataKey="date" tick={TICK} interval="preserveStartEnd" axisLine={false} tickLine={false} />
                            <YAxis tick={TICK} allowDecimals={false} axisLine={false} tickLine={false} />
                            <Tooltip content={<ChartTooltip />} />
                            <Area type="monotone" dataKey="count" name="Requests"
                                stroke={ACCENT} strokeWidth={2} fill="url(#volGrad)"
                                dot={false} activeDot={{ r: 4, fill: ACCENT, stroke: BG_CARD }} />
                        </AreaChart>
                    </ResponsiveContainer>
                </div>

                {/* Delivery pie */}
                <div style={card()}>
                    <div style={cardLabel()}>Delivery Method</div>
                    <ResponsiveContainer width="100%" height={200}>
                        <PieChart>
                            <Pie data={deliveryData} cx="50%" cy="47%"
                                outerRadius={68} paddingAngle={3}
                                dataKey="value" nameKey="name"
                                stroke="none" labelLine={false} label={PieOuterLabel}>
                                {deliveryData.map((_, i) => <Cell key={i} fill={PALETTE[i % PALETTE.length]} />)}
                            </Pie>
                            <Tooltip content={<ChartTooltip />} />
                            <Legend iconType="circle" iconSize={7}
                                wrapperStyle={{ fontSize: 11, color: TEXT_PRI }} />
                        </PieChart>
                    </ResponsiveContainer>
                </div>
            </div>

            {/* ── Row 3: Delivery Performance | SLA Age ─────────────────────── */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>

                {/* Delivery Performance — 100% stacked bar (% per status per channel) */}
                <div style={card()}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 10 }}>
                        <div>
                            <div style={cardLabel()}>Delivery Channel Performance</div>
                            <div style={{ fontSize: 10, color: TEXT_SEC }}>Status breakdown % per delivery method</div>
                        </div>
                    </div>
                    <ResponsiveContainer width="100%" height={170}>
                        <BarChart data={deliveryPerfData} layout="vertical"
                            margin={{ top: 0, right: 12, left: 4, bottom: 0 }}
                            barCategoryGap="28%">
                            <CartesianGrid strokeDasharray="3 3" stroke={GRID} horizontal={false} />
                            <XAxis type="number" domain={[0, 100]} tickFormatter={v => `${v}%`}
                                tick={TICK} axisLine={false} tickLine={false} />
                            <YAxis dataKey="method" type="category"
                                tick={{ fontSize: 10, fill: TEXT_SEC }} width={80}
                                axisLine={false} tickLine={false} />
                            <Tooltip
                                content={({ active, payload, label }) => {
                                    if (!active || !payload?.length) return null;
                                    return (
                                        <div style={{ ...TOOLTIP_STYLE, padding: '8px 12px' }}>
                                            <div style={{ fontSize: 11, color: TEXT_SEC, marginBottom: 4 }}>{label}</div>
                                            {[...payload].reverse().map((p: any, i: number) => (
                                                <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: TEXT_PRI, marginBottom: 2 }}>
                                                    <span style={{ width: 8, height: 8, borderRadius: '50%', background: p.fill, flexShrink: 0 }} />
                                                    <span style={{ color: TEXT_SEC }}>{p.name}:</span>
                                                    <span style={{ fontWeight: 600 }}>{p.value}%</span>
                                                </div>
                                            ))}
                                        </div>
                                    );
                                }}
                            />
                            <Legend iconType="circle" iconSize={7}
                                wrapperStyle={{ fontSize: 11, color: TEXT_PRI, paddingTop: 6 }} />
                            {allDeliveryStatuses.map(s => (
                                <Bar key={s} dataKey={s} name={s} stackId="a"
                                    fill={STATUS_COLORS[s] ?? PALETTE[0]}
                                    radius={s === allDeliveryStatuses[allDeliveryStatuses.length - 1] ? [0, 4, 4, 0] : [0, 0, 0, 0]}
                                />
                            ))}
                        </BarChart>
                    </ResponsiveContainer>
                </div>

                {/* SLA Age — redesigned progress-bar rows */}
                <div style={card()}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 12 }}>
                        <div>
                            <div style={cardLabel('#f59e0b')}>Processing Backlog by Age</div>
                            <div style={{ fontSize: 10, color: TEXT_SEC }}>SLA risk — older = higher risk</div>
                        </div>
                        <div style={{ fontSize: 11, color: TEXT_SEC }}>{totalProcessing.toLocaleString()} total</div>
                    </div>

                    {ageData.map((b, i) => (
                        <div key={b.bucket} style={{ marginBottom: 14 }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 4 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                    <span style={{ width: 10, height: 10, borderRadius: 2, background: b.fill, display: 'inline-block', flexShrink: 0 }} />
                                    <span style={{ fontSize: 12, color: TEXT_PRI, fontWeight: 500 }}>{b.bucket}</span>
                                </div>
                                <div style={{ display: 'flex', gap: 10, alignItems: 'baseline' }}>
                                    <span style={{ fontSize: 13, fontWeight: 700, color: b.fill }}>{b.pct.toFixed(1)}%</span>
                                    <span style={{ fontSize: 11, color: TEXT_SEC }}>{b.count.toLocaleString()}</span>
                                </div>
                            </div>
                            <div style={{ height: 10, borderRadius: 5, background: BG_CARD2, overflow: 'hidden' }}>
                                <div style={{
                                    height: '100%', borderRadius: 5,
                                    width: `${b.pct}%`,
                                    background: `linear-gradient(90deg, ${b.fill}66, ${b.fill})`,
                                    boxShadow: i >= 2 ? `0 0 8px ${b.fill}80` : 'none',
                                    transition: 'width 0.4s ease',
                                }} />
                            </div>
                        </div>
                    ))}

                    <div style={{ display: 'flex', gap: 10, marginTop: 6, justifyContent: 'flex-end' }}>
                        {(['Low', 'Medium', 'High', 'Critical'] as const).map((label, i) => (
                            <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 9, color: TEXT_SEC }}>
                                <span style={{ width: 6, height: 6, borderRadius: 1, background: AGE_COLORS[i], display: 'inline-block' }} />
                                {label}
                            </div>
                        ))}
                    </div>
                </div>
            </div>
        </div>
    );
}
