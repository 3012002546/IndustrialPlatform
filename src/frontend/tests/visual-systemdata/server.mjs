import { createServer } from 'node:http'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

import vue from '@vitejs/plugin-vue'
import { createServer as createViteServer } from 'vite'

import {
  SYSTEM_DATA_VISUAL_AUTH_USER,
  SYSTEM_DATA_VISUAL_FIXTURE,
  SYSTEM_DATA_VISUAL_PERMISSIONS,
} from '../fixtures/systemDataBrowserFixture.ts'

const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const frontendRoot = resolve(scriptDirectory, '../..')
const visualViteCacheDirectory = 'C:/Users/DONG/AppData/Local/Temp/pf02-systemdata-vite-cache'
const portArgumentIndex = process.argv.indexOf('--port')
const parsedPort = portArgumentIndex >= 0 ? Number(process.argv[portArgumentIndex + 1]) : 4187
const port = Number.isInteger(parsedPort) && parsedPort > 0 ? parsedPort : 4187
const origin = `http://127.0.0.1:${port}`

process.env.VITE_API_BASE_URL = origin
process.env.VITE_AUTH_MODE = 'http'
process.env.VITE_DEPLOYMENT_ENVIRONMENT = 'DEV'
process.env.VITE_REQUEST_TIMEOUT_MS = '10000'
process.env.VITE_CACHE_DIR = visualViteCacheDirectory

const requests = []
const navigationRevisionByScenario = new Map()

const navigationScenarioNames = new Set([
  'normal',
  'no-add',
  'add-skipped',
  'mixed',
  'blocked',
  'conflict',
  'validation',
])

const clone = (value) => structuredClone(value)

function envelope(data) {
  return {
    success: true,
    code: 'OK',
    message: 'OK',
    data,
    traceId: 'trace-pf02-visual-fixture',
  }
}

function failure(code, message) {
  return {
    success: false,
    code,
    message,
    data: null,
    traceId: 'trace-pf02-visual-fixture-error',
  }
}

function sendJson(response, status, data, headers = {}) {
  const body = JSON.stringify(data)
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store',
    ...headers,
  })
  response.end(body)
}

function sendFailure(response, status, code, message) {
  sendJson(response, status, failure(code, message))
}

function getCookie(request, name) {
  const header = request.headers.cookie ?? ''
  const value = header
    .split(';')
    .map((item) => item.trim())
    .find((item) => item.startsWith(`${name}=`))
  return value === undefined ? '' : decodeURIComponent(value.slice(name.length + 1))
}

function fixtureState(request) {
  const state = getCookie(request, 'pf02-systemdata-fixture')
  return ['normal', 'empty', 'loading', 'error', 'disabled'].includes(state) ? state : 'normal'
}

function navigationScenario(request) {
  const scenario = getCookie(request, 'pf02-systemdata-navigation-scenario')
  return navigationScenarioNames.has(scenario) ? scenario : 'normal'
}

function navigationRevision(request) {
  const scenario = navigationScenario(request)
  return navigationRevisionByScenario.get(scenario) ?? 12
}

function navigationDraft(request) {
  return {
    ...clone(SYSTEM_DATA_VISUAL_FIXTURE.navigation.draft),
    draftRevision: navigationRevision(request),
  }
}

const navigationDefaultDeclarations = [
  {
    nodeNId: 'navigation.group.workspace',
    label: '工作台',
    parentNodeNId: null,
    kind: 'Group',
    level: 1,
  },
  {
    nodeNId: 'navigation.group.identity-access',
    label: '身份与访问',
    parentNodeNId: null,
    kind: 'Group',
    level: 1,
  },
  {
    nodeNId: 'navigation.link.identity-users',
    label: '用户管理',
    parentNodeNId: 'navigation.group.identity-access',
    kind: 'Link',
    level: 2,
  },
]

function navigationDefaultPreview(request) {
  const scenario = navigationScenario(request)
  const blocked = scenario === 'mixed' || scenario === 'blocked'
  const noAdd = scenario === 'no-add'
  const declarations = navigationDefaultDeclarations
  return {
    draftRevision: navigationRevision(request),
    items: declarations.map((declaration, index) => {
      const action = noAdd ? 'Skipped' : index === 0 ? 'Add' : 'Skipped'
      const itemAction = blocked && index === declarations.length - 1 ? 'Blocked' : action
      return {
        ...declaration,
        action: itemAction,
        reason:
          itemAction === 'Blocked'
            ? '缺少受信任资源:systemdata.identity-users'
            : itemAction === 'Skipped'
              ? '节点已存在,不会覆盖当前草稿。'
              : '确认后追加到当前草稿。',
      }
    }),
  }
}

