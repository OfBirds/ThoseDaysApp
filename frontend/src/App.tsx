import { useState, useEffect } from 'react';
import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider, useTheme } from './context/ThemeContext';
import { getFontScale, saveFontScale } from './lib/storage';
import Calendar from './components/Calendar';
import StatusBar from './components/StatusBar';
import BloodDropIcon from './components/BloodDropIcon';
import LoginPage from './pages/LoginPage';
import StatsPage from './pages/StatsPage';
import './styles/app.css';

const FONT_MIN = 40;
const FONT_MAX = 200;
const FONT_STEP = 20;

function applyFontScale(pct: number) {
  document.documentElement.style.setProperty('--font-scale', String(pct / 100));
}

interface Cycle {
  id: string;
  startDate: string;
  durationDays: number;
  createdAt: string;
  corrected: boolean;
  auto: boolean;
}

interface Stats {
  averageCycleLength: number;
  averageInterval: number;
  totalCycles: number;
}

function localDateString(date: Date) {
  const yyyy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  const dd = String(date.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  return (
    <button
      className="theme-toggle"
      onClick={toggleTheme}
      aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
      title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
    >
      {theme === 'dark' ? '☀️' : '🌙'}
    </button>
  );
}

function FontSizeControl() {
  const [scale, setScale] = useState(getFontScale);

  useEffect(() => {
    applyFontScale(scale);
    saveFontScale(scale);
  }, [scale]);

  const adjust = (delta: number) =>
    setScale(s => Math.min(FONT_MAX, Math.max(FONT_MIN, s + delta)));

  return (
    <div className="font-control" title="Text size">
      <button onClick={() => adjust(-FONT_STEP)} disabled={scale <= FONT_MIN} aria-label="Smaller text">A−</button>
      <span className="font-control-value">{scale}%</span>
      <button onClick={() => adjust(FONT_STEP)} disabled={scale >= FONT_MAX} aria-label="Larger text">A+</button>
    </div>
  );
}

function AppContent() {
  const { user, logout } = useAuth();
  const [cycles, setCycles] = useState<Cycle[]>([]);
  const [stats, setStats] = useState<Stats | null>(null);
  const [nextPeriod, setNextPeriod] = useState<{ startIso: string; daysUntil: number } | null>(null);
  const [, setLoading] = useState(true);

  useEffect(() => {
    if (user) {
      void reconcileThenFetch();
    }
  }, [user]);

  // On load: catch up any elapsed forecasts (auto-fill), then load data.
  const reconcileThenFetch = async () => {
    try {
      await fetch(`/api/user/${user!.id}/reconcile?today=${localDateString(new Date())}`, {
        method: 'POST'
      });
    } catch (error) {
      console.error('Reconcile failed:', error);
    }
    await fetchData();
  };

  const fetchData = async () => {
    try {
      setLoading(true);
      const [cyclesRes, statsRes] = await Promise.all([
        fetch(`/api/user/${user!.id}/cycles`),
        fetch(`/api/user/${user!.id}/stats`)
      ]);

      if (cyclesRes.ok) setCycles(await cyclesRes.json());
      if (statsRes.ok) setStats(await statsRes.json());
    } catch (error) {
      console.error('Error fetching data:', error);
    } finally {
      setLoading(false);
    }
  };

  if (!user) {
    return <LoginPage onLoginSuccess={() => {}} />;
  }

  return (
    <div className="app">
      <header className="app-header">
        <FontSizeControl />
        <nav className="app-nav">
          <NavLink to="/" end className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>Calendar</NavLink>
          <NavLink to="/stats" className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>Statistics</NavLink>
        </nav>
        <div className="app-header-right">
          <span className="app-header-email">{user.email}</span>
          <ThemeToggle />
          <button className="logout-button" onClick={logout}>Logout</button>
        </div>
      </header>
      <main className="app-main">
        <Routes>
          <Route
            path="/"
            element={
              <>
                <Calendar
                  cycles={cycles}
                  onCommitted={fetchData}
                  userId={user.id}
                  onNextPeriod={setNextPeriod}
                />
                {stats && (
                  <StatusBar
                    averageCycleLength={stats.averageCycleLength}
                    averageInterval={stats.averageInterval}
                    totalCycles={stats.totalCycles}
                    nextPeriodDays={nextPeriod?.daysUntil ?? null}
                  />
                )}
              </>
            }
          />
          <Route path="/stats" element={<StatsPage />} />
        </Routes>
      </main>
      <footer className="app-footer">
        <BloodDropIcon size={16} />
        <span className="app-brand">ThoseDaysApp</span>
      </footer>
    </div>
  );
}

function App() {
  useEffect(() => {
    applyFontScale(getFontScale());
  }, []);

  return (
    <ThemeProvider>
      <AuthProvider>
        <BrowserRouter>
          <AppContent />
        </BrowserRouter>
      </AuthProvider>
    </ThemeProvider>
  );
}

export default App;
