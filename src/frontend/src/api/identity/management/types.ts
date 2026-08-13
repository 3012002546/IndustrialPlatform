/**
 * Identity 管理端点线上契约(TASK-ID-008,§16):
 * 全 camelCase;标识一律 NId;双版本(optimisticVersion/concurrencyVersion)供乐观并发回传。
 * email/phone 等可为 null;审计仅暴露 IP/UserAgent 哈希,不暴露原始值。
 */

export interface PageResultDto<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

// ---------------------------------------------------------------------------
// 用户
// ---------------------------------------------------------------------------

export interface UserSummaryDto {
  userNId: string
  loginName: string
  name: string
  email: string | null
  phone: string | null
  /** Active | Disabled(UserStatus 枚举名)。 */
  status: string
  tenantNId: string
  createdOn: string
  lastLoginOn: string | null
  roleNIds: string[]
  optimisticVersion: number
  concurrencyVersion: string
}

export interface CreateUserRequestDto {
  nId?: string | undefined
  loginName?: string | undefined
  name?: string | undefined
  initialPassword?: string | undefined
  email?: string | null | undefined
  phone?: string | null | undefined
  roleNIds?: string[] | undefined
}

export interface UpdateUserRequestDto {
  loginName?: string | undefined
  name?: string | undefined
  email?: string | null | undefined
  phone?: string | null | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface SetUserStatusRequestDto {
  enabled: boolean
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface AssignUserRolesRequestDto {
  roleNIds: string[]
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface ResetPasswordRequestDto {
  newPassword: string
}

// ---------------------------------------------------------------------------
// 角色
// ---------------------------------------------------------------------------

export interface RoleSummaryDto {
  roleNId: string
  name: string
  description: string | null
  isSystem: boolean
  tenantNId: string
  permissionNIds: string[]
  optimisticVersion: number
  concurrencyVersion: string
}

export interface CreateRoleRequestDto {
  nId?: string | undefined
  name?: string | undefined
  description?: string | undefined
  permissionNIds?: string[] | undefined
}

export interface UpdateRoleRequestDto {
  name?: string | undefined
  description?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface AssignRolePermissionsRequestDto {
  permissionNIds: string[]
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

// ---------------------------------------------------------------------------
// 权限目录
// ---------------------------------------------------------------------------

export interface PermissionTreeNodeDto {
  permissionNId: string
  name: string
  /** PermissionType 枚举名(如 Page/Action)。 */
  type: string
  parentPermissionNId: string | null
  description: string | null
  status: string
  children: PermissionTreeNodeDto[]
}

// ---------------------------------------------------------------------------
// 登录审计
// ---------------------------------------------------------------------------

export interface LoginAuditItemDto {
  tenantNId: string
  userNId: string | null
  loginNameSnapshot: string
  success: boolean
  failureCode: string | null
  /** 仅哈希摘要,不暴露原始 IP。 */
  ipAddressHash: string
  userAgentHash: string
  traceId: string
  occurredOn: string
}
