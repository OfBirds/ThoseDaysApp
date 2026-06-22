import { AuthScreen } from '@bearsoft/auth-core/react';
import '@bearsoft/auth-core/auth.css';
import '../styles/login.css';
import LegacyLoginForm from './LegacyLoginForm';

interface LoginPageProps {
  onLoginSuccess: () => void;
}

// The login screen is now the shared auth-core <AuthScreen> (Split-panel): brand panel + CrimsonRaven
// "Sign in" + the unverified-email verify card. The app injects only its copy/logo + palette (login.css),
// and its rich legacy email/password form as the maintenance-only `legacy` slot.
export default function LoginPage({ onLoginSuccess }: LoginPageProps) {
  return (
    <AuthScreen
      copy={{
        appName: 'Rosella Rhythm',
        tagline: 'Track and predict your cycle',
        logoUrl: '/rosella-dark.png',
      }}
      legacy={<LegacyLoginForm onSuccess={onLoginSuccess} />}
    />
  );
}
