/**
 * Identity 管理 API 模块映射测试(TASK-ID-021,§29A.5):
 * - 用户创建不再提交 initialPassword,解包 CreateUserResultDto(temporaryPassword 只出现一次);
 * - 重置密码提交空对象,解包 ResetPasswordResultDto;
 * - 用户组 list/create/members/roles/delete/restore 的路径、载荷与响应形状;
 * - 用户列表携带 groupNId/roleNId/includeDeleted 过滤。
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { HttpClient } from '@/api/httpClient'
import {
  createIdentityManagementApi,
  type IdentityManagementApi,
} from '@/api/identity/management'
import type {
  CreateUserResultDto,
  PageResultDto,
  ResetPasswordResultDto,
  UserGroupDetailDto,
  UserGroupSummaryDto,
  UserSummaryDto,
} from '@/api/identity/management'

function fakeClient(): HttpClient {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  } as unknown as HttpClient
}

function makeUserSummary(): UserSummaryDto {
  return {
    userNId: 'u1',
    loginName: 'alice',
    name: 'Alice',
    email: null,
    phone: null,
    status: 'Active',
    tenantNId: 't1',
    createdOn: '2026-01-01T00:00:00Z',
    lastLoginOn: null,
    mustChangePassword: true,
    directRoleNIds: ['r1'],
    groupRoleNIds: [],
    effectiveRoleNIds: ['r1'],
    optimisticVersion: 1,
    concurrencyVersion: 'c1',
    isDeleted: false,
  }
}

function makeGroupSummary(): UserGroupSummaryDto {
  return {
    groupNId: 'g1',
    name: 'ops',
    description: null,
    status: 'Active',
    memberCount: 2,
    roleCount: 1,
    optimisticVersion: 1,
    concurrencyVersion: 'c1',
    isDeleted: false,
  }
}

function makeGroupDetail(): UserGroupDetailDto {
  return {
    groupNId: 'g1',
    name: 'ops',
    description: null,
    status: 'Active',
    tenantNId: 't1',
    memberUserNIds: ['u1', 'u2'],
    roleNIds: ['r1'],
    optimisticVersion: 3,
    concurrencyVersion: 'c3',
  }
}

describe('IdentityManagementApi — 用户新契约(§29A.4/§29A.5)', () => {
  it('createUser 提交不含 initialPassword 的载荷并解包 CreateUserResultDto(含一次性 temporaryPassword)', async () => {
    const client = fakeClient()
    const createResult: CreateUserResultDto = {
      user: makeUserSummary(),
      temporaryPassword: 'Tmp!Pass123',
    }
    client.post = vi.fn().mockResolvedValueOnce(createResult) as typeof client.post

    const api = createIdentityManagementApi(client)
    const result = await api.createUser({ loginName: 'alice', name: 'Alice' })

    expect(result).toEqual(createResult)
    expect(result.temporaryPassword).toBe('Tmp!Pass123')
    expect(client.post).toHaveBeenCalledWith('/identity/api/v1/users', {
      loginName: 'alice',
      name: 'Alice',
    })
    // 契约回归:载荷绝不携带 initialPassword。
    const [, body] = (client.post as ReturnType<typeof vi.fn>).mock.calls[0] as [string, unknown]
    expect(JSON.stringify(body)).not.toContain('initialPassword')
    expect(JSON.stringify(body)).not.toContain('Tmp!Pass123')
  })

  it('resetPassword 提交空对象并解包 ResetPasswordResultDto', async () => {
    const client = fakeClient()
    const resetResult: ResetPasswordResultDto = { temporaryPassword: 'Rst!Pass456' }
    client.post = vi.fn().mockResolvedValueOnce(resetResult) as typeof client.post

    const api = createIdentityManagementApi(client)
    const result = await api.resetPassword('u1')

    expect(result).toEqual(resetResult)
    expect(client.post).toHaveBeenCalledWith('/identity/api/v1/users/u1/reset-password', {})
  })

  it('listUsers 携带 groupNId/roleNId/includeDeleted 过滤', async () => {
    const client = fakeClient()
    const page: PageResultDto<UserSummaryDto> = {
      items: [makeUserSummary()],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }
    client.get = vi.fn().mockResolvedValueOnce(page) as typeof client.get

    const api = createIdentityManagementApi(client)
    const result = await api.listUsers({
      groupNId: 'g1',
      roleNId: 'r1',
      includeDeleted: true,
      pageIndex: 1,
      pageSize: 20,
    })

    expect(result).toEqual(page)
    expect(client.get).toHaveBeenCalledWith(
      '/identity/api/v1/users?groupNId=g1&roleNId=r1&includeDeleted=true&pageIndex=1&pageSize=20',
    )
  })
})

describe('IdentityManagementApi — 用户组端点(§29A.5)', () => {
  let api: IdentityManagementApi
  let client: HttpClient

  beforeEach(() => {
    client = fakeClient()
    api = createIdentityManagementApi(client)
  })

  it('listUserGroups 拼查询串并解包分页 UserGroupSummaryDto', async () => {
    const page: PageResultDto<UserGroupSummaryDto> = {
      items: [makeGroupSummary()],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }
    client.get = vi.fn().mockResolvedValueOnce(page) as typeof client.get

    const result = await api.listUserGroups({ name: 'ops', status: 'Active' })

    expect(result).toEqual(page)
    expect(client.get).toHaveBeenCalledWith(
      '/identity/api/v1/user-groups?name=ops&status=Active',
    )
  })

  it('getUserGroup 解包 UserGroupDetailDto(成员/角色全量+双版本)', async () => {
    client.get = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.get

    const result = await api.getUserGroup('g1')

    expect(result).toEqual(makeGroupDetail())
    expect(client.get).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1')
  })

  it('createUserGroup 提交初始成员/角色并解包 UserGroupDetailDto', async () => {
    client.post = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.post

    const result = await api.createUserGroup({
      nId: 'g1',
      name: 'ops',
      description: 'ops group',
      memberUserNIds: ['u1', 'u2'],
      roleNIds: ['r1'],
    })

    expect(result).toEqual(makeGroupDetail())
    expect(client.post).toHaveBeenCalledWith('/identity/api/v1/user-groups', {
      nId: 'g1',
      name: 'ops',
      description: 'ops group',
      memberUserNIds: ['u1', 'u2'],
      roleNIds: ['r1'],
    })
  })

  it('setUserGroupMembers 提交最终成员集+双版本', async () => {
    client.put = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.put

    const result = await api.setUserGroupMembers('g1', {
      memberUserNIds: ['u1', 'u2'],
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })

    expect(result).toEqual(makeGroupDetail())
    expect(client.put).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1/members', {
      memberUserNIds: ['u1', 'u2'],
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })
  })

  it('setUserGroupRoles 提交最终角色集+双版本', async () => {
    client.put = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.put

    const result = await api.setUserGroupRoles('g1', {
      roleNIds: ['r1'],
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })

    expect(result).toEqual(makeGroupDetail())
    expect(client.put).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1/roles', {
      roleNIds: ['r1'],
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })
  })

  it('setUserGroupStatus 提交 enabled+双版本', async () => {
    client.put = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.put

    await api.setUserGroupStatus('g1', {
      enabled: false,
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })

    expect(client.put).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1/status', {
      enabled: false,
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })
  })

  it('deleteUserGroup 以 DELETE+body 提交原因+双版本', async () => {
    client.delete = vi.fn().mockResolvedValueOnce(undefined) as typeof client.delete

    await api.deleteUserGroup('g1', {
      reason: 'cleanup',
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })

    expect(client.delete).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1', {
      reason: 'cleanup',
      expectedOptimisticVersion: 3,
      expectedConcurrencyVersion: 'c3',
    })
  })

  it('restoreUserGroup 提交原因+双版本并解包 UserGroupDetailDto', async () => {
    client.post = vi.fn().mockResolvedValueOnce(makeGroupDetail()) as typeof client.post

    const result = await api.restoreUserGroup('g1', {
      expectedOptimisticVersion: 5,
      expectedConcurrencyVersion: 'c5',
    })

    expect(result).toEqual(makeGroupDetail())
    expect(client.post).toHaveBeenCalledWith('/identity/api/v1/user-groups/g1/restore', {
      expectedOptimisticVersion: 5,
      expectedConcurrencyVersion: 'c5',
    })
  })
})
