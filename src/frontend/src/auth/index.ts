/** 认证边界统一出口。 */

export {
  AUTH_SESSION_HTTP_STORAGE_KEY,
  AUTH_SESSION_STORAGE_KEY,
  AUTH_SESSION_VERSION,
  clearAuthSession,
  isSessionExpired,
  isValidAuthSession,
  parseStoredSession,
  readAuthSession,
  writeAuthSession,
  type SessionStorage,
} from './sessionStore'
export {
  MOCK_PASSWORD,
  MOCK_PERMISSIONS,
  MOCK_USER,
  MOCK_USERNAME,
  createMockAuthGateway,
  type MockAuthGatewayOptions,
} from './mockAuthGateway'
export { createHttpAuthGateway, type HttpAuthGatewayDeps } from './httpAuthGateway'
export { getAuthGateway, getCurrentSession, setAuthGateway, setCurrentSession } from './gateway'
export type { AuthGateway, AuthSession, AuthUser, LoginCommand } from './types'
