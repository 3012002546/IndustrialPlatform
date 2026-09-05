import { expect, test } from '@playwright/test'

type NodeWrite = {
  nId: string
  name: string
  description: string | null
  isInitial: boolean
  isTerminal: boolean
  outcome: string
  color: string | null
  sort: number
}

type TransitionWrite = {
  fromStatusNId: string
  actionNId: string
  actionName: string
  toStatusNId: string
  description: string | null
}

type StateMachine = {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: string
  tenantNId: string | null
  revision: number
  status: string
  sourceRevision: number | null
  publishedOn: string | null
  publishedBy: string | null
  nodes: (NodeWrite & { id: string })[]
  transitions: (TransitionWrite & { id: string })[]
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  isFrozen: boolean
  isLocked: boolean
}

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`state machine browser fixture ${scenario.width} ${scenario.locale}`, async ({
    page,
  }, testInfo) => {
    const failures: string[] = []
    page.on('pageerror', (error) => failures.push(error.message))
    page.on('console', (message) => {
      if (message.type() === 'error') failures.push(message.text())
    })
    await page.setViewportSize(scenario)

    const permissions = [
      'platform.home.view',
      ...['view', 'create', 'update', 'publish', 'disable'].map(
        (action) => `referencedata.state-machine.${action}`,
      ),
    ]
    const user = {
      userNId: 'PF03-STATE-MACHINE-VISUAL',
      loginName: 'pf03.state-machine.visual',
      name: 'State machine operator',
      tenantNId: 'PF03-STATE-MACHINE-TENANT',
      roleNIds: [],
      permissionNIds: permissions,
      mustChangePassword: false,
    }
    await page.addInitScript(
      ({ user, locale }) => {
        sessionStorage.setItem(
          'industrial-platform.auth.http.v1',
          JSON.stringify({
            version: 1,
            session: {
              accessToken: 'fixture-token',
              refreshToken: 'fixture-refresh',
              expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
              user: {
                userId: user.userNId,
                username: user.loginName,
                displayName: user.name,
                tenantId: user.tenantNId,
                roles: [],
                permissions: user.permissionNIds,
                mustChangePassword: false,
              },
            },
          }),
        )
        localStorage.setItem(
          'industrial-platform.locale.preferences.v1',
          JSON.stringify({
            locale,
            timeZone: 'Asia/Taipei',
            dateFormat: 'yyyy-MM-dd',
            numberLocale: locale,
            unitSystem: 'metric',
          }),
        )
      },
      { user, locale: scenario.locale },
    )

    const envelope = (data: unknown) => ({
      success: true,
      code: '200',
      message: 'success',
      data,
      traceId: 'pf03-state-machine-browser-fixture',
    })
    await page.route('**/identity/api/v1/**', (route) => route.fulfill({ json: envelope(user) }))
    await page.route('**/systemdata/runtime/**', (route) => {
      const url = route.request().url()
      const data = url.includes('theme-policy')
        ? {
            policyRevision: 0,
            configured: false,
            degraded: false,
            allowedPalettes: ['industrial-cyan'],
            allowedModes: ['light', 'dark'],
            allowedPcDensities: ['comfortable'],
            defaultPalette: 'industrial-cyan',
            defaultMode: 'light',
            defaultPcDensity: 'comfortable',
          }
        : url.includes('features')
          ? { revision: 0, configured: false, degraded: false, items: [] }
          : { revision: 0, configured: false, degraded: false, nodes: [] }
      return route.fulfill({ json: envelope(data) })
    })

    const now = '2026-09-05T00:00:00Z'
    const admin = '/referencedata/api/v1/reference-data/admin/state-machines'
    const runtime = '/referencedata/api/v1/reference-data/state-machines'
    let definition: StateMachine | null = null
    let fixedRead = false
    let currentRead = false
    let evaluationRead = false

    function summary() {
      const current = definition!
      return {
        id: current.id,
        nId: current.nId,
        name: current.name,
        description: current.description,
        scopeType: current.scopeType,
        tenantNId: current.tenantNId,
        revision: current.revision,
        status: current.status,
        sourceRevision: current.sourceRevision,
        nodeCount: current.nodes.length,
        transitionCount: current.transitions.length,
        optimisticVersion: current.optimisticVersion,
        concurrencyVersion: current.concurrencyVersion,
        lastUpdatedOn: current.lastUpdatedOn,
        publishedOn: current.publishedOn,
        publishedBy: current.publishedBy,
        isFrozen: current.isFrozen,
        isLocked: current.isLocked,
      }
    }

    function runtimeDefinition() {
      const current = definition!
      return {
        nId: current.nId,
        name: current.name,
        description: current.description,
        sourceScope: current.scopeType,
        sourceTenantNId: current.tenantNId,
        revision: current.revision,
        status: current.status,
        sourceRevision: current.sourceRevision,
        publishedOn: current.publishedOn,
        nodes: current.nodes.map((node) => ({
          nId: node.nId,
          name: node.name,
          description: node.description,
          isInitial: node.isInitial,
          isTerminal: node.isTerminal,
          outcome: node.outcome,
          color: node.color,
          sort: node.sort,
        })),
        transitions: current.transitions.map((transition) => ({
          fromStatusNId: transition.fromStatusNId,
          actionNId: transition.actionNId,
          actionName: transition.actionName,
          toStatusNId: transition.toStatusNId,
          description: transition.description,
        })),
      }
    }

    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const path = url.pathname
      const method = request.method()

      if (path === admin && method === 'GET')
        return route.fulfill({
          json: envelope({
            items: definition ? [summary()] : [],
            total: definition ? 1 : 0,
            pageIndex: 1,
            pageSize: 20,
          }),
        })

      if (path === admin && method === 'POST') {
        const body = request.postDataJSON() as {
          scopeType: string
          nId: string
          name: string
          description: string | null
          nodes: NodeWrite[]
          transitions: TransitionWrite[]
        }
        expect(body).toMatchObject({
          scopeType: 'Tenant',
          nId: 'ORDER_FLOW_FIXTURE',
          nodes: [
            { nId: 'OPEN', isInitial: true, isTerminal: false, outcome: 'None' },
            { nId: 'DONE', isInitial: false, isTerminal: true, outcome: 'Success' },
          ],
          transitions: [
            {
              fromStatusNId: 'OPEN',
              actionNId: 'APPROVE',
              actionName: 'Approve',
              toStatusNId: 'DONE',
            },
          ],
        })
        definition = {
          id: 'bff7a910-0298-4379-9ddf-93d8f7669fd9',
          nId: body.nId.toUpperCase(),
          name: body.name,
          description: body.description,
          scopeType: body.scopeType,
          tenantNId: user.tenantNId,
          revision: 1,
          status: 'Draft',
          sourceRevision: null,
          publishedOn: null,
          publishedBy: null,
          nodes: body.nodes.map((node, index) => ({
            ...node,
            id: `0b5d720d-791d-4a95-884d-aed2a426c8a${index}`,
            nId: node.nId.toUpperCase(),
          })),
          transitions: body.transitions.map((transition, index) => ({
            ...transition,
            id: `1b5d720d-791d-4a95-884d-aed2a426c8a${index}`,
            fromStatusNId: transition.fromStatusNId.toUpperCase(),
            actionNId: transition.actionNId.toUpperCase(),
            toStatusNId: transition.toStatusNId.toUpperCase(),
          })),
          optimisticVersion: 1,
          concurrencyVersion: '1c667792-ab91-4783-a73c-798375868e54',
          lastUpdatedOn: now,
          isFrozen: false,
          isLocked: false,
        }
        return route.fulfill({ status: 201, json: envelope(definition) })
      }

      if (definition && path === `${admin}/${definition.id}` && method === 'GET')
        return route.fulfill({ json: envelope(definition) })

      if (definition && path === `${admin}/${definition.id}` && method === 'PUT') {
        const body = request.postDataJSON() as {
          name: string
          description: string | null
          nodes: NodeWrite[]
          transitions: TransitionWrite[]
          expectedOptimisticVersion: number
          expectedConcurrencyVersion: string
        }
        expect(body.expectedOptimisticVersion).toBe(definition.optimisticVersion)
        expect(body.expectedConcurrencyVersion).toBe(definition.concurrencyVersion)
        definition = {
          ...definition,
          name: body.name,
          description: body.description,
          nodes: body.nodes.map((node, index) => ({ ...node, id: definition!.nodes[index]!.id })),
          transitions: body.transitions.map((transition, index) => ({
            ...transition,
            id: definition!.transitions[index]!.id,
          })),
          optimisticVersion: definition.optimisticVersion + 1,
          concurrencyVersion: '89b0ef29-19b6-4b66-b04c-020b4e536294',
        }
        return route.fulfill({ json: envelope(definition) })
      }

      if (definition && path === `${admin}/${definition.id}/publication-check` && method === 'GET')
        return route.fulfill({
          json: envelope({
            previousRevision: null,
            addedNodeNIds: definition.nodes.map((node) => node.nId),
            removedNodeNIds: [],
            changedNodeNIds: [],
            addedTransitionKeys: definition.transitions.map(
              (transition) => `${transition.fromStatusNId}:${transition.actionNId}`,
            ),
            removedTransitionKeys: [],
            changedTransitionKeys: [],
            errors: [],
          }),
        })

      if (definition && path === `${admin}/${definition.id}/publish` && method === 'POST') {
        const body = request.postDataJSON()
        expect(body.expectedOptimisticVersion).toBe(definition.optimisticVersion)
        expect(body.expectedConcurrencyVersion).toBe(definition.concurrencyVersion)
        definition = {
          ...definition,
          status: 'Published',
          publishedOn: now,
          publishedBy: user.userNId,
          optimisticVersion: definition.optimisticVersion + 1,
          concurrencyVersion: '60196316-b42b-4c77-bdee-fe171b543211',
        }
        return route.fulfill({ json: envelope(definition) })
      }

      if (definition && path === runtime && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: [
              {
                nId: definition.nId,
                name: definition.name,
                sourceScope: 'Tenant',
                sourceTenantNId: user.tenantNId,
                revision: definition.revision,
                publishedOn: definition.publishedOn,
                nodeCount: definition.nodes.length,
                transitionCount: definition.transitions.length,
              },
            ],
            total: 1,
            pageIndex: 1,
            pageSize: 100,
          }),
        })
      }

      const fixed = path.match(/\/state-machines\/([^/]+)\/revisions\/(\d+)$/)
      if (definition && fixed && method === 'GET') {
        fixedRead = true
        expect(decodeURIComponent(fixed[1]!)).toBe('ORDER_FLOW_FIXTURE')
        expect(fixed[2]).toBe('1')
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({ json: envelope(runtimeDefinition()) })
      }

      if (definition && path === `${runtime}/${definition.nId}` && method === 'GET') {
        currentRead = true
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({ json: envelope(runtimeDefinition()) })
      }

      if (definition && path === `${runtime}/${definition.nId}/evaluate` && method === 'POST') {
        evaluationRead = true
        expect(request.postDataJSON()).toEqual({
          sourceScope: 'Tenant',
          sourceTenantNId: user.tenantNId,
          revision: 1,
          fromStatusNId: 'OPEN',
          actionNId: 'REJECT',
        })
        return route.fulfill({
          json: envelope({
            stateMachineNId: definition.nId,
            stateMachineRevision: 1,
            sourceScope: 'Tenant',
            sourceTenantNId: user.tenantNId,
            fromStatusNId: 'OPEN',
            actionNId: 'REJECT',
            allowedByDefinition: false,
            toStatusNId: null,
            reasonCode: 'TRANSITION_NOT_DEFINED',
          }),
        })
      }

      return route.fulfill({ status: 404, json: envelope(null) })
    })

    const labels =
      scenario.locale === 'zh-CN'
        ? {
            success: '成功',
            publish: '发布',
            published: '已发布',
            current: '当前版本',
            fixed: '固定修订',
            denied: '定义不允许',
          }
        : {
            success: 'Success',
            publish: 'Publish',
            published: 'Published',
            current: 'Current revision',
            fixed: 'Fixed revision',
            denied: 'Not allowed',
          }

    await page.goto('/pc/system/reference-data/state-machines')
    const root = page.getByTestId('reference-data-state-machines')
    const more = root.locator('[data-testid="state-machine-more"]:visible')
    await expect(root).toBeVisible()
    await root.getByTestId('state-machine-create').click()
    const editor = page.getByRole('dialog').filter({ has: page.getByTestId('state-machine-save') })
    const editorField = (testId: string) => editor.getByTestId(testId).first()
    await editorField('state-machine-nid').fill('ORDER_FLOW_FIXTURE')
    await editorField('state-machine-name').fill('Fixture order flow')
    await editorField('state-node-nid-0').fill('OPEN')
    await editorField('state-node-name-0').fill('Open')
    await editorField('state-node-color-0').fill('#1677FF')
    await editor.getByTestId('state-node-add').click()
    await editorField('state-node-nid-1').fill('DONE')
    await editorField('state-node-name-1').fill('Done')
    await editor.getByTestId('state-node-terminal-1').first().click()
    await editor.getByTestId('state-node-outcome-1').first().click({ force: true })
    await page.getByRole('option', { name: labels.success, exact: true }).last().click()
    await editorField('state-node-color-1').fill('#52C41A')
    await editor.getByTestId('state-transition-add').click()
    await editor.getByTestId('state-transition-from-0').first().click({ force: true })
    await page.getByRole('option', { name: 'Open', exact: true }).last().click()
    await editorField('state-transition-action-0').fill('APPROVE')
    await editorField('state-transition-action-name-0').fill('Approve')
    await editor.getByTestId('state-transition-to-0').first().click({ force: true })
    await page.getByRole('option', { name: 'Done', exact: true }).last().click()
    await editor.screenshot({
      path: testInfo.outputPath(`state-machine-editor-${scenario.width}.png`),
      animations: 'disabled',
    })
    await editor.getByTestId('state-machine-save').click()
    await expect(editor).not.toBeVisible()

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="state-machine-publish"]:visible')).toBeVisible()
    await page.locator('[data-testid="state-machine-publish"]:visible').click()
    const publication = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('state-machine-publish-submit') })
    await expect(publication.getByTestId('state-machine-publish-confirm')).toContainText('OPEN')
    await expect(publication.getByTestId('state-machine-publish-confirm')).toContainText(
      'OPEN:APPROVE',
    )
    await publication.getByTestId('state-machine-publish-submit').click()
    await expect(root.locator('.app-data-table').first()).toContainText(labels.published)

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="state-machine-check-open"]:visible')).toBeVisible()
    await page.locator('[data-testid="state-machine-check-open"]:visible').click()
    const runtimeDrawer = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('state-machine-evaluate') })
    await expect(runtimeDrawer.getByTestId('state-machine-runtime-source')).toContainText(
      scenario.locale === 'zh-CN' ? '租户' : 'Tenant',
    )
    await expect(runtimeDrawer.getByTestId('state-machine-runtime-revision')).toContainText('1')
    expect(fixedRead).toBe(true)
    await runtimeDrawer.getByTestId('state-machine-read-mode').click({ force: true })
    await page.getByRole('option', { name: labels.current, exact: true }).click()
    await expect.poll(() => currentRead).toBe(true)
    await runtimeDrawer.getByTestId('state-machine-read-mode').click({ force: true })
    await page.getByRole('option', { name: labels.fixed, exact: true }).click()
    await runtimeDrawer.getByTestId('state-machine-from-status').click({ force: true })
    await page.getByRole('option', { name: 'Open (OPEN)', exact: true }).click()
    await runtimeDrawer.getByTestId('state-machine-action-nid').first().fill('REJECT')
    await runtimeDrawer.getByTestId('state-machine-evaluate').click()
    const result = runtimeDrawer.getByTestId('state-machine-evaluation-result')
    await expect(result).toContainText(labels.denied)
    await expect(result).toContainText('TRANSITION_NOT_DEFINED')
    expect(evaluationRead).toBe(true)

    await page.screenshot({
      path: testInfo.outputPath(`state-machines-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    ).toBe(true)
    expect(failures).toEqual([])
  })
}
