// The OIDC runtime config + UserManager engine now lives in @bearsoft/auth-core; re-exported here so
// `../lib/oidc` imports (e.g. the logo hook) keep working. See github.com/Trifunovich/auth-core.
export { loadRuntimeConfig, getUserManager } from '@bearsoft/auth-core';
export type { RuntimeConfig } from '@bearsoft/auth-core';
