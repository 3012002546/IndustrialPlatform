import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const require = createRequire(join(process.cwd(), 'package.json'))
const { chromium } = require('@playwright/test')

const source = readFileSync('tests/e2e/real-login.spec.ts', 'utf8')
const username = source.match(/E2E_ADMIN = '([^']+)'/)?.[1]
const password = source.match(/E2E_PASSWORD = '([^']+)'/)?.[1]
if (!username || !password) throw new Error('test credentials not found')

const outputDir = join(tmpdir(), `PF06-playwright-diagnostic-${Date.now()}`)
mkdirSync(outputDir, { recursive: true })
const startedAt = Date.now()
const errors = []
const failures = []
const responses = []
const requests = new Map()
const browser = await chromium.launch({ channel: 'msedge', headless: false })
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } })
const page = await context.newPage()

page.on('pageerror', (error) => errors.push({ atMs: Date.now() - startedAt, message: error.message }))
page.on('console', (message) => {
  if (message.type() === 'error') errors.push({ atMs: Date.now() - startedAt, message: message.text() })
})
page.on('request', (request) => {
  const url = new URL(request.url())
  requests.set(request, {
    atMs: Date.now() - startedAt,
    epochMs: Date.now(),
    method: request.method(),
    path: url.pathname,
  })
})
page.on('response', (response) => {
  const url = new URL(response.url())
  if (url.pathname.includes('/identity/api/v1/') || url.pathname.includes('/collaboration/')) {
    responses.push({
      atMs: Date.now() - startedAt,
      status: response.status(),
      method: response.request().method(),
      path: url.pathname,
      durationMs: Date.now() - (requests.get(response.request())?.epochMs ?? Date.now()),
    })
  }
})
page.on('requestfailed', (request) => {
  const url = new URL(request.url())
  failures.push({
    atMs: Date.now() - startedAt,
    method: request.method(),
    path: url.pathname,
    error: request.failure()?.errorText ?? 'unknown',
    requestDurationMs: Date.now() - (requests.get(request)?.epochMs ?? Date.now()),
  })
})

const result = {
  startedAt: new Date().toISOString(),
  config: {
    browserChannel: 'msedge',
    headless: false,
    viewport: { width: 1440, height: 900 },
    context: 'new non-persistent context',
    loginUsername: username,
    sourceTimeout: 'src/frontend/.env.local VITE_REQUEST_TIMEOUT_MS=10000; active Vite process environment not directly readable',
    playrightNavigationTimeoutMs: 30000,
    boundedLoginObservationMs: 20000,
  },
  timeline: [],
  responses,
  requestFailures: failures,
  consoleErrors: errors,
}

try {
  result.timeline.push({ atMs: Date.now() - startedAt, event: 'goto-login-start' })
  await page.goto('http://localhost:5173/login', { waitUntil: 'domcontentloaded', timeout: 30000 })
  result.timeline.push({ atMs: Date.now() - startedAt, event: 'login-page-loaded', url: page.url(), title: await page.title() })
  await page.getByTestId('login-username').fill(username)
  await page.getByTestId('login-password').fill(password)
  result.timeline.push({ atMs: Date.now() - startedAt, event: 'credentials-filled' })
  await page.getByTestId('login-submit').click({ noWaitAfter: true })
  result.timeline.push({ atMs: Date.now() - startedAt, event: 'login-clicked' })

  try {
    await page.waitForURL(/\/pc\/home/, { timeout: 20000, waitUntil: 'domcontentloaded' })
    result.timeline.push({ atMs: Date.now() - startedAt, event: 'login-succeeded', url: page.url() })
    await page.locator('.ip-pc-layout').waitFor({ state: 'visible', timeout: 5000 })
    const chatLinks = await page.locator('a').evaluateAll((anchors) =>
      anchors
        .filter((anchor) => (anchor.textContent ?? '').trim() === '聊天')
        .map((anchor) => ({ href: anchor.getAttribute('href'), text: (anchor.textContent ?? '').trim() })),
    )
    result.chatLinks = chatLinks
    result.timeline.push({ atMs: Date.now() - startedAt, event: 'chat-link-inspected', chatLinks })
    if (chatLinks.length > 0) {
      await page.locator('a').filter({ hasText: /^聊天$/ }).first().click({ noWaitAfter: true })
      result.timeline.push({ atMs: Date.now() - startedAt, event: 'chat-link-clicked', url: page.url() })
      try {
        await page.waitForURL(/\/pc\/collaboration(?:$|\/)/, { timeout: 15000, waitUntil: 'domcontentloaded' })
        result.timeline.push({ atMs: Date.now() - startedAt, event: 'chat-route-reached', url: page.url() })
      } catch (error) {
        result.timeline.push({ atMs: Date.now() - startedAt, event: 'chat-route-timeout', url: page.url(), error: error.message })
      }
    }
    await page.screenshot({ path: join(outputDir, 'logged-in.png'), fullPage: true })
  } catch (error) {
    result.timeline.push({ atMs: Date.now() - startedAt, event: 'login-observation-timeout', url: page.url(), error: error.message })
  }
} finally {
  result.final = { atMs: Date.now() - startedAt, url: page.url() }
  result.responses = responses
  result.requestFailures = failures
  result.consoleErrors = errors
  writeFileSync(join(outputDir, 'result.json'), JSON.stringify(result, null, 2), 'utf8')
  await browser.close()
}

console.log(JSON.stringify({ outputDir, resultFile: join(outputDir, 'result.json'), screenshot: join(outputDir, 'logged-in.png'), result }))
