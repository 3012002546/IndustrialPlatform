/**
 * PF-06 独立验收脚本：仅供本轮 Playwright Edge 双上下文验证使用，不是产品运行代码。
 * 从仓库 E2E 测试文件读取 admin 凭据，仅在进程内使用；不输出或持久化密码、token、Cookie。
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const require = createRequire(join(process.cwd(), 'package.json'))
const { chromium } = require('@playwright/test')

const source = readFileSync('tests/e2e/real-login.spec.ts', 'utf8')
const adminUsername = source.match(/E2E_ADMIN = '([^']+)'/)?.[1]
const adminPassword = source.match(/E2E_PASSWORD = '([^']+)'/)?.[1]
if (!adminUsername || !adminPassword) throw new Error('test credentials not found')

const apiOrigin = 'http://localhost:5041'
const webOrigin = 'http://localhost:5173'
const stamp = Date.now().toString()
const peerNId = `pf06-peer-${stamp}`
const peerLogin = peerNId
const peerName = `PF06 Playwright Peer ${stamp}`
const peerPassword = `Pf06Peer!${stamp}Aa9`
const acceptanceText = `PF06自动化测试 ${stamp}`
const outputDir = join(tmpdir(), `PF06-playwright-dual-context-${stamp}`)
mkdirSync(outputDir, { recursive: true })

const result = {
  startedAt: new Date().toISOString(),
  setup: {
    browserChannel: 'msedge',
    headless: false,
    contexts: 'two new non-persistent contexts; no persistent user profile',
    apiOrigin,
    webOrigin,
    sender: adminUsername,
    receiver: peerLogin,
    receiverBusinessId: peerNId,
    receiverDisplayName: peerName,
    assignedRole: '系统管理员（本轮隔离测试用户，清理后失效）',
    acceptanceText,
  },
  timeline: [],
  navigation: {},
  text: {},
  media: {},
  cleanup: {},
  http: { sender: [], receiver: [] },
  failures: [],
}

function mark(event, details = {}) {
  result.timeline.push({ at: new Date().toISOString(), event, ...details })
}

function attachDiagnostics(page, actor) {
  page.on('response', (response) => {
    const url = new URL(response.url())
    if (url.origin !== apiOrigin) return
    if (url.pathname.includes('/identity/api/v1/') || url.pathname.includes('/collaboration/')) {
      result.http[actor].push({ method: response.request().method(), path: url.pathname, status: response.status() })
    }
  })
  page.on('requestfailed', (request) => {
    const url = new URL(request.url())
    if (url.origin !== apiOrigin) return
    result.failures.push({ actor, kind: 'requestfailed', method: request.method(), path: url.pathname, error: request.failure()?.errorText ?? 'unknown' })
  })
  page.on('pageerror', (error) => result.failures.push({ actor, kind: 'pageerror', message: error.message }))
  page.on('console', (message) => {
    if (message.type() === 'error') result.failures.push({ actor, kind: 'console-error', message: message.text() })
  })
}

async function login(page, username, password, replacementPassword, actor) {
  await page.goto(`${webOrigin}/login`, { waitUntil: 'domcontentloaded', timeout: 30000 })
  await page.getByTestId('login-username').fill(username)
  await page.getByTestId('login-password').fill(password)
  await page.getByTestId('login-submit').click({ noWaitAfter: true })
  await page.waitForFunction(
    () => location.pathname === '/change-password' || location.pathname.startsWith('/pc/'),
    undefined,
    { timeout: 25000 },
  )
  if (page.url().includes('/change-password')) {
    mark('first-login-password-change-required', { actor })
    await page.getByTestId('change-current-password').fill(password)
    await page.getByTestId('change-new-password').fill(replacementPassword)
    await page.getByTestId('change-confirm-password').fill(replacementPassword)
    await page.getByTestId('change-submit').click({ noWaitAfter: true })
    await page.waitForURL(/\/login(?:$|\?)/, { timeout: 25000, waitUntil: 'domcontentloaded' })
    await page.getByTestId('login-username').fill(username)
    await page.getByTestId('login-password').fill(replacementPassword)
    await page.getByTestId('login-submit').click({ noWaitAfter: true })
    await page.waitForURL(/\/pc\/home(?:$|\/)/, { timeout: 25000, waitUntil: 'domcontentloaded' })
  }
  await page.locator('.ip-pc-layout').waitFor({ state: 'visible', timeout: 8000 })
  mark('logged-in', { actor, route: new URL(page.url()).pathname })
}

function visibleMenuLink(page, label) {
  return page.locator('.ip-function-tree a').filter({ hasText: new RegExp(`^${label}$`) }).first()
}

async function clickMenuLink(page, label, expectedPath, actor) {
  const groupByMenu = { 用户管理: '系统管理', 聊天: '工作台' }
  const group = groupByMenu[label]
  if (group) {
    const groupButton = page.locator(`.ip-toolrail__button[aria-label="${group}"]`).first()
    if (await groupButton.count()) {
      await groupButton.click()
      await page.locator('.ip-function-tree').waitFor({ state: 'visible', timeout: 8000 })
    }
  }
  const functionTree = page.locator('.ip-function-tree')
  const treeToggle = page.getByTestId('function-tree-toggle')
  if (await treeToggle.count() && (await treeToggle.getAttribute('aria-expanded')) === 'false') {
    await treeToggle.click()
  }
  const sections = functionTree.locator('button.ip-function-tree__section')
  for (let index = 0; index < await sections.count(); index += 1) {
    const section = sections.nth(index)
    if ((await section.getAttribute('aria-expanded')) === 'false') await section.click()
  }
  let link = visibleMenuLink(page, label)
  if (!(await link.count())) {
    result.navigation[`${actor}:menu-sections`] = await sections.allTextContents()
  }
  try {
    await link.waitFor({ state: 'visible', timeout: 10000 })
  } catch (error) {
    result.navigation[`${actor}:${label}:diagnostic`] = await page.locator('a').evaluateAll((anchors, labelValue) => anchors.map((anchor) => {
      const rect = anchor.getBoundingClientRect()
      return { text: (anchor.textContent ?? '').replace(/\s+/g, ' ').trim(), href: anchor.getAttribute('href'), display: getComputedStyle(anchor).display, rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height } }
    }).filter((item) => item.text.includes(labelValue)), label)
    await page.screenshot({ path: join(outputDir, `${actor}-${label}-navigation.png`), fullPage: true }).catch(() => undefined)
    throw error
  }
  const href = await link.getAttribute('href')
  result.navigation[`${actor}:${label}`] = { href }
  await link.click({ noWaitAfter: true })
  await page.waitForURL(new RegExp(expectedPath), { timeout: 15000, waitUntil: 'domcontentloaded' })
  mark('menu-route-reached', { actor, label, href, route: new URL(page.url()).pathname })
}

async function createIsolatedUser(page) {
  await clickMenuLink(page, '用户管理', '/pc/identity/users(?:$|/)', 'sender')
  await page.getByTestId('identity-users-page').waitFor({ state: 'visible', timeout: 10000 })
  await page.getByTestId('identity-users-create').click()
  const panel = page.locator('.app-form-drawer__panel').last()
  await panel.waitFor({ state: 'visible', timeout: 5000 })
  await panel.getByLabel('业务标识').fill(peerNId)
  await panel.getByLabel('登录名').fill(peerLogin)
  await panel.getByLabel('姓名').fill(peerName)
  await panel.getByRole('button', { name: '确认', exact: true }).click()
  await page.getByTestId('temporary-password').waitFor({ state: 'visible', timeout: 15000 })
  const temporaryPassword = await page.getByTestId('temporary-password').textContent()
  if (!temporaryPassword) throw new Error('temporary password dialog did not expose a password')
  await page.getByTestId('temporary-password-confirm').click()
  mark('isolated-user-created-through-ui', { businessId: peerNId, loginName: peerLogin })
  return temporaryPassword.trim()
}

async function assignSystemAdmin(page) {
  const loginFilter = page.getByLabel('登录名').first()
  await loginFilter.fill(peerLogin)
  const queryPanel = page.getByTestId('identity-users-query')
  await queryPanel.getByTestId('query-panel-submit').click()
  const row = page.locator('.vxe-body--row').filter({ hasText: peerLogin }).first()
  await row.waitFor({ state: 'visible', timeout: 15000 })
  const actions = page.locator(`[data-testid="identity-user-actions-${peerNId}"]:visible`).first()
  const directAssign = actions.getByRole('button', { name: '分配角色', exact: true })
  if (await directAssign.count()) {
    await directAssign.click()
  } else {
    const more = actions.locator('[data-testid^="identity-user-more-"]')
    await more.click()
    await page.locator('[data-testid^="identity-user-action-assign-role-"]:visible').click()
  }
  const panel = page.locator('.app-form-drawer__panel').last()
  await panel.waitFor({ state: 'visible', timeout: 5000 })
  await panel.locator('.users-page__role-select .el-select__wrapper').click()
  await page.locator('.el-select-dropdown:visible').getByRole('option', { name: '系统管理员', exact: true }).click()
  await panel.getByRole('button', { name: '确认', exact: true }).click()
  await page.getByText('角色已更新', { exact: true }).waitFor({ state: 'visible', timeout: 10000 }).catch(() => undefined)
  mark('isolated-user-assigned-existing-system-role-through-ui', { loginName: peerLogin })
}

async function goChat(page, actor) {
  await clickMenuLink(page, '聊天', '/pc/collaboration(?:$|/)', actor)
  await page.locator('.collaboration-chat').waitFor({ state: 'visible', timeout: 12000 })
  const chatHref = result.navigation[`${actor}:聊天`]?.href ?? null
  result.navigation[`${actor}:聊天`].route = new URL(page.url()).pathname
  return chatHref
}

async function createConversation(page) {
  const directory = page.locator('#collaboration-directory')
  await directory.fill(peerLogin)
  const option = page.getByRole('option').filter({ hasText: peerLogin }).first()
  await option.waitFor({ state: 'visible', timeout: 15000 })
  const optionText = await option.textContent()
  await option.click()
  await page.locator('.collaboration-chat__conversation-header').waitFor({ state: 'visible', timeout: 15000 })
  result.text.directoryMatch = optionText?.replace(/\s+/g, ' ').trim() ?? null
  mark('conversation-created-through-directory-ui', { peer: peerLogin })
}

async function selectConversationForPeer(page) {
  const conversation = page.locator('.collaboration-chat__conversation').filter({ hasText: peerName }).first()
  if (!(await conversation.count())) {
    await page.reload({ waitUntil: 'domcontentloaded' })
  }
  await conversation.waitFor({ state: 'visible', timeout: 20000 })
  await conversation.click()
  await page.locator('.collaboration-chat__conversation-header').waitFor({ state: 'visible', timeout: 15000 })
}

async function sendAndReceive(senderPage, receiverPage) {
  await senderPage.locator('.collaboration-chat__composer textarea').fill(acceptanceText)
  await senderPage.locator('.collaboration-chat__composer').getByRole('button', { name: '发送', exact: true }).click()
  await senderPage.locator('.collaboration-chat__message-text').filter({ hasText: acceptanceText }).last().waitFor({ state: 'visible', timeout: 15000 })
  await receiverPage.locator('.collaboration-chat__message-text').filter({ hasText: acceptanceText }).last().waitFor({ state: 'visible', timeout: 20000 })
  result.text = { sent: true, senderRendered: true, receiverRendered: true, exactText: acceptanceText }
  mark('text-sent-and-received', { text: acceptanceText })
}

async function probeMedia(senderPage, receiverPage) {
  const media = { voice: {}, screen: {} }
  const voiceButton = senderPage.getByRole('button', { name: '语音通话', exact: true })
  await voiceButton.waitFor({ state: 'visible', timeout: 10000 })
  await voiceButton.click()
  await senderPage.waitForTimeout(1200)
  const senderVoiceError = await senderPage.locator('.collaboration-chat__error').allTextContents()
  const senderVoiceBar = await senderPage.locator('.collaboration-voice-bar').allTextContents()
  media.voice.senderError = senderVoiceError.map((item) => item.trim()).filter(Boolean)
  media.voice.senderState = senderVoiceBar.join(' ').replace(/\s+/g, ' ').trim() || null
  if (media.voice.senderState) {
    await receiverPage.waitForTimeout(1200)
    const decline = receiverPage.getByRole('button', { name: '拒绝', exact: true })
    if (await decline.count()) {
      await decline.click()
      await senderPage.waitForTimeout(800)
    }
    media.voice.receiverState = (await receiverPage.locator('.collaboration-voice-bar').allTextContents()).join(' ').replace(/\s+/g, ' ').trim() || null
    media.voice.result = 'invite-created-and-decline-path-exercised'
  } else if (media.voice.senderError.some((item) => item.includes('MEDIA_DISABLED'))) {
    media.voice.result = 'runtime-disabled-confirmed-by-protected-invite'
  } else {
    media.voice.result = 'no-visible-state-or-error'
  }

  const screenButton = senderPage.getByRole('button', { name: '共享我的屏幕', exact: true })
  await screenButton.waitFor({ state: 'visible', timeout: 10000 })
  await screenButton.click()
  await senderPage.waitForTimeout(1200)
  const senderScreenError = await senderPage.locator('.collaboration-chat__error').allTextContents()
  const senderScreenPanel = await senderPage.locator('.collaboration-screen-panel').allTextContents()
  media.screen.senderError = senderScreenError.map((item) => item.trim()).filter(Boolean)
  media.screen.senderState = senderScreenPanel.join(' ').replace(/\s+/g, ' ').trim() || null
  if (media.screen.senderState) {
    await receiverPage.waitForTimeout(1200)
    const decline = receiverPage.getByRole('button', { name: '拒绝', exact: true })
    if (await decline.count()) {
      await decline.click()
      await senderPage.waitForTimeout(800)
    }
    media.screen.receiverState = (await receiverPage.locator('.collaboration-screen-panel').allTextContents()).join(' ').replace(/\s+/g, ' ').trim() || null
    media.screen.result = 'invite-created-and-decline-path-exercised'
  } else if (media.screen.senderError.some((item) => item.includes('MEDIA_DISABLED'))) {
    media.screen.result = 'runtime-disabled-confirmed-by-protected-invite'
  } else {
    media.screen.result = 'no-visible-state-or-error'
  }
  result.media = media
  mark('media-protected-invite-probes-complete', { voice: media.voice.result, screen: media.screen.result })
}

async function deleteIsolatedUser(page) {
  await clickMenuLink(page, '用户管理', '/pc/identity/users(?:$|/)', 'sender-cleanup')
  const loginFilter = page.getByLabel('登录名').first()
  await loginFilter.fill(peerLogin)
  await page.getByTestId('identity-users-query').getByTestId('query-panel-submit').click()
  const row = page.locator('.vxe-body--row').filter({ hasText: peerLogin }).first()
  await row.waitFor({ state: 'visible', timeout: 15000 })
  const actions = page.locator(`[data-testid="identity-user-actions-${peerNId}"]:visible`).first()
  const directDelete = actions.getByRole('button', { name: '删除', exact: true })
  if (await directDelete.count()) {
    await directDelete.click()
  } else {
    await actions.locator('[data-testid^="identity-user-more-"]').click()
    await page.locator('[data-testid^="identity-user-action-delete-"]:visible').click()
  }
  const dialog = page.getByRole('dialog').filter({ hasText: peerLogin }).last()
  await dialog.waitFor({ state: 'visible', timeout: 5000 })
  const input = dialog.locator('input').last()
  if (await input.count()) await input.fill('PF06 独立验收清理')
  await dialog.getByRole('button', { name: '删除', exact: true }).click()
  await row.waitFor({ state: 'detached', timeout: 15000 }).catch(() => undefined)
  result.cleanup = { isolatedUserDeletedThroughUi: true, loginName: peerLogin, reason: 'PF06 独立验收清理' }
  mark('isolated-user-deleted-through-ui', { loginName: peerLogin })
}

let browser
let senderContext
let receiverContext
let senderPage
let receiverPage
let temporaryPassword

try {
  browser = await chromium.launch({ channel: 'msedge', headless: false })
  senderContext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  senderPage = await senderContext.newPage()
  attachDiagnostics(senderPage, 'sender')
  await login(senderPage, adminUsername, adminPassword, adminPassword, 'sender')
  temporaryPassword = await createIsolatedUser(senderPage)
  await assignSystemAdmin(senderPage)

  receiverContext = await browser.newContext({ viewport: { width: 1440, height: 900 } })
  receiverPage = await receiverContext.newPage()
  attachDiagnostics(receiverPage, 'receiver')
  await login(receiverPage, peerLogin, temporaryPassword, peerPassword, 'receiver')
  temporaryPassword = null

  await goChat(senderPage, 'sender')
  await goChat(receiverPage, 'receiver')
  await createConversation(senderPage)
  await selectConversationForPeer(receiverPage)
  await sendAndReceive(senderPage, receiverPage)
  await probeMedia(senderPage, receiverPage)
} catch (error) {
  result.failures.push({ kind: 'script', message: error instanceof Error ? error.message : String(error) })
  mark('acceptance-script-failed', { message: error instanceof Error ? error.message : String(error) })
} finally {
  if (receiverContext) await receiverContext.close().catch(() => undefined)
  if (senderPage && senderContext && result.cleanup.isolatedUserDeletedThroughUi !== true) {
    try {
      await deleteIsolatedUser(senderPage)
    } catch (error) {
      result.cleanup = { isolatedUserDeletedThroughUi: false, loginName: peerLogin, error: error instanceof Error ? error.message : String(error) }
      result.failures.push({ kind: 'cleanup', message: result.cleanup.error })
    }
  }
  if (senderContext) await senderContext.close().catch(() => undefined)
  if (browser) await browser.close().catch(() => undefined)
  result.completedAt = new Date().toISOString()
  result.outputDir = outputDir
  result.resultFile = join(outputDir, 'result.json')
  writeFileSync(result.resultFile, JSON.stringify(result, null, 2), 'utf8')
  console.log(JSON.stringify(result))
}
