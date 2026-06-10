import { useState, useEffect } from 'react';
import { AuthProvider, useAuth } from './context/AuthContext';
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
}

interface Stats {
  averageCycleLength: number;
  averageInterval: number;
  totalCycles: number;
}

function AppContent() {
  const { user } = useAuth();
  const [cycles, setCycles] = useState<Cycle[]>([]);
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (user) {
      fetchData();
    }
  }, [user]);

  const fetchData = async () => {
    try {
      setLoading(true);
      const [cyclesRes, statsRes] = await Promise.all([
        fetch(`/api/user/${user!.id}/cycles`),
        fetch(`/api/user/${user!.id}/stats`)
      ]);

      if (cyclesRes.ok) {
        const cyclesData = await cyclesRes.json();
        setCycles(cyclesData);
      }

      if (statsRes.ok) {
        const statsData = await statsRes.json();
        setStats(statsData);
      }
    } catch (error) {
      console.error('Error fetching data:', error);
    } finally {
      setLoading(false);
    }
  };

  const { logout } = useAuth();

  if (!user) {
    return <LoginPage onLoginSuccess={() => {}} />;
  }

  return (
    <div className="app">
      <header className="app-header">
        <span className="app-header-email">{user.email}</span>
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
        <Calendar cycles={cycles} onCycleAdded={fetchData} userId={user.id} />
      </main>
    </div>
  );
}

function App() {
  return (
    <AuthProvider>
      <AppContent />
    </AuthProvider>
  );
}

export default App;
