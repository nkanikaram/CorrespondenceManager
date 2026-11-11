import { useState } from 'react';

// Renders an AI answer as: a short, clean headline up front, with the full detail
// (including any markdown tables) tucked behind a "Show details" toggle. Also strips
// raw markdown punctuation (**bold**, ## headings) instead of showing it literally.

type Segment =
    | { kind: 'text'; content: string }
    | { kind: 'table'; headers: string[]; rows: string[][] };

const TABLE_ROW = /^\s*\|.*\|\s*$/;
const SEPARATOR_ROW = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;
const SUMMARY_LIMIT = 160;

function splitCells(line: string): string[] {
    const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '');
    return trimmed.split('|').map(c => c.trim());
}

// Strips markdown punctuation that would otherwise show up literally as "**" or "##" —
// bold/italic markers and heading hashes — leaving just the plain wording.
function stripMarkdown(line: string): string {
    return line
        .replace(/^#{1,6}\s*/, '')
        .replace(/\*\*(.*?)\*\*/g, '$1')
        .replace(/__(.*?)__/g, '$1')
        .replace(/(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)/g, '$1')
        .replace(/^[-*]\s+/, '• ')
        .trim();
}

function parseSegments(text: string): Segment[] {
    const lines = text.split('\n');
    const segments: Segment[] = [];
    let textBuf: string[] = [];

    const flushText = () => {
        if (textBuf.length > 0) {
            segments.push({ kind: 'text', content: textBuf.map(stripMarkdown).join('\n').trim() });
            textBuf = [];
        }
    };

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const next = lines[i + 1];
        if (TABLE_ROW.test(line) && next !== undefined && SEPARATOR_ROW.test(next)) {
            flushText();
            const headers = splitCells(line).map(stripMarkdown);
            let j = i + 2;
            const rows: string[][] = [];
            while (j < lines.length && TABLE_ROW.test(lines[j])) {
                rows.push(splitCells(lines[j]).map(stripMarkdown));
                j++;
            }
            segments.push({ kind: 'table', headers, rows });
            i = j - 1;
        } else {
            textBuf.push(line);
        }
    }
    flushText();
    return segments;
}

function truncate(s: string): string {
    return s.length > SUMMARY_LIMIT ? s.slice(0, SUMMARY_LIMIT).trim() + '…' : s;
}

// A short, single-line summary for the collapsed view. Headings like "## Summary" or
// "**Explanation**" carry no actual information, so they're skipped in favor of the first
// real sentence/bullet — falling back to the heading text itself only if nothing else exists.
function computeHeadline(text: string, segments: Segment[]): string {
    const rawLines = text.split('\n').map(l => l.trim()).filter(Boolean);
    const headingLines: string[] = [];
    for (const line of rawLines) {
        if (TABLE_ROW.test(line) || SEPARATOR_ROW.test(line)) continue;
        if (/^#{1,6}\s/.test(line)) {
            headingLines.push(line);
            continue;
        }
        const cleaned = stripMarkdown(line);
        if (cleaned) return truncate(cleaned);
    }
    for (const line of headingLines) {
        const cleaned = stripMarkdown(line);
        if (cleaned) return truncate(cleaned);
    }
    const firstTable = segments.find((s): s is Extract<Segment, { kind: 'table' }> => s.kind === 'table');
    if (firstTable) return `Table with ${firstTable.rows.length} row${firstTable.rows.length === 1 ? '' : 's'}`;
    return '';
}

function TableBlock({ headers, rows }: { headers: string[]; rows: string[][] }) {
    return (
        <div style={{ overflowX: 'auto', marginBottom: 8, borderRadius: 4, border: '1px solid #3c3c3c' }}>
            <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 12 }}>
                <thead>
                    <tr style={{ background: '#2d2d2d' }}>
                        {headers.map((h, hi) => (
                            <th
                                key={hi}
                                style={{
                                    textAlign: 'left',
                                    padding: '6px 10px',
                                    color: '#9cdcfe',
                                    fontWeight: 600,
                                    borderBottom: '1px solid #3c3c3c',
                                    whiteSpace: 'nowrap',
                                }}
                            >
                                {h}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row, ri) => (
                        <tr key={ri} style={{ background: ri % 2 === 0 ? 'transparent' : 'rgba(255,255,255,0.03)' }}>
                            {row.map((cell, ci) => (
                                <td
                                    key={ci}
                                    style={{
                                        padding: '5px 10px',
                                        color: '#d4d4d4',
                                        borderBottom: '1px solid #2d2d2d',
                                        whiteSpace: 'nowrap',
                                    }}
                                >
                                    {cell}
                                </td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

export default function MarkdownAnswer({ text }: { text: string }) {
    const [expanded, setExpanded] = useState(false);
    const segments = parseSegments(text);
    const hasTable = segments.some(s => s.kind === 'table');
    const fullPlainLength = segments.reduce((n, s) => n + (s.kind === 'text' ? s.content.length : 0), 0);
    const headline = computeHeadline(text, segments);
    const isMultiline = segments.some(s => s.kind === 'text' && s.content.includes('\n'));
    const needsDrillDown = hasTable || isMultiline || fullPlainLength > SUMMARY_LIMIT;

    // Short, single-paragraph answers: just show them, no collapse UI needed.
    if (!needsDrillDown) {
        return <span style={{ whiteSpace: 'pre-wrap' }}>{headline}</span>;
    }

    if (!expanded) {
        return (
            <div>
                <span style={{ whiteSpace: 'pre-wrap' }}>{headline}</span>{' '}
                <button
                    type="button"
                    onClick={() => setExpanded(true)}
                    style={{ fontSize: 11, color: '#4aa3f0', background: 'none', border: 'none', padding: 0, cursor: 'pointer', textDecoration: 'underline' }}
                >
                    Show details
                </button>
            </div>
        );
    }

    return (
        <div>
            {segments.map((seg, i) => {
                if (seg.kind === 'text') {
                    if (!seg.content) return null;
                    return (
                        <div key={i} style={{ whiteSpace: 'pre-wrap', marginBottom: 6 }}>
                            {seg.content}
                        </div>
                    );
                }
                return <TableBlock key={i} headers={seg.headers} rows={seg.rows} />;
            })}
            <button
                type="button"
                onClick={() => setExpanded(false)}
                style={{ fontSize: 11, color: '#858585', background: 'none', border: 'none', padding: 0, cursor: 'pointer', textDecoration: 'underline' }}
            >
                Show less
            </button>
        </div>
    );
}
