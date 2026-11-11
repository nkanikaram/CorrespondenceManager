import { useEffect, useRef, useState, forwardRef, useImperativeHandle } from 'react';
import AssistantPanel, { type AssistantPanelHandle } from './AssistantPanel';
import type { SearchContext, SearchParams, SendCorrespondenceParams, ExportParams } from '../api/kendoAiAdapter';

type Props = {
    onQuery?: (q: string) => void;
    onSearch?: (params: SearchParams) => void | Promise<void>;
    onSendCorrespondence?: (params: SendCorrespondenceParams) => Promise<string>;
    onUpdateCorrespondence?: (params: Partial<SendCorrespondenceParams>) => Promise<string>;
    onExport?: (params: ExportParams) => void;
    onClearFilters?: () => void;
    onUndo?: () => void;
    searchContext?: SearchContext | null;
    /** The real status values present in the data — lets the AI validate/suggest instead of guessing. */
    statusValues?: string[];
    activeParams?: SearchParams | null;
    activeDraft?: SendCorrespondenceParams | null;
    insights?: string[];
    /** Called whenever dock state/width changes, so the host page can reserve space alongside it. */
    onDockChange?: (docked: boolean, width: number) => void;
};

export type AssistantPopupHandle = {
    /** Opens the assistant panel and immediately runs `text` as a query — used by "Explain this record". */
    openAndAsk: (text: string) => void;
};

const DEFAULT_WIDTH = 420;
const DEFAULT_HEIGHT = 640;
const MIN_WIDTH = 320;
const MIN_HEIGHT = 360;
const EDGE_MARGIN = 24;

const DEFAULT_DOCK_WIDTH = 380;
const MIN_DOCK_WIDTH = 300;
const MAX_DOCK_WIDTH = 640;

type Handle = 'nw' | 'ne' | 'sw' | 'se' | 'w' | 'e';

type Box = { top: number; left: number; width: number; height: number };

// Which axis (if any) each handle drags along — null means that axis is left untouched.
const HANDLE_AXES: Record<Handle, { x: 'w' | 'e' | null; y: 'n' | 's' | null }> = {
    nw: { x: 'w', y: 'n' },
    ne: { x: 'e', y: 'n' },
    sw: { x: 'w', y: 's' },
    se: { x: 'e', y: 's' },
    w: { x: 'w', y: null },
    e: { x: 'e', y: null },
};

function clamp(value: number, min: number, max: number) {
    return Math.max(min, Math.min(value, max));
}

function defaultBox(): Box {
    const width = Math.min(DEFAULT_WIDTH, window.innerWidth - EDGE_MARGIN * 2);
    const height = Math.min(DEFAULT_HEIGHT, window.innerHeight - EDGE_MARGIN * 2);
    return {
        width,
        height,
        left: window.innerWidth - EDGE_MARGIN - width,
        top: window.innerHeight - 92 - height,
    };
}

const HANDLE_CURSOR: Record<Handle, string> = {
    nw: 'nwse-resize',
    se: 'nwse-resize',
    ne: 'nesw-resize',
    sw: 'nesw-resize',
    w: 'ew-resize',
    e: 'ew-resize',
};

const CORNER_SIZE = 14;
const EDGE_HANDLE_WIDTH = 10;
const EDGE_HANDLE_HEIGHT = 36;

const HANDLE_STYLE: Record<Handle, React.CSSProperties> = {
    nw: { top: -5, left: -5, width: CORNER_SIZE, height: CORNER_SIZE },
    ne: { top: -5, right: -5, width: CORNER_SIZE, height: CORNER_SIZE },
    sw: { bottom: -5, left: -5, width: CORNER_SIZE, height: CORNER_SIZE },
    se: { bottom: -5, right: -5, width: CORNER_SIZE, height: CORNER_SIZE },
    w: { top: '50%', left: -4, width: EDGE_HANDLE_WIDTH, height: EDGE_HANDLE_HEIGHT, transform: 'translateY(-50%)', borderRadius: 4 },
    e: { top: '50%', right: -4, width: EDGE_HANDLE_WIDTH, height: EDGE_HANDLE_HEIGHT, transform: 'translateY(-50%)', borderRadius: 4 },
};

const DRAG_HANDLE_HEIGHT = 22;

