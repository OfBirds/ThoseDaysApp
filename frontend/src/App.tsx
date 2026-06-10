import { useState, useEffect } from 'react';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider, useTheme } from './context/ThemeContext';
import Calendar from './components/Calendar';
import StatusBar from './components/StatusBar';
import LoginPage from './pages/LoginPage';
import './styles/app.css';

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

function AppContent() {
  const { user, logout } = useAuth();
  const [cycles, setCycles] = useState<Cycle[]>([]);
  const [stats, setStats] = useState<Stats | null>(null);
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
        <span className="app-header-email">{user.email}</span>
        <ThemeToggle />
        <button className="logout-button" onClick={logout}>Logout</button>
      </header>
      {stats && (
        <StatusBar
          averageCycleLength={stats.averageCycleLength}
          averageInterval={stats.averageInterval}
          totalCycles={stats.totalCycles}
        />
      )}
      <main className="app-main">
        <Calendar cycles={cycles} onCommitted={fetchData} userId={user.id} />
      </main>
    </div>
  );
}

function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <AppContent />
      </AuthProvider>
    </ThemeProvider>
  );
}

export default App;