function navigationValidation(request) {
  const details = (resourceNId, trustedReceiptChecksum) => ({
    resourceNId,
    moduleNId: 'module-systemdata',
    manifestVersion: '2.0.0',
    manifestChecksum: 'sha256:module-systemdata-2.0.0',
    trustedReceiptVersion: '20260904.1',
    trustedReceiptChecksum,
    trustedReceiptVerified: false,
  })
  const refresh = details('resource-platform-refresh', 'sha256:receipt-platform-refresh')
  const exportAction = details('resource-platform-export', 'sha256:receipt-platform-export')
  return {
    draftRevision: navigationRevision(request),
    isValid: false,
    errors: [
      {
        code: 'PERMISSION_UNVERIFIED',
        message: 'Permission receipt is not verified',
        nodeNId: 'nav-platform-diagnostics-page',
        ...refresh,
        receiptDetails: [refresh],
      },
      {
        code: 'PERMISSION_UNVERIFIED',
        message: 'Permission receipt is not verified',
        nodeNId: 'nav-platform-diagnostics-page',
        ...exportAction,
        receiptDetails: [exportAction],
      },
      {
        code: 'RESOURCE_NOT_FOUND',
        message: 'Navigation resource is missing',
        nodeNId: 'nav-platform-diagnostics-page',
        resourceNId: 'resource-missing',
      },
    ],
  }
}

function pageResult(items) {
  return {
    items: clone(items),
    total: items.length,
    pageIndex: 1,
    pageSize: 20,
  }
}

function flatten(items, result = []) {
  for (const item of items) {
    result.push(item)
    flatten(item.children ?? [], result)
  }
  return result
}

const organizationIndex = new Map(
  flatten(SYSTEM_DATA_VISUAL_FIXTURE.organizations.tree).map((item) => [item.nId, item]),
)

function organizationDetail(nId) {
  const item = organizationIndex.get(nId) ?? SYSTEM_DATA_VISUAL_FIXTURE.organizations.detail
  return clone({
    ...item,
    children: undefined,
    organizationRevision: 7,
    optimisticVersion: 2,
    concurrencyVersion: `${item.nId}-visual-v2`,
  })
}

function emptyPayload(path, payload) {
  if (path.endsWith('/organizations/tree')) return []
  if (path.includes('/positions')) return pageResult([])
  if (path.includes('/assignments')) return []
  if (path.endsWith('/users')) return pageResult([])
  if (path.endsWith('/navigation/draft')) return { draftRevision: 0, nodes: [] }
  if (path.endsWith('/resources')) return []
  if (path.endsWith('/features')) return []
  if (path.endsWith('/service-catalog')) return []
  if (path.endsWith('/registrations')) return pageResult([])
  if (path.endsWith('/plans')) return pageResult([])
  if (path.endsWith('/operations')) return pageResult([])
  if (path.endsWith('/environment-policy')) return null
  return payload
}

function recordWrite(request, path) {
  requests.push({ method: request.method ?? 'UNKNOWN', path, kind: 'blocked-write' })
}

function recordFixtureOperation(request, path, operation, result) {
  requests.push({
    method: request.method ?? 'UNKNOWN',
    path,
    kind: 'fixture-operation',
    operation,
    result,
  })
}

async function readJson(request) {
  let body = ''
  for await (const chunk of request) body += chunk
  if (body.trim() === '') return {}
  try {
    return JSON.parse(body)
  } catch {
    return {}
  }
}

async function rejectWrite(request, response, path) {
  recordWrite(request, path)
  for await (const chunk of request) {
    // Consume the body without logging it; visual fixtures never persist it.
    void chunk
  }
  sendFailure(response, 405, 'VISUAL_FIXTURE_WRITE_BLOCKED', 'PF-02 visual fixture blocks writes')
}

function authUserDto() {
  return clone(SYSTEM_DATA_VISUAL_AUTH_USER)
}

