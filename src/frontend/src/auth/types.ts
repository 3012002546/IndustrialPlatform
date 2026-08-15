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
  /** §29A.4:普通新用户首次登录必须改密;内置 admin 为 false。 */
  mustChangePassword: boolean
}

export interface AuthSession {
  accessToken: string
  refreshToken: string
  /** ISO 8601(带 Z 或明确偏移);解析失败视为无效会话。 */
  expiresAt: string
  user: AuthUser
}

/** bootstrap 状态(§29A.5,仅非敏感状态):Pending=初始化未完成;RecoveryRequired=admin 异常需紧急恢复。 */
export interface BootstrapStatus {
  state: 'Pending' | 'Ready' | 'RecoveryRequired'
  adminExists: boolean
}

/** 认证边界唯一入口。 */
export interface AuthGateway {
  login(command: LoginCommand): Promise<AuthSession>
  refresh(refreshToken: string): Promise<AuthSession>
  logout(): Promise<void>
  getCurrentUser(): Promise<AuthUser>
  /** §29A.4:当前用户修改密码(首次登录门禁);成功后服务端撤销全部会话,前端需重新登录。 */
  changePassword(currentPassword: string, newPassword: string): Promise<void>
  /** §29A.5:读取 bootstrap 状态(登录页 HTTP 模式诊断;失败时按 Ready 降级,不阻塞登录)。 */
  getBootstrapStatus(): Promise<BootstrapStatus>
}
