import { createApiError } from '@/api/errors'
import { parseEnvelope } from '@/api/envelope'
import type { AuthGateway, AuthSession, AuthUser, BootstrapStatus } from './types'

export interface EmbeddedAuthGatewayDeps {
  baseUrl: string
  requestTimeoutMs: number
  fetchImpl?: typeof fetch
  /** 仅用于独立宿主 FixedDemo 调试；正式 MES 不启用。 */
  demoAutoLogin?: boolean
  /** 独立协作入口通过 account 校验用户列表后建立页级会话。 */
  standaloneAutoLogin?: boolean
  /** 独立演示页级账户；不是生产认证凭据。 */
  demoAccount?: string
  /** Optional account selector, verified by the server-side MES adapter. */
  account?: string
}

interface EmbeddedIdentityDto {
  tenantNId: string
  userNId: string
  displayName: string
  securityVersion: string
  roles?: string[]
  permissions?: string[]
}

interface EmbeddedSessionDto {
  expiresOn: string
  identity: EmbeddedIdentityDto
  pageSession?: {
    token: string
    binding: string
  }
}

interface PageSessionCredential {
  token: string
  binding: string
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function mapSession(dto: unknown, pageCredential?: PageSessionCredential): AuthSession {
  if (typeof dto !== 'object' || dto === null) {
    throw createApiError('invalidResponse', '嵌入宿主返回的会话格式无效。', 'embedded-session')
  }
  const record = dto as Record<string, unknown>
  const identity = record['identity']
  if (typeof identity !== 'object' || identity === null) {
    throw createApiError('invalidResponse', '嵌入宿主未返回身份投影。', 'embedded-session')
  }
  const value = identity as Record<string, unknown>
  if (
    typeof record['expiresOn'] !== 'string' ||
    typeof value['tenantNId'] !== 'string' ||
    typeof value['userNId'] !== 'string' ||
    typeof value['displayName'] !== 'string' ||
    typeof value['securityVersion'] !== 'string' ||
    (value['roles'] !== undefined && !isStringArray(value['roles'])) ||
    (value['permissions'] !== undefined && !isStringArray(value['permissions']))
  ) {
    throw createApiError('invalidResponse', '嵌入宿主身份投影字段无效。', 'embedded-session')
  }
  return {
    transport: 'embedded-cookie',
    expiresAt: record['expiresOn'],
    ...(pageCredential === undefined
      ? {}
      : {
          embeddedSessionToken: pageCredential.token,
          embeddedSessionBinding: pageCredential.binding,
        }),
    user: {
      userId: value['userNId'],
      username: value['userNId'],
      displayName: value['displayName'],
      tenantId: value['tenantNId'],
      roles: value['roles'] ?? [],
      permissions: value['permissions'] ?? [],
      mustChangePassword: false,
    },
  }
}

export function createEmbeddedAuthGateway(deps: EmbeddedAuthGatewayDeps): AuthGateway {
  const fetcher = deps.fetchImpl ?? globalThis.fetch.bind(globalThis)
  const endpoint = (path: string): string => `${deps.baseUrl.replace(/\/$/, '')}${path}`
  let pageCredential: PageSessionCredential | undefined
  // 同一页面最多创建一次入口会话，并发恢复共享请求；失败不会不断重试。
  let entrySessionPromise: Promise<unknown> | undefined

  async function request(path: string, method: 'GET' | 'POST', accountOverride = deps.account): Promise<EmbeddedSessionDto> {
    const controller = new AbortController()
    const timeout = globalThis.setTimeout(() => controller.abort(), deps.requestTimeoutMs)
    try {
      const headers: Record<string, string> = { Accept: 'application/json' }
      if (pageCredential !== undefined) {
        headers['X-Embedded-Session'] = pageCredential.token
        headers['X-Embedded-Binding'] = pageCredential.binding
      }
      const account = accountOverride
      const pathWithAccount = account === undefined
        ? path
        : `${path}${path.includes('?') ? '&' : '?'}account=${encodeURIComponent(account)}`
      const response = await fetcher(endpoint(pathWithAccount), {
        method,
        credentials: 'include',
        headers,
        signal: controller.signal,
      })
      let body: unknown = undefined
      try {
        body = await response.json()
      } catch {
        body = undefined
      }
      // MVC 会话接口返回平台统一信封，FixedDemo 最小 API 仍返回原始 DTO。
      // 两种入口共用此适配器；先解开信封，才能读取身份与页级凭据。
      const envelope = parseEnvelope(body)
      if (envelope.valid) {
        if (response.ok && !envelope.success)
          throw createApiError('business', envelope.message, 'embedded-session', {
            status: response.status,
            code: envelope.code,
          })
        body = envelope.data ?? { code: envelope.code }
      }
      if (!response.ok) {
        const code =
          typeof body === 'object' &&
          body !== null &&
          typeof (body as Record<string, unknown>)['code'] === 'string'
            ? (body as Record<string, string>)['code']
            : undefined
        const accountMismatch = response.status === 403 && code === 'EMBEDDED_ACCOUNT_MISMATCH'
        throw createApiError(
          response.status === 401 || accountMismatch ? 'unauthorized' : 'server',
          response.status === 401 || accountMismatch ? 'MES 宿主登录会话已失效。' : '嵌入宿主会话服务不可用。',
          'embedded-session',
          { status: response.status, ...(code === undefined ? {} : { code }) },
        )
      }
      const pageSession =
        typeof body === 'object' && body !== null
          ? (body as Record<string, unknown>)['pageSession']
          : undefined
      if (
        typeof pageSession === 'object' &&
        pageSession !== null &&
        typeof (pageSession as Record<string, unknown>)['token'] === 'string' &&
        typeof (pageSession as Record<string, unknown>)['binding'] === 'string'
      ) {
        pageCredential = {
          token: (pageSession as Record<string, string>)['token']!,
          binding: (pageSession as Record<string, string>)['binding']!,
        }
      }
      return body as EmbeddedSessionDto
    } catch (error) {
      if (error instanceof Error && error.name === 'AbortError')
        throw createApiError('timeout', '嵌入宿主会话请求超时。', 'embedded-session')
      throw error
    } finally {
      globalThis.clearTimeout(timeout)
    }
  }

  async function bootstrapSession(): Promise<AuthSession> {
    try {
      return mapSession(await request('/api/v1/embedded/session', 'GET'), pageCredential)
    } catch (error) {
      if (
        !(deps.standaloneAutoLogin || deps.demoAutoLogin) ||
        !(error instanceof Error && 'kind' in error && error.kind === 'unauthorized'
          && ((error as { details?: { status?: number; code?: string } }).details?.status === 401
            || ((error as { details?: { status?: number; code?: string } }).details?.status === 403
              && (error as { details?: { status?: number; code?: string } }).details?.code === 'EMBEDDED_ACCOUNT_MISMATCH')))
      )
        throw error
      const entryPath = deps.standaloneAutoLogin ? '/embedded/standalone/session' : '/embedded/demo/session'
      const account = deps.standaloneAutoLogin ? deps.account : deps.demoAccount ?? deps.account
      entrySessionPromise ??= request(entryPath, 'POST', account)
      await entrySessionPromise
      return mapSession(await request('/api/v1/embedded/session', 'GET'), pageCredential)
    }
  }

  async function logout(): Promise<void> {
    try {
      await request('/api/v1/embedded/session/revoke', 'POST')
    } catch (error) {
      if (!(
        error instanceof Error &&
        'kind' in error &&
        (error as { kind?: string }).kind === 'unauthorized'
      ))
        throw error
    } finally {
      pageCredential = undefined
    }
  }

  async function keepAliveEmbeddedSession(): Promise<AuthSession> {
    return mapSession(await request('/api/v1/embedded/session/heartbeat', 'POST'), pageCredential)
  }

  const unavailable = async (): Promise<AuthSession> => {
    throw createApiError(
      'forbidden',
      '嵌入页面不接受平台用户名密码，请从 MES 登录后重新打开。',
      'embedded-session',
    )
  }

  return {
    login: unavailable,
    refresh: () => bootstrapSession(),
    refreshEmbeddedSession: bootstrapSession,
    keepAliveEmbeddedSession,
    bootstrapSession,
    logout,
    getCurrentUser: async (): Promise<AuthUser> => (await bootstrapSession()).user,
    changePassword: async () => {
      throw createApiError('forbidden', '嵌入页面不提供平台密码修改。', 'embedded-session')
    },
    getBootstrapStatus: async (): Promise<BootstrapStatus> => ({
      state: 'Ready',
      adminExists: true,
    }),
  }
}
