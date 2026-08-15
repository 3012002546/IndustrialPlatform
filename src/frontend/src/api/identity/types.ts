/**
 * Identity 认证端点线上契约(与后端 Contracts 对齐,§15):
 * 全部 camelCase;标识一律为 NId 字符串,不含数据库 Id。
 * 前端业务类型(AuthUser/AuthSession)由 mapper.ts 转换,本文件只表达线上形状。
 */

/** 当前用户线上 DTO(§15.2 AuthUser)。 */
export interface IdentityAuthUserDto {
  userNId: string
  loginName: string
  name: string
  tenantNId: string
  roleNIds: string[]
  permissionNIds: string[]
  /** §29A.4:普通新用户首次登录必须改密。 */
  mustChangePassword: boolean
}

/** 登录/刷新会话线上 DTO(§15.2 AuthSession)。 */
export interface IdentityAuthSessionDto {
  accessToken: string
  refreshToken: string
  /** ISO 8601(带 Z 或明确偏移)。 */
  expiresAt: string
  user: IdentityAuthUserDto
}

/** 登录请求(§15.1)。 */
export interface IdentityLoginRequest {
  loginName: string
  password: string
}

/** 刷新请求(§15.3)。 */
export interface IdentityRefreshRequest {
  refreshToken: string
}

/** 单会话注销请求(§15.4)。 */
export interface IdentityLogoutRequest {
  refreshToken: string
}

/** 修改密码请求(§29A.4 首次登录改密;成功后撤销全部会话,前端需重新登录)。 */
export interface IdentityChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

/** bootstrap 状态线上 DTO(§29A.5,仅状态/版本,不含 Secret)。 */
export interface IdentityBootstrapStatusDto {
  state: 'Pending' | 'Ready' | 'RecoveryRequired'
  schemaVersion: string
  adminExists: boolean
  mustChangePassword: boolean
  credentialDelivered: boolean
}
