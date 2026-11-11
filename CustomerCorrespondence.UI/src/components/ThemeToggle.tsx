import { useTheme, type Theme } from '../context/ThemeContext';
import { Dropdown } from 'react-bootstrap';

const THEME_LABELS: Record<Theme, { label: string; emoji: string }> = {
    dark: { label: 'Dark', emoji: '🌙' },
    light: { label: 'Light', emoji: '☀️' },
    azure: { label: 'Azure', emoji: '🔵' },
    meridian: { label: 'Meridian', emoji: '🌊' },
    nord: { label: 'Nord', emoji: '❄️' },
    dracula: { label: 'Dracula', emoji: '🧛' },
};

const THEME_LIST: Theme[] = ['dark', 'light', 'azure', 'meridian', 'nord', 'dracula'];

export default function ThemeToggle() {
    const { theme, setTheme } = useTheme();
    const current = THEME_LABELS[theme];

    return (
        <Dropdown>
            <Dropdown.Toggle
                variant="link"
                id="theme-toggle"
                style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--vsc-text)',
                    cursor: 'pointer',
                    padding: '4px 8px',
                    textDecoration: 'none',
                    fontSize: '16px',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                }}
                title="Switch theme"
            >
                <span>{current.emoji}</span>
                <span style={{ fontSize: '12px' }}>{current.label}</span>
            </Dropdown.Toggle>
            <Dropdown.Menu
                style={{
                    backgroundColor: 'var(--vsc-dropdown)',
                    borderColor: 'var(--vsc-border)',
                    borderRadius: '4px',
                }}
                align="end"
            >
                {THEME_LIST.map(t => (
                    <Dropdown.Item
                        key={t}
                        onClick={() => setTheme(t)}
                        active={theme === t}
                        style={{
                            backgroundColor: theme === t ? 'var(--vsc-selection)' : 'transparent',
                            color: 'var(--vsc-text)',
                            cursor: 'pointer',
                            fontSize: '13px',
                        }}
                    >
                        <span style={{ marginRight: '8px' }}>{THEME_LABELS[t].emoji}</span>
                        {THEME_LABELS[t].label}
                    </Dropdown.Item>
                ))}
            </Dropdown.Menu>
        </Dropdown>
    );
}
