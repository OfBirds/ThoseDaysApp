import { AuthScreen } from '@bearsoft/auth-core/react';
import '@bearsoft/auth-core/auth.css';
import '../styles/login.css';
import LegacyLoginForm from './LegacyLoginForm';

interface LoginPageProps {
  onLoginSuccess: () => void;
}

// In CrimsonRaven mode this just bounces to Keycloak (which hosts the themed Rosella login,
// registration, verify-email and forgot-password pages) and shows a brief "Signing you in…". The app
// only supplies its palette (login.css) and the legacy email/password form as the break-glass `legacy`
// slot, shown when AUTH_MODE=legacy.
export default function LoginPage({ onLoginSuccess }: LoginPageProps) {
  return <AuthScreen legacy={<LegacyLoginForm onSuccess={onLoginSuccess} />} />;
}
