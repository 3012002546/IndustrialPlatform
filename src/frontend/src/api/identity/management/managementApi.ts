/**
 * Identity 管理 API(§16.3/§29A.5):经网关 /identity/api/v1 前缀访问后端 Management 端点。
 * httpClient 已解包信封,页面直接拿到业务 DTO 或 PageResult。
 * 查询参数全部拼入 path(后端端点接受 Query 绑定)。
 * 用户创建/重置密码不再提交明文密码:服务端生成随机临时密码,
 * 只在 CreateUserResultDto/ResetPasswordResultDto.temporaryPassword 中出现一次。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  AssignRolePermissionsRequestDto,
  AssignUserRolesRequestDto,
  CreateRoleRequestDto,
  CreateUserGroupRequestDto,
  CreateUserRequestDto,
  CreateUserResultDto,
  DeleteUserGroupRequestDto,
  DeleteUserRequestDto,
  LoginAuditItemDto,
  PageResultDto,
  PermissionTreeNodeDto,
  ResetPasswordResultDto,
  RestoreUserGroupRequestDto,
  RestoreUserRequestDto,
  RoleSummaryDto,
  SetUserGroupMembersRequestDto,
  SetUserGroupRolesRequestDto,
  SetUserGroupStatusRequestDto,
  SetUserStatusRequestDto,
  UpdateRoleRequestDto,
  UpdateUserGroupRequestDto,
  UpdateUserRequestDto,
  UserGroupDetailDto,
  UserGroupSummaryDto,
  UserSummaryDto,
} from './types'

const IDENTITY_MANAGEMENT_PREFIX = '/identity/api/v1'

export interface ListUsersParams {
  nId?: string | undefined
  loginName?: string | undefined
  name?: string | undefined
  status?: string | undefined
  /** 按有效成员过滤(用户组 NId,§29A.5)。 */
  groupNId?: string | undefined
  /** 按直接角色过滤(角色 NId,§29A.5)。 */
  roleNId?: string | undefined
  /** 为 true 时同时返回墓碑(已删除用户,§29A.3)。 */
  includeDeleted?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
}

export interface ListRolesParams {
  nId?: string | undefined
  name?: string | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
}

export interface ListUserGroupsParams {
  name?: string | undefined
  status?: string | undefined
  /** true 时同时返回已删除墓碑(§29A.3,供恢复操作)。 */
  includeDeleted?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
}

export interface ListLoginAuditsParams {
  userNId?: string | undefined
  success?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
}

/** 查询串生成:跳过 undefined/null/空串,其余编码拼接。 */
function toQueryString(params: object): string {
  const entries = Object.entries(params)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
  return entries.length > 0 ? `?${entries.join('&')}` : ''
}

export interface IdentityManagementApi {
  // 用户
  listUsers(params: ListUsersParams): Promise<PageResultDto<UserSummaryDto>>
  getUser(userNId: string): Promise<UserSummaryDto>
  createUser(request: CreateUserRequestDto): Promise<CreateUserResultDto>
  updateUser(userNId: string, request: UpdateUserRequestDto): Promise<UserSummaryDto>
  setUserStatus(userNId: string, request: SetUserStatusRequestDto): Promise<UserSummaryDto>
  assignUserRoles(userNId: string, request: AssignUserRolesRequestDto): Promise<UserSummaryDto>
  resetPassword(userNId: string): Promise<ResetPasswordResultDto>
  deleteUser(userNId: string, request: DeleteUserRequestDto): Promise<void>
  restoreUser(userNId: string, request: RestoreUserRequestDto): Promise<UserSummaryDto>

  // 用户组(§29A.5)
  listUserGroups(params: ListUserGroupsParams): Promise<PageResultDto<UserGroupSummaryDto>>
  getUserGroup(groupNId: string): Promise<UserGroupDetailDto>
  createUserGroup(request: CreateUserGroupRequestDto): Promise<UserGroupDetailDto>
  updateUserGroup(groupNId: string, request: UpdateUserGroupRequestDto): Promise<UserGroupDetailDto>
  setUserGroupStatus(
    groupNId: string,
    request: SetUserGroupStatusRequestDto,
  ): Promise<UserGroupDetailDto>
  setUserGroupMembers(
    groupNId: string,
    request: SetUserGroupMembersRequestDto,
  ): Promise<UserGroupDetailDto>
  setUserGroupRoles(
    groupNId: string,
    request: SetUserGroupRolesRequestDto,
  ): Promise<UserGroupDetailDto>
  deleteUserGroup(groupNId: string, request: DeleteUserGroupRequestDto): Promise<void>
  restoreUserGroup(
    groupNId: string,
    request: RestoreUserGroupRequestDto,
  ): Promise<UserGroupDetailDto>

