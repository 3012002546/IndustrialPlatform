import { describe, expect, it, vi } from 'vitest'

import { createEmbeddedAuthGateway } from '@/auth/embeddedAuthGateway'

function response(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response
}

describe('createEmbeddedAuthGateway', () => {
  it('独立入口按 URL account 创建页级会话，平台嵌入入口不受影响', async () => {
    const identity = { tenantNId: 'standalone', userNId: 'user-1', displayName: '用户 1', securityVersion: '1' }
    const fetchImpl = vi.fn()
      .mockResolvedValueOnce(response(401, { code: 'EMBEDDED_SESSION_INVALID' }))
      .mockResolvedValueOnce(response(200, { pageSession: { token: 'page-1', binding: 'binding-1' } }))
      .mockResolvedValueOnce(response(200, { expiresOn: '2030-01-01T00:01:00Z', identity }))
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      standaloneAutoLogin: true,
      account: 'user-1',
      fetchImpl,
    })

    const session = await gateway.bootstrapSession?.()

    expect(session?.user.userId).toBe('user-1')
    expect(session?.embeddedSessionToken).toBe('page-1')
    expect(fetchImpl.mock.calls.map(([url]) => url)).toEqual([
      '/_backend/api/v1/embedded/session?account=user-1',
      '/_backend/embedded/standalone/session?account=user-1',
      '/_backend/api/v1/embedded/session?account=user-1',
    ])
  })

  it('按真实宿主的信封恢复 operator-1 登录并续期，保留本页凭据', async () => {
    const identity = { tenantNId: 'T-1', userNId: 'A', displayName: '系统 A', securityVersion: '1' }
    const fetchImpl = vi.fn()
      .mockResolvedValueOnce(response(401, { success: false, code: '401', message: 'Request failed', data: null }))
      .mockResolvedValueOnce(response(200, { pageSession: { token: 'page-a', binding: 'binding-a' } }))
      .mockResolvedValueOnce(response(200, { success: true, code: '200', message: 'success', data: { expiresOn: '2030-01-01T00:01:00Z', identity } }))
      .mockResolvedValueOnce(response(200, { success: true, code: '200', message: 'success', data: { expiresOn: '2030-01-01T00:02:00Z', identity } }))
    const gateway = createEmbeddedAuthGateway({ baseUrl: '/_backend', requestTimeoutMs: 1000, demoAutoLogin: true, account: 'operator-1', fetchImpl })

    const session = await gateway.bootstrapSession?.()
    expect(session?.user.userId).toBe('A')
    expect(session?.embeddedSessionToken).toBe('page-a')
    const renewed = await gateway.keepAliveEmbeddedSession?.()
    expect(renewed?.expiresAt).toBe('2030-01-01T00:02:00Z')
    expect(renewed?.embeddedSessionBinding).toBe('binding-a')
    expect(fetchImpl.mock.calls.slice(2).every(([, options]) => options.headers['X-Embedded-Session'] === 'page-a')).toBe(true)
  })

  it('拒绝 HTTP 200 的失败信封，不伪造成功会话', async () => {
    const gateway = createEmbeddedAuthGateway({ baseUrl: '/_backend', requestTimeoutMs: 1000, demoAutoLogin: true,
      fetchImpl: vi.fn().mockResolvedValue(response(200, { success: false, code: 'DENIED', message: 'denied', data: null })) })
    await expect(gateway.bootstrapSession?.()).rejects.toMatchObject({ kind: 'business', details: { code: 'DENIED' } })
  })

  it('restores a cookie-only session without manufacturing bearer or refresh tokens', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      response(200, {
        expiresOn: '2030-01-01T00:01:00Z',
        epoch: 3,
        identity: {
          tenantNId: 'T-1',
          userNId: 'ext-user-1',
          displayName: 'MES operator',
          securityVersion: '7',
          roles: ['operator'],
          permissions: ['collaboration.messaging.read'],
        },
      }),
    )
    const gateway = createEmbeddedAuthGateway({
      baseUrl: 'https://embedded.example.test',
      requestTimeoutMs: 1000,
      fetchImpl,
    })

    const session = await gateway.bootstrapSession?.()

    expect(session?.transport).toBe('embedded-cookie')
    expect(session?.accessToken).toBeUndefined()
    expect(session?.refreshToken).toBeUndefined()
    expect(session?.user.permissions).toEqual(['collaboration.messaging.read'])
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://embedded.example.test/api/v1/embedded/session',
      expect.objectContaining({ method: 'GET', credentials: 'include' }),
    )
  })

  it('fails closed when the host session is missing', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response(401, { code: 'EMBEDDED_SESSION_INVALID' }))
    const gateway = createEmbeddedAuthGateway({
      baseUrl: 'https://embedded.example.test',
      requestTimeoutMs: 1000,
      fetchImpl,
    })

    await expect(gateway.bootstrapSession?.()).rejects.toMatchObject({
      kind: 'unauthorized',
    })
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('独立演示缺少会话时创建 A 登录，然后读取服务端完整身份和权限', async () => {
    const fetchImpl = vi
      .fn()
      .mockResolvedValueOnce(response(401, {}))
      .mockResolvedValueOnce(response(200, { currentUser: 'A' }))
      .mockResolvedValueOnce(
        response(200, {
          expiresOn: '2030-01-01T00:01:00Z',
          identity: {
            tenantNId: 'T-1',
            userNId: 'A',
            displayName: '系统 A',
            securityVersion: '1',
            permissions: ['collaboration.messaging.read'],
          },
        }),
      )
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      demoAutoLogin: true,
      fetchImpl,
    })
    const session = await gateway.bootstrapSession?.()
    expect(session?.user.displayName).toBe('系统 A')
    expect(session?.user.permissions).toEqual(['collaboration.messaging.read'])
    expect(session?.accessToken).toBeUndefined()
    expect(fetchImpl.mock.calls.map(([url, options]) => [url, options.method])).toEqual([
      ['/_backend/api/v1/embedded/session', 'GET'],
      ['/_backend/embedded/demo/session', 'POST'],
      ['/_backend/api/v1/embedded/session', 'GET'],
    ])
    expect(fetchImpl.mock.calls.every(([, options]) => options.credentials === 'include')).toBe(
      true,
    )
  })

  it('独立演示账户通过 URL 传给服务端，并只在本页内存携带不透明会话', async () => {
    const fetchImpl = vi
      .fn()
      .mockResolvedValueOnce(response(401, {}))
      .mockResolvedValueOnce(
        response(200, {
          pageSession: { token: 'page-token-b', binding: 'page-binding-b' },
        }),
      )
      .mockResolvedValueOnce(
        response(200, {
          expiresOn: '2030-01-01T00:01:00Z',
          identity: {
            tenantNId: 'T-1',
            userNId: 'B',
            displayName: '系统 B',
            securityVersion: '1',
            permissions: ['collaboration.messaging.read'],
          },
        }),
      )
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      demoAutoLogin: true,
      demoAccount: 'operator-2',
      fetchImpl,
    })

    const session = await gateway.bootstrapSession?.()

    expect(session?.user.displayName).toBe('系统 B')
    expect(fetchImpl.mock.calls.map(([url]) => url)).toEqual([
      '/_backend/api/v1/embedded/session',
      '/_backend/embedded/demo/session?account=operator-2',
      '/_backend/api/v1/embedded/session',
    ])
    expect(fetchImpl.mock.calls[2]?.[1]).toEqual(
      expect.objectContaining({
        headers: expect.objectContaining({
          'X-Embedded-Session': 'page-token-b',
          'X-Embedded-Binding': 'page-binding-b',
        }),
      }),
    )
  })

  it('演示端点不可用时不伪造登录，并发和后续恢复最多尝试一次创建', async () => {
    const fetchImpl = vi.fn<typeof fetch>(async (url) =>
      response(String(url).endsWith('/demo/session') ? 404 : 401, {}),
    )
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      demoAutoLogin: true,
      fetchImpl,
    })
    const results = await Promise.allSettled([
      gateway.bootstrapSession?.(),
      gateway.bootstrapSession?.(),
    ])
    expect(results.every((result) => result.status === 'rejected')).toBe(true)
    await expect(gateway.bootstrapSession?.()).rejects.toMatchObject({ kind: 'server' })
    expect(
      fetchImpl.mock.calls.filter(([url]) => String(url).endsWith('/demo/session')),
    ).toHaveLength(1)
  })

  it('后端故障不会触发演示登录', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response(503, {}))
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      demoAutoLogin: true,
      fetchImpl,
    })
    await expect(gateway.bootstrapSession?.()).rejects.toMatchObject({ kind: 'server' })
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('forwards a formal MES account only on the session endpoints', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(response(200, {
      expiresOn: '2030-01-01T00:01:00Z',
      identity: {
        tenantNId: 'T-1',
        userNId: 'U-1',
        displayName: 'MES operator',
        securityVersion: '7',
      },
    }))
    const gateway = createEmbeddedAuthGateway({
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      account: 'mes-user-42',
      fetchImpl,
    })

    await gateway.bootstrapSession?.()
    expect(fetchImpl).toHaveBeenCalledWith(
      '/_backend/api/v1/embedded/session?account=mes-user-42',
      expect.anything(),
    )
  })

  it('仅把 FixedDemo 的 account mismatch 403 当作切换入口，并替换本页凭据', async () => {
    const fetchImpl = vi
      .fn()
      .mockResolvedValueOnce(response(200, {
        pageSession: { token: 'page-token-a', binding: 'page-binding-a' },
        expiresOn: '2030-01-01T00:01:00Z',
        identity: { tenantNId: 'T-1', userNId: 'A', displayName: '系统 A', securityVersion: '1' },
      }))
      .mockResolvedValueOnce(response(403, { code: 'EMBEDDED_ACCOUNT_MISMATCH' }))
      .mockResolvedValueOnce(response(200, {
        pageSession: { token: 'page-token-b', binding: 'page-binding-b' },
      }))
      .mockResolvedValueOnce(response(200, {
        expiresOn: '2030-01-01T00:02:00Z',
        identity: { tenantNId: 'T-1', userNId: 'B', displayName: '系统 B', securityVersion: '1' },
      }))
    const deps = {
      baseUrl: '/_backend',
      requestTimeoutMs: 1000,
      demoAutoLogin: true,
      account: 'operator-1',
      fetchImpl,
    }
    const gateway = createEmbeddedAuthGateway(deps)
    await gateway.bootstrapSession?.()
    deps.account = 'operator-2'

    const session = await gateway.bootstrapSession?.()

    expect(session?.user.displayName).toBe('系统 B')
    expect(fetchImpl.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      headers: expect.objectContaining({
        'X-Embedded-Session': 'page-token-a',
        'X-Embedded-Binding': 'page-binding-a',
      }),
    }))
    expect(fetchImpl.mock.calls[2]?.[0]).toBe('/_backend/embedded/demo/session?account=operator-2')
    expect(fetchImpl.mock.calls[3]?.[1]).toEqual(expect.objectContaining({
      headers: expect.objectContaining({
        'X-Embedded-Session': 'page-token-b',
        'X-Embedded-Binding': 'page-binding-b',
      }),
    }))
  })
})
