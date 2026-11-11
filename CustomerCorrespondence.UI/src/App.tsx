import SearchPage from './pages/SearchPage';
import { ThemeProvider } from './context/ThemeContext';

export default function App() {
  return (
    <ThemeProvider>
      <SearchPage />
    </ThemeProvider>
  );
}