  // 角色
  listRoles(params: ListRolesParams): Promise<PageResultDto<RoleSummaryDto>>
  getRole(roleNId: string): Promise<RoleSummaryDto>
  createRole(request: CreateRoleRequestDto): Promise<RoleSummaryDto>
  updateRole(roleNId: string, request: UpdateRoleRequestDto): Promise<RoleSummaryDto>
  assignRolePermissions(
    roleNId: string,
    request: AssignRolePermissionsRequestDto,
  ): Promise<RoleSummaryDto>

  // 权限目录
  getPermissionTree(): Promise<PermissionTreeNodeDto[]>

  // 登录审计
  listLoginAudits(params: ListLoginAuditsParams): Promise<PageResultDto<LoginAuditItemDto>>
}

export function createIdentityManagementApi(client: HttpClient): IdentityManagementApi {
  const base = IDENTITY_MANAGEMENT_PREFIX

  return {
    // 用户
    listUsers: (params) =>
      client.get<PageResultDto<UserSummaryDto>>(`${base}/users${toQueryString(params)}`),
    getUser: (userNId) =>
      client.get<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}`),
    createUser: (request) => client.post<CreateUserResultDto>(`${base}/users`, request),
    updateUser: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}`, request),
    setUserStatus: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}/status`, request),
    assignUserRoles: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}/roles`, request),
    resetPassword: (userNId) =>
      client.post<ResetPasswordResultDto>(
        `${base}/users/${encodeURIComponent(userNId)}/reset-password`,
        {},
      ),
    deleteUser: (userNId, request) =>
      client.delete<void>(`${base}/users/${encodeURIComponent(userNId)}`, request),
    restoreUser: (userNId, request) =>
      client.post<UserSummaryDto>(
        `${base}/users/${encodeURIComponent(userNId)}/restore`,
        request,
      ),

    // 用户组(§29A.5)
    listUserGroups: (params) =>
      client.get<PageResultDto<UserGroupSummaryDto>>(
        `${base}/user-groups${toQueryString(params)}`,
      ),
    getUserGroup: (groupNId) =>
      client.get<UserGroupDetailDto>(`${base}/user-groups/${encodeURIComponent(groupNId)}`),
    createUserGroup: (request) =>
      client.post<UserGroupDetailDto>(`${base}/user-groups`, request),
    updateUserGroup: (groupNId, request) =>
      client.put<UserGroupDetailDto>(
        `${base}/user-groups/${encodeURIComponent(groupNId)}`,
        request,
      ),
    setUserGroupStatus: (groupNId, request) =>
      client.put<UserGroupDetailDto>(
        `${base}/user-groups/${encodeURIComponent(groupNId)}/status`,
        request,
      ),
    setUserGroupMembers: (groupNId, request) =>
      client.put<UserGroupDetailDto>(
        `${base}/user-groups/${encodeURIComponent(groupNId)}/members`,
        request,
      ),
    setUserGroupRoles: (groupNId, request) =>
      client.put<UserGroupDetailDto>(
        `${base}/user-groups/${encodeURIComponent(groupNId)}/roles`,
        request,
      ),
    deleteUserGroup: (groupNId, request) =>
      client.delete<void>(`${base}/user-groups/${encodeURIComponent(groupNId)}`, request),
    restoreUserGroup: (groupNId, request) =>
      client.post<UserGroupDetailDto>(
        `${base}/user-groups/${encodeURIComponent(groupNId)}/restore`,
        request,
      ),

    // 角色
    listRoles: (params) =>
      client.get<PageResultDto<RoleSummaryDto>>(`${base}/roles${toQueryString(params)}`),
    getRole: (roleNId) =>
      client.get<RoleSummaryDto>(`${base}/roles/${encodeURIComponent(roleNId)}`),
    createRole: (request) => client.post<RoleSummaryDto>(`${base}/roles`, request),
    updateRole: (roleNId, request) =>
      client.put<RoleSummaryDto>(`${base}/roles/${encodeURIComponent(roleNId)}`, request),
    assignRolePermissions: (roleNId, request) =>
      client.put<RoleSummaryDto>(
        `${base}/roles/${encodeURIComponent(roleNId)}/permissions`,
        request,
      ),

    // 权限目录
    getPermissionTree: () => client.get<PermissionTreeNodeDto[]>(`${base}/permissions/tree`),

    // 登录审计
    listLoginAudits: (params) =>
      client.get<PageResultDto<LoginAuditItemDto>>(`${base}/audits/logins${toQueryString(params)}`),
  }
}
