import React, { createContext, useContext, useState, useEffect } from 'react';

export type Theme = 'dark' | 'light' | 'azure' | 'meridian' | 'nord' | 'dracula';

interface ThemeContextType {
    theme: Theme;
    setTheme: (theme: Theme) => void;
    toggleTheme: () => void;
}

const THEMES: Theme[] = ['dark', 'light', 'azure', 'meridian', 'nord', 'dracula'];

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
    const [theme, setThemeState] = useState<Theme>(() => {
        // Get theme from localStorage or default to dark
        const saved = localStorage.getItem('ecco-theme') as Theme | null;
        return (saved && THEMES.includes(saved)) ? saved : 'dark';
    });

    // Apply theme to document
    useEffect(() => {
        const root = document.documentElement;
        root.setAttribute('data-theme', theme);
        const isLight = theme === 'light';
        root.style.colorScheme = isLight ? 'light' : 'dark';
        root.setAttribute('data-bs-theme', isLight ? 'light' : 'dark');
        localStorage.setItem('ecco-theme', theme);
    }, [theme]);

    const setTheme = (newTheme: Theme) => {
        setThemeState(newTheme);
    };

    const toggleTheme = () => {
        setThemeState(prev => prev === 'dark' ? 'light' : 'dark');
    };

    return (
        <ThemeContext.Provider value={{ theme, setTheme, toggleTheme }}>
            {children}
        </ThemeContext.Provider>
    );
}

export function useTheme() {
    const context = useContext(ThemeContext);
    if (!context) {
        throw new Error('useTheme must be used within ThemeProvider');
    }
    return context;
}

