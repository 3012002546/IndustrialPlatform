import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { AuthSession } from '@/auth/types'
import type { Json } from '@/api/studio'

/** Cookie sessions retain the shared HTTP/error/upload contract and add Studio antiforgery. */
export function studioCookieClient(raw: HttpClient): HttpClient {
  let token: Promise<string> | null = null
  async function options(original?: RequestOptions): Promise<RequestOptions> {
    token ??= raw
      .get<{ token: string }>('/api/v1/studio/auth/csrf')
      .then((value) => value.token)
      .catch((error) => {
        token = null
        throw error
      })
    return { ...original, headers: { ...original?.headers, 'X-Studio-CSRF': await token } }
  }
  return {
    ...raw,
    post: async <T>(path: string, body?: unknown, opts?: RequestOptions) => {
      const result = await raw.post<T>(path, body, await options(opts))
      if (
        ['/auth/local-login', '/auth/logout', '/auth/local-change-password'].some((endpoint) =>
          path.endsWith(endpoint),
        )
      )
        token = null
      return result
    },
    put: async <T>(path: string, body?: unknown, opts?: RequestOptions) =>
      raw.put<T>(path, body, await options(opts)),
    patch: async <T>(path: string, body?: unknown, opts?: RequestOptions) => {
      if (!raw.patch) throw new Error('PATCH unsupported')
      return raw.patch<T>(path, body, await options(opts))
    },
    delete: async <T>(path: string, body?: unknown, opts?: RequestOptions) =>
      raw.delete<T>(path, body, await options(opts)),
  }
}
export function studioAuthSession(me: Json): AuthSession {
  const p = me.principal as Json
  if (!p || typeof p.subjectNId !== 'string' || !Array.isArray(p.permissions))
    throw new Error('STUDIO_IDENTITY_UNAVAILABLE')
  return {
    transport: 'embedded-cookie',
    expiresAt: String(p.expiresOn),
    user: {
      userId: p.subjectNId,
      username: p.displayName,
      displayName: p.displayName,
      tenantId: p.tenantNId,
      roles: p.roles,
      permissions: p.permissions,
      mustChangePassword: me.mustChangePassword === true,
    },
  }
}
