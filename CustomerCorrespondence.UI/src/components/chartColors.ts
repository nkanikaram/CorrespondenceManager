// Shared fixed categorical palette for the AI Assistant's mini charts (bar/pie/donut) — never
// cycled per-render, so a given rank (both charts sort entries the same way, by count descending)
// gets the same color whether it's drawn as a bar or a slice.
export const CHART_CATEGORY_COLORS = [
    '#4aa3f0', '#4ec994', '#e8c15a', '#c586c0', '#f4886b', '#6a9fd8', '#78c9c0', '#d4a373',
];
