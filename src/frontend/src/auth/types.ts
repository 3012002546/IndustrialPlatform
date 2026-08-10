/**
 * 认证边界类型:页面与 Store 只接触 AuthGateway 与 AuthSession,不接触具体实现。
 * 契约见 02B §9.1 / §9.2;Phase 3 HttpAuthGateway 必须通过相同的契约测试。
 */

export interface LoginCommand {
  username: string
  password: string
}

export interface AuthUser {
  userId: string
  username: string
  displayName: string
  tenantId: string
  roles: string[]
  permissions: string[]
}

export interface AuthSession {
  accessToken: string
  refreshToken: string
  /** ISO 8601(带 Z 或明确偏移);解析失败视为无效会话。 */
  expiresAt: string
  user: AuthUser
}

/** 认证边界唯一入口。 */
export interface AuthGateway {
  login(command: LoginCommand): Promise<AuthSession>
  refresh(refreshToken: string): Promise<AuthSession>
  logout(): Promise<void>
  getCurrentUser(): Promise<AuthUser>
}
