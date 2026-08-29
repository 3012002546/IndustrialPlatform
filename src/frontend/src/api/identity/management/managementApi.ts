/**
 * Identity 管理 API(§16.3/§29A.5):经网关 /identity/api/v1 前缀访问后端 Management 端点。
 * httpClient 已解包信封,页面直接拿到业务 DTO 或 PageResult。
 * 查询参数全部拼入 path(后端端点接受 Query 绑定)。
 * 用户创建/重置密码不再提交明文密码:服务端生成随机临时密码,
 * 只在 CreateUserResultDto/ResetPasswordResultDto.temporaryPassword 中出现一次。
 */

import type { HttpClient } from '@/api/httpClient'
import { serializeODataQuery, toODataQuery, type QueryDescriptor, type QueryResourceSchema } from '@/querying'

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

export const USER_QUERY_SCHEMA: QueryResourceSchema = {
  selectable: [
    'userNId',
    'loginName',
    'name',
    'email',
    'phone',
    'status',
    'tenantNId',
    'createdOn',
    'lastLoginOn',
    'mustChangePassword',
    'directRoleNIds',
    'groupRoleNIds',
    'effectiveRoleNIds',
    'effectiveRoleCount',
    'optimisticVersion',
    'concurrencyVersion',
    'isDeleted',
  ],
  filterable: ['userNId', 'loginName', 'name', 'email', 'phone', 'status', 'createdOn', 'lastLoginOn', 'mustChangePassword'],
  sortable: ['userNId', 'loginName', 'name', 'status', 'createdOn', 'lastLoginOn'],
  fieldTypes: {
    createdOn: 'date',
    lastLoginOn: 'date',
    mustChangePassword: 'boolean',
  },
  tieBreaker: 'userNId',
}

export interface ListUsersParams {
  nId?: string | undefined
  loginName?: string | undefined
  name?: string | undefined
  status?: string | undefined
  /** 按有效成员过滤(用户组 NId,§29A.5)。 */
  groupNId?: string | undefined
  /** 按直接角色过滤(角色 NId,§29A.5)。 */
  roleNId?: string | undefined
  keyword?: string | undefined
  email?: string | undefined
  phone?: string | undefined
  mustChangePassword?: boolean | undefined
  lastLoginFrom?: string | undefined
  lastLoginTo?: string | undefined
  createdFrom?: string | undefined
  createdTo?: string | undefined
  /** 为 true 时同时返回墓碑(已删除用户,§29A.3)。 */
  includeDeleted?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
}

export interface ExportUsersParams extends Omit<ListUsersParams, 'pageIndex' | 'pageSize'> {
  quantity?: number | 'all' | undefined
  columns?: string[] | undefined
}

export interface ExportRolesParams extends Omit<ListRolesParams, 'pageIndex' | 'pageSize'> {
  quantity?: number | 'all' | undefined
  columns?: string[] | undefined
}

export interface ExportUserGroupsParams extends Omit<
  ListUserGroupsParams,
  'pageIndex' | 'pageSize'
> {
  quantity?: number | 'all' | undefined
  columns?: string[] | undefined
}

export interface ExportLoginAuditsParams extends Omit<
  ListLoginAuditsParams,
  'pageIndex' | 'pageSize'
> {
  quantity?: number | 'all' | undefined
  columns?: string[] | undefined
}

export interface ListRolesParams {
  nId?: string | undefined
  name?: string | undefined
  description?: string | undefined
  isSystem?: boolean | undefined
  keyword?: string | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
}

export interface ListUserGroupsParams {
  nId?: string | undefined
  name?: string | undefined
  description?: string | undefined
  status?: string | undefined
  keyword?: string | undefined
  /** true 时同时返回已删除墓碑(§29A.3,供恢复操作)。 */
  includeDeleted?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
}

export interface ListLoginAuditsParams {
  userNId?: string | undefined
  keyword?: string | undefined
  loginNameSnapshot?: string | undefined
  failureCode?: string | undefined
  ipAddressHash?: string | undefined
  userAgentHash?: string | undefined
  traceId?: string | undefined
  occurredFrom?: string | undefined
  occurredTo?: string | undefined
  success?: boolean | undefined
  pageIndex?: number | undefined
  pageSize?: number | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
}

/** 查询串生成:跳过 undefined/null/空串,其余编码拼接。 */
function toQueryString(params: object): string {
  const entries = Object.entries(params)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(
      ([key, value]) =>
        `${encodeURIComponent(key)}=${encodeURIComponent(Array.isArray(value) ? value.join(',') : String(value))}`,
    )
  return entries.length > 0 ? `?${entries.join('&')}` : ''
}

export interface IdentityManagementApi {
  // 用户
  listUsers(params: ListUsersParams): Promise<PageResultDto<UserSummaryDto>>
  listUsersOData(descriptor: QueryDescriptor): Promise<PageResultDto<UserSummaryDto>>
  exportUsersOData?(
    descriptor: QueryDescriptor,
    columns: string[],
    quantity: number | 'all',
    culture: string,
    timeZone: string,
  ): Promise<Blob>
  exportUsers?(params: ExportUsersParams): Promise<Blob>
  exportRoles?(params: ExportRolesParams): Promise<Blob>
  exportUserGroups?(params: ExportUserGroupsParams): Promise<Blob>
  exportLoginAudits?(params: ExportLoginAuditsParams): Promise<Blob>
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
    listUsersOData: (descriptor) =>
      client.get<PageResultDto<UserSummaryDto>>(
        `${base}/odata/users?${serializeODataQuery(toODataQuery(descriptor, USER_QUERY_SCHEMA))}`,
      ),
    exportUsersOData: (descriptor, columns, quantity, culture, timeZone) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      const query = toODataQuery(descriptor, USER_QUERY_SCHEMA)
      return client.getBlob(
        `${base}/odata/users/export?${serializeODataQuery({
          ...query,
          columns: columns.join(','),
          quantity: String(quantity),
          culture,
          timeZone,
        })}`,
      )
    },
    exportUsers: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/users/export${toQueryString(params)}`)
    },
    exportRoles: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/roles/export${toQueryString(params)}`)
    },
    exportUserGroups: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/user-groups/export${toQueryString(params)}`)
    },
    exportLoginAudits: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/audits/logins/export${toQueryString(params)}`)
    },
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
      client.post<UserSummaryDto>(`${base}/users/${encodeURIComponent(userNId)}/restore`, request),

    // 用户组(§29A.5)
    listUserGroups: (params) =>
      client.get<PageResultDto<UserGroupSummaryDto>>(`${base}/user-groups${toQueryString(params)}`),
    getUserGroup: (groupNId) =>
      client.get<UserGroupDetailDto>(`${base}/user-groups/${encodeURIComponent(groupNId)}`),
    createUserGroup: (request) => client.post<UserGroupDetailDto>(`${base}/user-groups`, request),
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