function resolveManagementPayload(path, url, request) {
  const fixture = SYSTEM_DATA_VISUAL_FIXTURE
  if (path === '/systemdata/api/v1/organizations/tree') return fixture.organizations.tree
  if (path.startsWith('/systemdata/api/v1/organizations/')) {
    return organizationDetail(decodeURIComponent(path.split('/')[5] ?? ''))
  }
  if (path === '/systemdata/api/v1/positions') {
    const organizationNId = url.searchParams.get('organizationNId')
    return pageResult(
      organizationNId === null || organizationNId === ''
        ? fixture.organizations.positions
        : fixture.organizations.positions.filter(
            (item) => item.organizationNId === organizationNId,
          ),
    )
  }
  if (path.match(/^\/systemdata\/api\/v1\/users\/[^/]+\/assignments$/)) return fixture.assignments
  if (path === '/systemdata/api/v1/resources') return fixture.navigation.resources
  if (path === '/systemdata/api/v1/navigation/draft') return navigationDraft(request)
  if (path === '/systemdata/api/v1/navigation/defaults/preview') {
    recordFixtureOperation(request, path, 'default-preview', 'read-only')
    return navigationDefaultPreview(request)
  }
  if (path === '/systemdata/api/v1/features') return fixture.features
  if (path === '/systemdata/api/v1/service-catalog') return fixture.services
  if (path === '/systemdata/api/v1/theme-policy') return fixture.themePolicy
  if (path === '/systemdata/api/v1/service-initialization/registrations')
    return fixture.initialization.registrations
  if (path === '/systemdata/api/v1/service-initialization/plans')
    return fixture.initialization.plans
  if (path === '/systemdata/api/v1/service-initialization/operations')
    return fixture.initialization.operations
  if (path.match(/^\/systemdata\/api\/v1\/service-initialization\/registrations\/[^/]+\/[^/]+$/))
    return fixture.initialization.registrationDetail
  if (path === '/systemdata/api/v1/service-initialization/environment-policy')
    return fixture.initialization.policy
  if (path.match(/^\/systemdata\/api\/v1\/service-initialization\/plans\/[^/]+$/))
    return fixture.initialization.plans.items[0]
  if (path.match(/^\/systemdata\/api\/v1\/service-initialization\/plans\/[^/]+\/approvals$/))
    return fixture.initialization.approvals
  if (path.match(/^\/systemdata\/api\/v1\/service-initialization\/plans\/[^/]+\/backup-evidence$/))
    return fixture.initialization.backupEvidence
  return undefined
}

function resolveRuntimePayload(path) {
  const runtime = SYSTEM_DATA_VISUAL_FIXTURE.runtime
  if (path.endsWith('/navigation')) return runtime.navigation
  if (path.endsWith('/features')) return runtime.features
  if (path.endsWith('/theme-policy')) return runtime.themePolicy
  return undefined
}

function resolveIdentityPayload(path, url) {
  if (path === '/identity/api/v1/auth/me') return authUserDto()
  if (path === '/identity/api/v1/bootstrap/status') {
    return {
      state: 'Ready',
      schemaVersion: 'visual-fixture',
      adminExists: true,
      mustChangePassword: false,
      credentialDelivered: true,
    }
  }
  if (path === '/identity/api/v1/users' || path === '/identity/api/v1/odata/users') {
    const query = (url.searchParams.get('name') ?? url.searchParams.get('loginName') ?? '').trim()
    const items =
      query === ''
        ? SYSTEM_DATA_VISUAL_FIXTURE.users
        : SYSTEM_DATA_VISUAL_FIXTURE.users.filter((item) =>
            `${item.name} ${item.loginName}`.toLowerCase().includes(query.toLowerCase()),
          )
    return pageResult(items)
  }
  if (path === '/identity/api/v1/roles') return pageResult([])
  if (path === '/identity/api/v1/user-groups') return pageResult([])
  if (path === '/identity/api/v1/permissions/tree') return []
  return undefined
}

