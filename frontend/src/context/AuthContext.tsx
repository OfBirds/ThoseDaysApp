// Auth now lives in the shared @bearsoft/auth-core engine (one implementation across all apps + Angular).
// This module re-exports the React adapter so existing `../context/AuthContext` imports keep working.
// See github.com/Trifunovich/auth-core.
export { AuthProvider, useAuth, SSO_BLOCKED_KEY } from '@bearsoft/auth-core/react';
export { SSO_PENDING_KEY } from '@bearsoft/auth-core';
