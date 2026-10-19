import { chromium, request } from '../src/frontend/node_modules/@playwright/test/index.mjs'
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import assert from 'node:assert/strict'
const base = 'http://localhost:5097'
const ui = 'http://localhost:4173'
const output = '.run/pf07/http-acceptance'
const credentials = JSON.parse(readFileSync(`${output}/credentials-redis.json`, 'utf8'))
const client = await request.newContext()
let browser, page
const checks = [], failures = []
async function json(path, method = 'get', data, headers = {}) {
  const response = await client[method](base + path, { ...(data === undefined ? {} : { data }), headers })
  return { status: response.status(), value: await response.json() }
}
try {
  const login = await json('/identity/api/v1/auth/login', 'post', { loginName: credentials.loginName, password: credentials.temporaryPassword })
  assert.equal(login.status, 200, '真实Identity登录')
  const session = login.value.data
  const headers = { Authorization: `Bearer ${session.accessToken}` }
  const jobs = await json('/systemdata/api/v1/scheduler/jobs', 'get', undefined, headers)
  assert.equal(jobs.status, 200, '任务列表授权路由')
  const job = jobs.value.data.items.find(item => item.nId === 'systemdata.reconcile')
  assert.ok(job, '内建真实对账任务已部署'); assert.equal(job.status, 'Enabled')
  const command = { optimisticVersion: job.optimisticVersion, concurrencyVersion: job.concurrencyVersion }
  const triggerHeaders = { ...headers, 'Idempotency-Key': 'browser-acceptance-' + Date.now() }
  const accepted = await json(`/systemdata/api/v1/scheduler/jobs/${job.nId}/run`, 'post', command, triggerHeaders)
  assert.equal(accepted.status, 202, '实际人工触发202')
  const replay = await json(`/systemdata/api/v1/scheduler/jobs/${job.nId}/run`, 'post', command, triggerHeaders)
  assert.equal(replay.value.data.runNId, accepted.value.data.runNId, '相同命令防重')
  let detail
  for (let i = 0; i < 60; i++) {
    detail = await json(`/systemdata/api/v1/scheduler/runs/${accepted.value.data.runNId}`, 'get', undefined, headers)
    if (['Succeeded', 'Failed', 'Unknown'].includes(detail.value.data?.run.status)) break
    await new Promise(resolve => setTimeout(resolve, 500))
  }
  assert.equal(detail.value.data.run.status, 'Succeeded', '所属模块真实对账成功')
  assert.equal(detail.value.data.attemptHistory.items.length, 1, '原生实际尝试一次')
  const unauthorized = await json('/systemdata/api/v1/scheduler/jobs')
  assert.equal(unauthorized.status, 401)
  const invalid = await json('/systemdata/api/v1/scheduler/jobs', 'post', { ...command, tenantNId: 'forged' }, headers)
  assert.equal(invalid.status, 400, '请求不能覆盖租户或提交内部字段')
  const health = await json('/systemdata/api/v1/platform-health/workspace', 'get', undefined, headers)
  assert.equal(health.status, 200)
  assert.notEqual(health.value.data.state, 'Healthy', '本环境RabbitMQ缺失且扫描Bypass不得全绿')
  assert.ok(health.value.data.services.some(service => service.checks.some(check => check.capabilityKey === 'scheduler' && check.effectiveState === 'Healthy')))
  assert.ok(health.value.data.services.some(service => service.checks.some(check => check.capabilityKey === 'seq' && check.effectiveState === 'Disabled')), '停用Seq不伪装绿色')
  assert.ok(health.value.data.services.some(service => service.checks.some(check => check.capabilityKey === 'file-scan' && check.reasonCode === 'FILE_SCAN_DEVELOPMENT_BYPASS')), '扫描绕过有明确原因')
  const audit = await json('/systemdata/api/v1/audits/facts?action=scheduler.run.manual', 'get', undefined, headers)
  assert.equal(audit.status, 200); assert.ok(audit.value.data.items.some(item => item.objectNId === accepted.value.data.runNId), '既有统一审计可见人工运行')
  checks.push('真实登录/权限/路由/内建真实对账/Succeeded/单次尝试/人工防重/401/越界输入400/降级健康/统一审计')
  // 新测试租户先按既有菜单发布流程发布草稿；已有已发布租户由版本化增量种子自动补入口。
  const draft = await json('/systemdata/api/v1/navigation/draft', 'get', undefined, headers)
  assert.equal(draft.status, 200)
  const published = await json(`/systemdata/api/v1/navigation/publish?expectedDraftRevision=${draft.value.data.draftRevision}`, 'post', undefined, headers)
  assert.equal(published.status, 200, '测试租户使用公开菜单发布入口')
  browser = await chromium.launch({ headless: true })
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } })
  await context.addInitScript((session) => sessionStorage.setItem('industrial-platform.auth.http.v1', JSON.stringify({ version: 1, session })), {
    accessToken: session.accessToken, refreshToken: session.refreshToken, expiresAt: session.expiresAt,
    user: { userId: session.user.userNId, username: session.user.loginName, displayName: session.user.name, tenantId: session.user.tenantNId, roles: session.user.roleNIds, permissions: session.user.permissionNIds, mustChangePassword: session.user.mustChangePassword },
  })
  page = await context.newPage()
  page.on('pageerror', error => failures.push(error.message))
  const network = []
  page.on('response', response => { if (response.url().includes('/scheduler/') || response.url().includes('/platform-health/')) network.push({ path: new URL(response.url()).pathname, status: response.status() }) })
  await page.goto(ui + '/pc/home'); await page.getByRole('heading').first().waitFor()
  await page.getByText('平台服务', { exact: true }).first().waitFor({ timeout: 30000 })
  await page.getByText('平台服务', { exact: true }).first().click()
  const schedulerSection = page.getByRole('button', { name: '任务调度', exact: true })
  if (await schedulerSection.getAttribute('aria-expanded') === 'false') await schedulerSection.click()
  await page.getByText('任务管理', { exact: true }).first().click()
  await page.waitForURL('**/pc/system/scheduler/jobs')
  await page.getByRole('heading', { level: 1, name: '任务管理' }).waitFor()
  await page.getByText('systemdata.reconcile', { exact: true }).first().waitFor()
  mkdirSync(`${output}/screenshots`, { recursive: true })
  await page.screenshot({ path: `${output}/screenshots/jobs-light.png`, fullPage: true })
  await page.goto(ui + `/pc/system/scheduler/runs?jobNId=systemdata.reconcile&runNId=${accepted.value.data.runNId}`)
  await page.getByRole('heading', { level: 1, name: '运行记录' }).waitFor()
  await page.getByText('RECONCILED', { exact: true }).first().waitFor({ timeout: 30000 })
  await page.screenshot({ path: `${output}/screenshots/runs-detail.png`, fullPage: true })
  await page.goto(ui + '/pc/system/health'); await page.getByRole('heading', { level: 1, name: '平台健康' }).waitFor()
  await page.getByText('systemdata.file-scan', { exact: true }).first().waitFor({ timeout: 30000 })
  await page.screenshot({ path: `${output}/screenshots/health-light.png`, fullPage: true })
  await page.getByTestId('theme-control-trigger').click()
  await page.getByTestId('theme-mode-dark').check()
  await page.keyboard.press('Escape')
  assert.equal(await page.locator('html').getAttribute('data-ip-color-mode'), 'dark')
  await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  await page.screenshot({ path: `${output}/screenshots/health-dark-200.png`, fullPage: true })
  assert.equal(failures.length, 0, '浏览器页面无未处理异常')
  assert.ok(network.some(item => item.path.includes('/scheduler/jobs') && item.status === 200))
  assert.ok(network.some(item => item.path.includes('/platform-health/workspace') && item.status === 200))
  assert.equal(network.filter(item => item.status >= 400).length, 0, '页面任务/健康请求无失败')
  checks.push('真实动态菜单打开任务页面/运行明细/健康工作台/亮色/暗色200%截图/无页面异常/实际HTTP请求成功')
  writeFileSync(`${output}/result.json`, JSON.stringify({ state: 'PASS', checks, network, runNId: accepted.value.data.runNId, screenshots: ['jobs-light.png', 'runs-detail.png', 'health-light.png', 'health-dark-200.png'] }, null, 2))
  console.log('PASS — PF07真实HTTP与浏览器链路。凭据、令牌和请求正文未输出。')
} catch (error) {
  if (page) { await page.screenshot({ path: `${output}/browser-failure.png`, fullPage: true }); writeFileSync(`${output}/browser-failure.txt`, await page.locator('body').innerText()) }
  writeFileSync(`${output}/result.json`, JSON.stringify({ state: 'FAIL', checks, error: error.message, failures }, null, 2))
  console.log('FAIL — ' + error.message); process.exitCode = 1
} finally { await browser?.close(); await client.dispose() }