const AssistantPopup = forwardRef<AssistantPopupHandle, Props>(function AssistantPopup(props, ref) {
    const { onDockChange } = props;
    const [open, setOpen] = useState(false);
    const [box, setBox] = useState<Box>(() => defaultBox());
    const [resizing, setResizing] = useState(false);
    const [dragging, setDragging] = useState(false);
    const [pendingAsk, setPendingAsk] = useState<string | null>(null);
    const [docked, setDocked] = useState(false);
    const [dockWidth, setDockWidth] = useState(DEFAULT_DOCK_WIDTH);
    const panelRef = useRef<AssistantPanelHandle>(null);

    // Undocking always drops back to the default floating position/size — "back to
    // original/default" per the request, rather than remembering wherever it was last dragged.
    function toggleDock() {
        setDocked(d => {
            const next = !d;
            onDockChange?.(next, next ? dockWidth : 0);
            if (!next) setBox(defaultBox());
            return next;
        });
    }

    function startDockResize(e: React.MouseEvent) {
        e.preventDefault();
        const startX = e.clientX;
        const startWidth = dockWidth;
        setResizing(true);

        function onMove(ev: MouseEvent) {
            // Dragging left grows the panel (its right edge is pinned to the screen edge).
            const dx = startX - ev.clientX;
            const width = clamp(startWidth + dx, MIN_DOCK_WIDTH, Math.min(MAX_DOCK_WIDTH, window.innerWidth - EDGE_MARGIN));
            setDockWidth(width);
            onDockChange?.(true, width);
        }

        function onUp() {
            setResizing(false);
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
        }

        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseup', onUp);
    }

    useImperativeHandle(ref, () => ({
        openAndAsk(text: string) {
            setOpen(true);
            setPendingAsk(text);
        },
    }));

    useEffect(() => {
        if (open && pendingAsk && panelRef.current) {
            panelRef.current.ask(pendingAsk);
            setPendingAsk(null);
        }
    }, [open, pendingAsk]);

    function startMove(e: React.MouseEvent) {
        e.preventDefault();
        const start = { x: e.clientX, y: e.clientY, ...box };
        setDragging(true);

        function onMove(ev: MouseEvent) {
            const dx = ev.clientX - start.x;
            const dy = ev.clientY - start.y;
            const left = clamp(start.left + dx, 0, window.innerWidth - start.width);
            const top = clamp(start.top + dy, 0, window.innerHeight - start.height);
            setBox(b => ({ ...b, left, top }));
        }

        function onUp() {
            setDragging(false);
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
        }

        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseup', onUp);
    }

    function restorePosition() {
        setBox(defaultBox());
    }

    function startResize(handle: Handle) {
        return (e: React.MouseEvent) => {
            e.preventDefault();
            e.stopPropagation();
            const start = { x: e.clientX, y: e.clientY, ...box };
            const axes = HANDLE_AXES[handle];
            setResizing(true);

            function onMove(ev: MouseEvent) {
                const dx = ev.clientX - start.x;
                const dy = ev.clientY - start.y;
                const maxWidth = window.innerWidth - EDGE_MARGIN;
                const maxHeight = window.innerHeight - EDGE_MARGIN;

                let width = start.width;
                let left = start.left;
                if (axes.x === 'e') {
                    width = clamp(start.width + dx, MIN_WIDTH, maxWidth);
                } else if (axes.x === 'w') {
                    const rightEdge = start.left + start.width;
                    width = clamp(start.width - dx, MIN_WIDTH, maxWidth);
                    left = rightEdge - width;
                }

                let height = start.height;
                let top = start.top;
                if (axes.y === 's') {
                    height = clamp(start.height + dy, MIN_HEIGHT, maxHeight);
                } else if (axes.y === 'n') {
                    const bottomEdge = start.top + start.height;
                    height = clamp(start.height - dy, MIN_HEIGHT, maxHeight);
                    top = bottomEdge - height;
                }

                left = clamp(left, 0, window.innerWidth - width);
                top = clamp(top, 0, window.innerHeight - height);

                setBox({ top, left, width, height });
            }

            function onUp() {
                setResizing(false);
                window.removeEventListener('mousemove', onMove);
                window.removeEventListener('mouseup', onUp);
            }

            window.addEventListener('mousemove', onMove);
            window.addEventListener('mouseup', onUp);
        };
    }

    const handlesRef = useRef<Handle[]>(['nw', 'ne', 'sw', 'se', 'w', 'e']);

    function closePanel() {
        setOpen(false);
        if (docked) {
            setDocked(false);
            onDockChange?.(false, 0);
        }
    }

    return (
        <>
            {open && (
                <div
                    style={{
                        position: 'fixed',
                        top: docked ? 0 : box.top,
                        left: docked ? undefined : box.left,
                        right: docked ? 0 : undefined,
                        width: docked ? dockWidth : box.width,
                        height: docked ? '100vh' : box.height,
                        zIndex: 1050,
                        boxShadow: docked ? '-4px 0 20px rgba(0,0,0,0.35)' : '0 8px 30px rgba(0,0,0,0.45)',
                        borderRadius: docked ? 0 : 8,
                        overflow: 'hidden',
                        userSelect: resizing || dragging ? 'none' : undefined,
                        display: 'flex',
                        flexDirection: 'column',
                        transition: resizing || dragging ? undefined : 'width 0.15s ease, height 0.15s ease',
                    }}
                >
                    <div
                        onMouseDown={docked ? undefined : startMove}
                        title={docked ? undefined : 'Drag to move'}
                        style={{
                            flexShrink: 0,
                            height: DRAG_HANDLE_HEIGHT,
                            background: '#3c3c3c',
                            borderBottom: '1px solid #252526',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            padding: '0 6px',
                            cursor: docked ? 'default' : 'move',
                        }}
                    >
                        <div style={{ display: 'flex', gap: 3, paddingLeft: 4 }}>
                            {[0, 1, 2].map(i => (
                                <span key={i} style={{ width: 3, height: 3, borderRadius: '50%', background: 'rgba(255,255,255,0.3)' }} />
                            ))}
                        </div>
                        <div style={{ display: 'flex', gap: 10 }}>
                            {!docked && (
                                <button
                                    type="button"
                                    onMouseDown={e => e.stopPropagation()}
                                    onClick={restorePosition}
                                    title="Restore to default position and size"
                                    style={{ fontSize: 10, background: 'none', color: '#9cdcfe', border: 'none', padding: '2px 4px', cursor: 'pointer' }}
                                >
                                    ⟲ Restore
                                </button>
                            )}
                            <button
                                type="button"
                                onMouseDown={e => e.stopPropagation()}
                                onClick={toggleDock}
                                title={docked ? 'Undock — return to a floating, movable window' : 'Dock to the right edge of the screen'}
                                style={{ fontSize: 10, background: 'none', color: '#9cdcfe', border: 'none', padding: '2px 4px', cursor: 'pointer' }}
                            >
                                {docked ? '⇱ Undock' : '⇥ Dock right'}
                            </button>
                        </div>
                    </div>
                    <div style={{ flex: 1, minHeight: 0 }}>
                        <AssistantPanel ref={panelRef} {...props} />
                    </div>
                    {docked ? (
                        <div
                            onMouseDown={startDockResize}
                            title="Drag to resize"
                            style={{
                                position: 'absolute',
                                top: 0,
                                left: -4,
                                width: 8,
                                height: '100%',
                                cursor: 'ew-resize',
                                zIndex: 1,
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                            }}
                        >
                            <div style={{ width: 3, height: 40, borderRadius: 2, background: 'rgba(255,255,255,0.25)' }} />
                        </div>
                    ) : (
                        handlesRef.current.map(handle => {
                            const isEdge = handle === 'w' || handle === 'e';
                            return (
                                <div
                                    key={handle}
                                    onMouseDown={startResize(handle)}
                                    title="Drag to resize"
                                    style={{
                                        position: 'absolute',
                                        cursor: HANDLE_CURSOR[handle],
                                        zIndex: 1,
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        ...HANDLE_STYLE[handle],
                                    }}
                                >
                                    <div
                                        style={{
                                            width: isEdge ? 3 : 8,
                                            height: isEdge ? 20 : 8,
                                            borderRadius: isEdge ? 2 : 2,
                                            background: 'rgba(255,255,255,0.25)',
                                        }}
                                    />
                                </div>
                            );
                        })
                    )}
                </div>
            )}
            <button
                type="button"
                onClick={() => (open ? closePanel() : setOpen(true))}
                title={open ? 'Close AI Assistant' : 'Open AI Assistant'}
                aria-label={open ? 'Close AI Assistant' : 'Open AI Assistant'}
                className="ai-launcher-btn"
                style={{
                    position: 'fixed',
                    bottom: 24,
                    right: (docked && open) ? dockWidth + 24 : 24,
                    transition: 'right 0.15s ease, transform 0.15s ease, box-shadow 0.15s ease',
                    width: 48,
                    height: 48,
                    borderRadius: '50%',
                    border: '2px solid rgba(255,255,255,0.9)',
                    background: open ? '#3c3c3c' : 'transparent',
                    color: '#fff',
                    fontSize: 20,
                    boxShadow: '0 3px 10px rgba(0,0,0,0.45)',
                    cursor: 'pointer',
                    zIndex: 1051,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    overflow: 'visible',
                    padding: 0,
                }}
            >
                {open ? (
                    '×'
                ) : (
                    <span style={{ position: 'relative', width: '100%', height: '100%', display: 'block' }}>
                        {/* Water pool — clipped to the circle so ripples don't spill out */}
                        <span
                            aria-hidden
                            style={{
                                position: 'absolute',
                                inset: 0,
                                borderRadius: '50%',
                                overflow: 'hidden',
                                background: 'linear-gradient(180deg, transparent 0%, transparent 40%, #0288d1 55%, #015d94 78%, #012f4d 100%)',
                                boxShadow: 'inset 0 2px 5px rgba(255,255,255,0.35), inset 0 -4px 8px rgba(0,0,0,0.35)',
                            }}
                        >
                            {/* Slow-drifting wave layers — a floating water surface, independent of the dolphin's ripples */}
                            <span style={{ position: 'absolute', bottom: '-25%', left: '-25%', width: '150%', height: '65%', background: 'rgba(255,255,255,0.16)', borderRadius: '42%', animation: 'water-float-a 5s ease-in-out infinite' }} />
                            <span style={{ position: 'absolute', bottom: '-30%', left: '-35%', width: '170%', height: '60%', background: 'rgba(255,255,255,0.10)', borderRadius: '44%', animation: 'water-float-b 6.5s ease-in-out infinite' }} />
                            <span style={{ position: 'absolute', width: '160%', height: '160%', left: '50%', top: '50%', transform: 'translate(-50%, -50%)', borderRadius: '46%', border: '2px solid rgba(255,255,255,0.35)', animation: 'water-ripple 2.4s ease-out infinite' }} />
                            <span style={{ position: 'absolute', width: '160%', height: '160%', left: '50%', top: '50%', transform: 'translate(-50%, -50%)', borderRadius: '46%', border: '2px solid rgba(255,255,255,0.3)', animation: 'water-ripple 2.4s ease-out infinite 1.2s' }} />
                        </span>
                        {/* Dolphin + splash — siblings of the clipped pool, so the jump can escape the circle */}
                        <span
                            aria-hidden
                            style={{ position: 'absolute', bottom: 3, left: '50%', fontSize: 11, transform: 'translateX(-140%)', animation: 'dolphin-splash-left 2.4s ease-in-out infinite' }}
                        >
                            💦
                        </span>
                        <span
                            aria-hidden
                            style={{ position: 'absolute', bottom: 3, left: '50%', fontSize: 11, transform: 'translateX(40%)', animation: 'dolphin-splash-right 2.4s ease-in-out infinite' }}
                        >
                            💦
                        </span>
                        <span
                            style={{
                                position: 'absolute',
                                bottom: 4,
                                left: '50%',
                                display: 'inline-block',
                                fontSize: 20,
                                animation: 'dolphin-jump 2.4s ease-in-out infinite',
                            }}
                        >
                            🐬
                        </span>
                    </span>
                )}
            </button>
            <style>{`
                .ai-launcher-btn:hover, .ai-launcher-btn:focus-visible {
                    transform: scale(1.08);
                    box-shadow: 0 4px 16px rgba(0,0,0,0.55) !important;
                }
                @keyframes dolphin-jump {
                    0%   { transform: translateX(-50%) translateY(0) rotate(0deg); }
                    12%  { transform: translateX(-50%) translateY(-10px) rotate(-20deg); }
                    28%  { transform: translateX(-50%) translateY(-22px) rotate(-8deg); }
                    42%  { transform: translateX(-50%) translateY(-22px) rotate(10deg); }
                    58%  { transform: translateX(-50%) translateY(-6px) rotate(22deg); }
                    68%  { transform: translateX(-50%) translateY(0) rotate(8deg); }
                    76%  { transform: translateX(-50%) translateY(0) rotate(0deg); }
                    100% { transform: translateX(-50%) translateY(0) rotate(0deg); }
                }
                @keyframes water-ripple {
                    0%   { transform: translate(-50%, -50%) scale(0.6); opacity: 0.8; }
                    100% { transform: translate(-50%, -50%) scale(1.15); opacity: 0; }
                }
                @keyframes water-float-a {
                    0%, 100% { transform: translateX(0) translateY(0) rotate(0deg); }
                    50%      { transform: translateX(4%) translateY(-3px) rotate(2deg); }
                }
                @keyframes water-float-b {
                    0%, 100% { transform: translateX(0) translateY(0) rotate(0deg); }
                    50%      { transform: translateX(-5%) translateY(2px) rotate(-2deg); }
                }
                @keyframes dolphin-splash-left {
                    0%, 55%   { opacity: 0; transform: translateX(-140%) translateY(0) scale(0.5); }
                    64%       { opacity: 1; transform: translateX(-170%) translateY(-6px) scale(1); }
                    80%, 100% { opacity: 0; transform: translateX(-190%) translateY(2px) scale(1.1); }
                }
                @keyframes dolphin-splash-right {
                    0%, 55%   { opacity: 0; transform: translateX(40%) translateY(0) scale(0.5); }
                    64%       { opacity: 1; transform: translateX(70%) translateY(-6px) scale(1); }
                    80%, 100% { opacity: 0; transform: translateX(90%) translateY(2px) scale(1.1); }
                }
            `}</style>
        </>
    );
});

export default AssistantPopup;
