/**
 * Identity 管理端点线上契约(TASK-ID-008/§16,§29A.5):
 * 全 camelCase;标识一律 NId;双版本(optimisticVersion/concurrencyVersion)供乐观并发回传。
 * email/phone 等可为 null;审计仅暴露 IP/UserAgent 哈希,不暴露原始值。
 * 临时密码只在创建/重置响应的 temporaryPassword 中出现一次,禁止持久化。
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
  /** 首次登录是否强制改密(管理员重置或服务端随机临时密码创建后为 true)。 */
  mustChangePassword: boolean
  /** 直接分配的角色 NId 集(原 roleNIds 更名,§29A.5)。 */
  directRoleNIds: string[]
  /** 经有效用户组继承的角色 NId 集。 */
  groupRoleNIds: string[]
  /** 直接 ∪ 组继承的有效角色并集。 */
  effectiveRoleNIds: string[]
  optimisticVersion: number
  concurrencyVersion: string
  /** 墓碑标识(includeDeleted 列表用于恢复操作,§29A.3)。 */
  isDeleted: boolean
}

/** 创建用户请求(§29A.4):不再接受明文初始密码,服务端生成随机临时密码。 */
export interface CreateUserRequestDto {
  nId?: string | undefined
  loginName?: string | undefined
  name?: string | undefined
  email?: string | null | undefined
  phone?: string | null | undefined
  roleNIds?: string[] | undefined
}

/** 创建用户结果(§29A.4):临时密码只在本次响应出现一次,禁止持久化/日志。 */
export interface CreateUserResultDto {
  user: UserSummaryDto
  temporaryPassword: string
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

/** 安全删除用户请求(§29A.3):墓碑删除,UserNId/登录标识永久保留不复用。 */
export interface DeleteUserRequestDto {
  reason?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 恢复用户墓碑请求(§29A.3):仅恢复为 Disabled,不自动恢复授权/凭据/会话。 */
export interface RestoreUserRequestDto {
  reason?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 管理员重置密码结果(§29A.4):临时密码只出现一次;重置后强制改密并撤销全部会话。 */
export interface ResetPasswordResultDto {
  temporaryPassword: string
}

// ---------------------------------------------------------------------------
// 用户组(§29A.5)
// ---------------------------------------------------------------------------

/** 用户组列表项:成员数与角色数,双版本供管理回传;isDeleted 标识墓碑(includeDeleted 列表用于恢复)。 */
export interface UserGroupSummaryDto {
  groupNId: string
  name: string
  description: string | null
  /** Active | Disabled(UserGroupStatus 枚举名)。 */
  status: string
  memberCount: number
  roleCount: number
  optimisticVersion: number
  concurrencyVersion: string
  isDeleted: boolean
}

/** 用户组详情:含成员与角色 NId 全量。 */
export interface UserGroupDetailDto {
  groupNId: string
  name: string
  description: string | null
  status: string
  tenantNId: string
  memberUserNIds: string[]
  roleNIds: string[]
  optimisticVersion: number
  concurrencyVersion: string
}

/** 创建用户组请求:成员与角色可原子提交;NId 为空时服务端生成。 */
export interface CreateUserGroupRequestDto {
  nId?: string | undefined
  name?: string | undefined
  description?: string | undefined
  memberUserNIds?: string[] | undefined
  roleNIds?: string[] | undefined
}

/** 更新用户组资料请求:NId 创建后不可修改。 */
export interface UpdateUserGroupRequestDto {
  name?: string | undefined
  description?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 启用/禁用用户组请求:禁用时失效全部成员授权。 */
export interface SetUserGroupStatusRequestDto {
  enabled: boolean
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 设置用户组成员请求:memberUserNIds 为最终成员集(幂等收敛)。 */
export interface SetUserGroupMembersRequestDto {
  memberUserNIds: string[]
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 设置用户组角色请求:roleNIds 为最终角色集(幂等收敛,不接受权限)。 */
export interface SetUserGroupRolesRequestDto {
  roleNIds: string[]
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 安全删除用户组请求(§29A.5):软删并解除有效成员/组角色关系。 */
export interface DeleteUserGroupRequestDto {
  reason?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 恢复用户组墓碑请求(§29A.5):仅恢复为 Disabled,不自动恢复关系。 */
export interface RestoreUserGroupRequestDto {
  reason?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
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
