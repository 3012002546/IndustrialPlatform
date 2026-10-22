import { describe, expect, it, vi } from 'vitest'
import { createStudioApi, type StudioResource } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import {
  createHistory,
  evaluateCondition,
  logicalDelta,
  resolveBinding,
  safeTotal,
  validateFieldBindings,
  syncGridPlacement,
  newNode,
} from '@/components/studio/shared/model'
import { studioAuthSession, studioCookieClient } from '@/app/studioSession'

function client(): HttpClient {
  return {
    get: vi.fn().mockResolvedValue({ token: 'csrf' }),
    getWithMeta: vi.fn(),
    post: vi.fn().mockResolvedValue({}),
    put: vi.fn().mockResolvedValue({}),
    delete: vi.fn(),
  }
}
describe('Studio shared editing and binding contract', () => {
  it('persists responsive order and span from canvas placement', () => {
    const root = newNode('Container'),
      first = newNode('Text'),
      second = newNode('Text')
    Object.assign(first.grid, { order: 0, x: 100, y: 300, width: 600 })
    Object.assign(second.grid, { order: 1, x: 0, y: 0 })
    root.children.push(first, second)
    syncGridPlacement(root, first.id, 1200)
    expect(root.children.map((node) => node.id)).toEqual([second.id, first.id])
    expect(first.grid).toMatchObject({ span: 12, order: 1 })
  })
  it('adapts actual deployment profile arrays without a nonexistent capabilities endpoint', async () => {
    const http = client()
    vi.mocked(http.get).mockResolvedValue({
      installedModules: ['LowCode'],
      enabledModules: ['LowCode'],
    })
    expect(await createStudioApi(http).capabilities()).toMatchObject({
      coreReady: true,
      modules: [{ key: 'LowCode', installed: true, enabled: true, available: true }],
    })
    expect(http.get).toHaveBeenCalledWith('/api/v1/studio/deployment/profile', expect.anything())
  })
  it.each([0.5, 1, 1.5])('converts a 120 logical pixel move at zoom %s', (zoom) => {
    expect(logicalDelta(120 * zoom, zoom)).toBe(120)
  })
  it('keeps one hundred undo commands, selection and independent snapshots', () => {
    const source = { x: 0, selected: 'first' },
      history = createHistory(source)
    for (let i = 1; i <= 110; i++) history.commit({ x: i, selected: 'node-' + i })
    source.x = 999
    for (let i = 0; i < 100; i++) history.undo()
    expect(history.undo()).toEqual({ x: 10, selected: 'node-10' })
    expect(history.redo().x).toBe(11)
    history.commit({ x: 200, selected: 'new' })
    expect(history.canRedo).toBe(false)
  })
  it('preserves zero and false while rejecting prototype paths and missing required paths', () => {
    expect(
      resolveBinding(
        { from: 'Row', path: ['value'], nullPolicy: 'Default', defaultValue: 99 },
        { Row: { value: 0 } },
      ),
    ).toBe(0)
    expect(resolveBinding({ from: 'Form', path: ['enabled'] }, { Form: { enabled: false } })).toBe(
      false,
    )
    expect(() => resolveBinding({ from: 'Row', path: ['constructor'] }, { Row: {} })).toThrow(
      'PATH_INVALID',
    )
    expect(() => resolveBinding({ from: 'PageParam', path: ['line'] }, { PageParam: {} })).toThrow(
      'line',
    )
  })
  it('compares exact numeric strings above JavaScript safe integer precision', () => {
    expect(
      evaluateCondition(
        {
          op: 'Gt',
          left: { from: 'Constant', value: '9007199254740993.000001' },
          right: { from: 'Constant', value: '9007199254740993' },
        },
        {},
      ),
    ).toBe(true)
    expect(
      evaluateCondition(
        {
          op: 'Lt',
          left: { from: 'Constant', value: '-2.50' },
          right: { from: 'Constant', value: '-2.49' },
        },
        {},
      ),
    ).toBe(true)
  })
  it('validates mapped field IDs and conditional required inputs', () => {
    const fields = [
      {
        writeTarget: 'stable-id',
        requiredWhen: {
          op: 'Eq',
          left: { from: 'Form', path: ['mode'] },
          right: { from: 'Constant', value: 'write' },
        },
        validators: [],
      },
    ]
    expect(() => validateFieldBindings(fields, {}, { Form: { mode: 'write' } })).toThrow(
      'stable-id',
    )
    expect(() =>
      validateFieldBindings(fields, { 'stable-id': 'filled' }, { Form: { mode: 'write' } }),
    ).not.toThrow()
  })
  it('rejects an unsafe table total instead of rounding its Int64 string', () => {
    expect(safeTotal('125')).toBe(125)
    expect(() => safeTotal('9007199254740993')).toThrow('STUDIO_LIMIT')
  })
})
describe('Studio API and cookie identity boundary', () => {
  it('does not coerce versions when saving a draft', async () => {
    const http = client(),
      api = createStudioApi(http)
    const resource = {
      nId: 'resource/a',
      name: 'Page',
      optimisticVersion: '9007199254740993',
      concurrencyVersion: '11111111-1111-1111-1111-111111111111',
    } as StudioResource
    await api.save(resource, { schemaVersion: 1 })
    expect(http.put).toHaveBeenCalledWith(
      '/api/v1/studio/resources/resource%2Fa/draft',
      expect.objectContaining({
        optimisticVersion: '9007199254740993',
        concurrencyVersion: resource.concurrencyVersion,
        document: { schemaVersion: 1 },
      }),
      expect.objectContaining({ headers: { 'Idempotency-Key': expect.any(String) } }),
    )
  })
  it('uses only the simulation endpoint for a previewed write action', async () => {
    const http = client(),
      api = createStudioApi(http)
    await api.action(
      'page',
      'save',
      { pageDocument: { mode: 'Crud' }, context: { form: { f: 'x' } } },
      true,
      'operation-1',
    )
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/actions/preview',
      expect.objectContaining({ actionId: 'save', context: { form: { f: 'x' } } }),
      { headers: { 'Idempotency-Key': 'operation-1' } },
    )
    expect(vi.mocked(http.post).mock.calls.some(([path]) => path.endsWith('/execute'))).toBe(false)
  })
  it('reuses an operation key without hiding network failures or replaying a write', async () => {
    const http = client(),
      api = createStudioApi(http)
    vi.mocked(http.post).mockRejectedValueOnce(new Error('timeout'))
    await expect(
      api.action('page', 'save', { revisionNId: 'rev', context: { form: {} } }, false, 'same-key'),
    ).rejects.toThrow('timeout')
    expect(http.post).toHaveBeenCalledTimes(1)
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/pages/page/actions/save/execute',
      expect.anything(),
      { headers: { 'Idempotency-Key': 'same-key' } },
    )
  })
  it('shares antiforgery requests and renews the token after identity changes', async () => {
    const raw = client(),
      cookie = studioCookieClient(raw)
    await Promise.all([cookie.post('/one', {}), cookie.put('/two', {})])
    expect(raw.get).toHaveBeenCalledTimes(1)
    await cookie.post('/api/v1/studio/auth/local-login', {})
    await cookie.post('/three', {})
    expect(raw.get).toHaveBeenCalledTimes(2)
    expect(raw.post).toHaveBeenLastCalledWith(
      '/three',
      {},
      expect.objectContaining({ headers: { 'X-Studio-CSRF': 'csrf' } }),
    )
  })
  it('adopts permissions from the trusted principal without creating bearer credentials', () => {
    const session = studioAuthSession({
      principal: {
        subjectNId: 'user',
        tenantNId: 'tenant',
        displayName: 'User',
        roles: [],
        permissions: ['studio.lowcode.run'],
        expiresOn: '2026-10-23T00:00:00Z',
      },
      mustChangePassword: true,
    })
    expect(session.transport).toBe('embedded-cookie')
    expect(session).not.toHaveProperty('accessToken')
    expect(session.user.permissions).toEqual(['studio.lowcode.run'])
    expect(session.user.mustChangePassword).toBe(true)
  })
})