async function serveNavigationOperation(request, response, url) {
  const path = url.pathname
  if (!path.startsWith('/systemdata/api/v1/navigation/')) return false
  if (request.method !== 'POST') return false

  if (path === '/systemdata/api/v1/navigation/validate') {
    await readJson(request)
    recordFixtureOperation(request, path, 'validate', 'read-only')
    sendJson(response, 200, envelope(navigationValidation(request)))
    return true
  }

  if (path !== '/systemdata/api/v1/navigation/defaults/import') return false

  const body = await readJson(request)
  const expectedDraftRevision = Number(body.expectedDraftRevision)
  const currentDraftRevision = navigationRevision(request)
  const scenario = navigationScenario(request)
  if (scenario === 'conflict' || expectedDraftRevision !== currentDraftRevision) {
    recordFixtureOperation(request, path, 'default-import', 'conflict')
    sendFailure(
      response,
      409,
      'NAVIGATION_DRAFT_CONFLICT',
      'Navigation draft revision changed; re-preview before importing defaults',
    )
    return true
  }

  const preview = navigationDefaultPreview(request)
  if (preview.items.some((item) => item.action === 'Blocked')) {
    recordFixtureOperation(request, path, 'default-import', 'blocked')
    sendFailure(
      response,
      422,
      'NAVIGATION_DEFAULT_IMPORT_BLOCKED',
      'Blocked default navigation items cannot be partially imported',
    )
    return true
  }

  const nextDraftRevision = currentDraftRevision + 1
  navigationRevisionByScenario.set(scenario, nextDraftRevision)
  recordFixtureOperation(request, path, 'default-import', 'accepted-in-memory')
  sendJson(
    response,
    200,
    envelope({
      draftRevision: nextDraftRevision,
      items: preview.items.map((item) => ({
        ...item,
        action: item.action === 'Add' ? 'Added' : item.action,
      })),
    }),
  )
  return true
}

async function serveApi(request, response, url) {
  const { pathname: path } = url
  const state = fixtureState(request)
  const management = path.startsWith('/systemdata/api/v1/')
  const runtime = path.startsWith('/systemdata/runtime/')
  const identity = path.startsWith('/identity/api/v1/')
  if (!management && !runtime && !identity) return false

  if (
    identity &&
    path !== '/identity/api/v1/auth/me' &&
    path !== '/identity/api/v1/bootstrap/status' &&
    state === 'error'
  ) {
    sendFailure(response, 503, 'VISUAL_FIXTURE_ERROR', 'PF-02 visual fixture error state')
    return true
  }
  if ((management || runtime) && state === 'error' && management) {
    sendFailure(response, 503, 'VISUAL_FIXTURE_ERROR', 'PF-02 visual fixture error state')
    return true
  }
  if (['loading', 'disabled'].includes(state) && (management || runtime || identity))
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 1600))

  if (management && (await serveNavigationOperation(request, response, url))) return true

  if (request.method !== 'GET') {
    await rejectWrite(request, response, path)
    return true
  }

  let payload = management
    ? resolveManagementPayload(path, url, request)
    : runtime
      ? resolveRuntimePayload(path)
      : resolveIdentityPayload(path, url)
  if (payload === undefined) {
    requests.push({ method: 'UNKNOWN_GET', path })
    sendFailure(response, 404, 'VISUAL_FIXTURE_NOT_FOUND', `No visual fixture for ${path}`)
    return true
  }
  if (state === 'empty' && management) payload = emptyPayload(path, payload)
  sendJson(response, 200, envelope(payload), runtime ? { etag: '"pf02-visual-fixture-v1"' } : {})
  return true
}

