/**
 * Identity 管理 API(§16.3):经网关 /identity/api/v1 前缀访问后端 Management 端点。
 * httpClient 已解包信封,页面直接拿到业务 DTO 或 PageResult。
 * 查询参数全部拼入 path(后端端点接受 Query 绑定)。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  AssignRolePermissionsRequestDto,
  AssignUserRolesRequestDto,
  CreateRoleRequestDto,
  CreateUserRequestDto,
  LoginAuditItemDto,
  PageResultDto,
  PermissionTreeNodeDto,
  ResetPasswordRequestDto,
  RoleSummaryDto,
  SetUserStatusRequestDto,
  UpdateRoleRequestDto,
  UpdateUserRequestDto,
  UserSummaryDto,
} from './types'

const IDENTITY_MANAGEMENT_PREFIX = '/identity/api/v1'

export interface ListUsersParams {
  nId?: string | undefined
  loginName?: string | undefined
  name?: string | undefined
  status?: string | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
}

export interface ListRolesParams {
  nId?: string | undefined
  name?: string | undefined
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
  createUser(request: CreateUserRequestDto): Promise<UserSummaryDto>
  updateUser(userNId: string, request: UpdateUserRequestDto): Promise<UserSummaryDto>
  setUserStatus(userNId: string, request: SetUserStatusRequestDto): Promise<UserSummaryDto>
  assignUserRoles(userNId: string, request: AssignUserRolesRequestDto): Promise<UserSummaryDto>
  resetPassword(userNId: string, request: ResetPasswordRequestDto): Promise<void>

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
    createUser: (request) => client.post<UserSummaryDto>(`${base}/users`, request),
    updateUser: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}`, request),
    setUserStatus: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}/status`, request),
    assignUserRoles: (userNId, request) =>
      client.put<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}/roles`, request),
    resetPassword: (userNId, request) =>
      client.post<void>(`${base}/users/${encodeURIComponent(userNId)}/reset-password`, request),

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