function bootstrapHtml(url) {
  const state = ['normal', 'empty', 'loading', 'error', 'disabled'].includes(
    url.searchParams.get('state'),
  )
    ? url.searchParams.get('state')
    : 'normal'
  const requestedNavigationScenario = url.searchParams.get('navigationScenario')
  const navigationScenarioValue = navigationScenarioNames.has(requestedNavigationScenario ?? '')
    ? requestedNavigationScenario
    : 'normal'
  const locale = url.searchParams.get('locale') === 'en-US' ? 'en-US' : 'zh-CN'
  const palette = ['industrial-cyan', 'technology-blue', 'neutral-gray'].includes(
    url.searchParams.get('palette'),
  )
    ? url.searchParams.get('palette')
    : 'industrial-cyan'
  const mode = ['light', 'dark', 'system'].includes(url.searchParams.get('mode'))
    ? url.searchParams.get('mode')
    : 'light'
  const density = ['comfortable', 'compact'].includes(url.searchParams.get('density'))
    ? url.searchParams.get('density')
    : 'comfortable'
  const requestedPath = url.searchParams.get('path') ?? '/pc/systemdata/organizations'
  const path = requestedPath.startsWith('/pc/') ? requestedPath : '/pc/systemdata/organizations'
  const authSession = {
    version: 1,
    session: {
      accessToken: 'visual-fixture-access',
      refreshToken: 'visual-fixture-refresh',
      expiresAt: '2099-01-01T00:00:00.000Z',
      user: {
        userId: SYSTEM_DATA_VISUAL_AUTH_USER.userNId,
        username: SYSTEM_DATA_VISUAL_AUTH_USER.loginName,
        displayName: SYSTEM_DATA_VISUAL_AUTH_USER.name,
        tenantId: SYSTEM_DATA_VISUAL_AUTH_USER.tenantNId,
        roles: ['role-visual-admin'],
        permissions: SYSTEM_DATA_VISUAL_PERMISSIONS,
        mustChangePassword: false,
      },
    },
  }
  const localePreferences = {
    locale,
    timeZone: 'UTC',
    dateFormat: locale === 'en-US' ? 'MM/dd/yyyy' : 'yyyy-MM-dd',
    numberLocale: locale,
    unitSystem: 'metric',
  }
  const appearance = { version: 1, palette, mode, density }
  const userPreferences = {
    version: 1,
    palette,
    mode,
    density,
    pcFunctionTreeCollapsed: false,
    updatedAt: '2026-09-03T00:00:00.000Z',
  }
  const userPreferenceKey = 'industrial-platform.ui.preferences.v1:dev-tenant:visual-admin'
  const script = `
    sessionStorage.setItem('industrial-platform.auth.http.v1', ${JSON.stringify(JSON.stringify(authSession))});
    localStorage.setItem('industrial-platform.locale.preferences.v1', ${JSON.stringify(JSON.stringify(localePreferences))});
    localStorage.setItem('industrial-platform.ui.bootstrap.v1', ${JSON.stringify(JSON.stringify(appearance))});
    localStorage.setItem(${JSON.stringify(userPreferenceKey)}, ${JSON.stringify(JSON.stringify(userPreferences))});
    document.cookie = 'pf02-systemdata-fixture=${state}; Path=/; SameSite=Lax';
    document.cookie = 'pf02-systemdata-navigation-scenario=${navigationScenarioValue}; Path=/; SameSite=Lax';
    location.replace(${JSON.stringify(path)});
  `
  return `<!doctype html><html lang="${locale}"><head><meta charset="utf-8"><title>PF-02 SystemData Mock Fixture</title></head><body><p>PF-02 SystemData local Mock fixture; redirecting…</p><script>${script}</script></body></html>`
}

let vite
let server

async function start() {
  vite = await createViteServer({
    root: frontendRoot,
    configFile: false,
    plugins: [vue()],
    cacheDir: visualViteCacheDirectory,
    resolve: { alias: { '@': resolve(frontendRoot, 'src') } },
    appType: 'spa',
    clearScreen: false,
    server: { middlewareMode: true, hmr: false },
  })
  server = createServer((request, response) => {
    void (async () => {
      const url = new URL(request.url ?? '/', origin)
      if (request.method === 'GET' && url.pathname === '/__systemdata_fixture__') {
        response.writeHead(200, {
          'content-type': 'text/html; charset=utf-8',
          'cache-control': 'no-store',
        })
        response.end(bootstrapHtml(url))
        return
      }
      if (request.method === 'GET' && url.pathname === '/__systemdata_fixture__/status') {
        const blockedWrites = requests.filter((item) => item.kind === 'blocked-write').length
        sendJson(response, 200, {
          state: fixtureState(request),
          writes: blockedWrites,
          blockedWrites,
          fixtureOperations: requests.filter((item) => item.kind === 'fixture-operation').length,
          unknown: requests.filter((item) => item.method === 'UNKNOWN_GET').length,
          requests: clone(requests),
        })
        return
      }
      if (await serveApi(request, response, url)) return
      vite.middlewares(request, response, () => {
        sendFailure(
          response,
          404,
          'VISUAL_FIXTURE_ROUTE_NOT_FOUND',
          'Visual fixture route not found',
        )
      })
    })().catch((error) => {
      if (!response.headersSent)
        sendFailure(response, 500, 'VISUAL_FIXTURE_SERVER_ERROR', String(error))
      else response.end()
    })
  })
  await new Promise((resolveListen, rejectListen) => {
    server.once('error', rejectListen)
    server.listen(port, '127.0.0.1', resolveListen)
  })
  console.log(`PF-02 SystemData local Mock fixture: ${origin}/__systemdata_fixture__`)
  console.log(`PF-02 SystemData local Mock fixture PID: ${process.pid}`)
}

async function stop() {
  await vite?.close()
  if (server?.listening) await new Promise((resolveClose) => server.close(resolveClose))
}

process.once('SIGINT', () => void stop().finally(() => process.exit(0)))
process.once('SIGTERM', () => void stop().finally(() => process.exit(0)))

await start()
