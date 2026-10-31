import { defineComponent, h, ref, watch } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import WorkbookPage from '@/pages/pc/studio/report/WorkbookPage.vue'

const state = vi.hoisted(() => ({
  canEdit: true,
  deferDrawer: false,
  revealDrawer: undefined as undefined | (() => void),
  leave: undefined as undefined | (() => Promise<boolean>),
  reload: vi.fn(),
  api: {
    workbooks: vi.fn(),
    workbookSession: vi.fn(),
    closeWorkbookSession: vi.fn(),
    workbookVersions: vi.fn(),
    renewWorkbookSession: vi.fn(),
    copyWorkbook: vi.fn(),
    workbookState: vi.fn(),
    editWorkbook: vi.fn(),
    saveWorkbookVersion: vi.fn(),
  },
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: (guard: () => Promise<boolean>) => {
    state.leave = guard
  },
}))
vi.mock('@/stores/authStore', () => ({
  useAuthStore: () => ({ hasPermission: () => state.canEdit }),
}))
const books = [
  { nId: 'a', name: 'Book A', contentVersion: '1', isArchived: false },
  { nId: 'b', name: 'Book B', contentVersion: '2', isArchived: false },
]
const Table = defineComponent({
  props: ['tableKey', 'rows'],
  setup(props, { slots, expose }) {
    expose({ reload: state.reload })
    return () =>
      h('section', [
        slots['toolbar-actions']?.(),
        ...(props.tableKey === 'studio.workbooks'
          ? books.map((row) => h('div', { 'data-book': row.nId }, slots.actions?.({ row })))
          : [h('output', JSON.stringify(props.rows))]),
      ])
  },
})
const Drawer = defineComponent({
  props: ['modelValue'],
  emits: ['update:modelValue', 'submit'],
  setup(props, { slots, emit }) {
    const contentReady = ref(false)
    state.revealDrawer = () => {
      contentReady.value = true
    }
    watch(
      () => props.modelValue,
      (open) => {
        contentReady.value = !!open && !state.deferDrawer
      },
    )
    return () =>
      props.modelValue
        ? h('aside', [
            h(
              'button',
              { class: 'close', onClick: () => emit('update:modelValue', false) },
              'Close',
            ),
            contentReady.value ? slots.default?.() : null,
            h('button', { class: 'submit', onClick: () => emit('submit') }, 'Submit'),
          ])
        : null
  },
})
let page: VueWrapper | undefined
const session = (mode = 'Edit') => ({
  sessionNId: 'session-a',
  editorUrl: 'https://office.example.test/edit',
  accessToken: 'fixture-token',
  contentVersion: '1',
  mode,
  expiresOn: '2099-01-01T00:00:00Z',
})
const action = (book: string, label: string) =>
  page!.findAll(`[data-book="${book}"] button`).find((button) => button.text() === label)!
beforeEach(() => {
  vi.clearAllMocks()
  state.canEdit = true
  state.deferDrawer = false
  state.revealDrawer = undefined
  state.reload.mockResolvedValue(undefined)
  state.api.workbookSession.mockResolvedValue(session())
  state.api.closeWorkbookSession.mockResolvedValue({})
  state.api.workbookVersions.mockResolvedValue([])
  state.api.workbookState.mockResolvedValue({
    nId: 'a',
    contentVersion: '2',
    historyVersion: '1',
    hasWorkingCopy: true,
  })
  vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(
    'confirm' as Awaited<ReturnType<typeof ElMessageBox.confirm>>,
  )
  page = mount(WorkbookPage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: {
        AppDataTable: Table,
        AppFormDrawer: Drawer,
        AppPage: { template: '<main><slot /></main>' },
        StudioFileField: true,
        ElDropdown: {
          template: '<div><slot /><slot name="dropdown" /></div>',
        },
        ElDropdownMenu: { template: '<div><slot /></div>' },
        ElDropdownItem: { template: '<button><slot /></button>' },
      },
    },
  })
})
afterEach(() => {
  page?.unmount()
  page = undefined
  vi.restoreAllMocks()
  vi.useRealTimers()
})

it('loads workbooks on initial entry without a manual refresh', () => {
  expect(state.reload).toHaveBeenCalledOnce()
})

it('submits the Office form when a lazy drawer mounts its form and frame', async () => {
  state.deferDrawer = true
  await action('a', '打开').trigger('click')
  await flushPromises()
  expect(page!.find('iframe').exists()).toBe(false)
  expect(HTMLFormElement.prototype.submit).not.toHaveBeenCalled()
  state.revealDrawer!()
  await flushPromises()
  expect(page!.find('iframe').exists()).toBe(true)
  expect(HTMLFormElement.prototype.submit).toHaveBeenCalledOnce()
})

it('keeps a non-rotating Office session alive without a provider countdown or iframe reload', async () => {
  vi.useFakeTimers()
  const expiresOn = new Date(Date.now() + 240000).toISOString()
  state.api.workbookSession.mockResolvedValue({
    ...session(),
    expiresOn,
    slidingSession: true,
    supportsTokenRefresh: false,
  })
  state.api.renewWorkbookSession.mockResolvedValue({
    accessToken: null,
    expiresOn: new Date(Date.now() + 900000).toISOString(),
  })
  await action('a', '打开').trigger('click')
  await flushPromises()
  expect(page!.findComponent(Drawer).attributes('size')).toBe('fullscreen')
  expect(page!.get('input[name="access_token_ttl"]').attributes('value')).toBe('0')
  await vi.advanceTimersByTimeAsync(10000)
  await flushPromises()
  expect(state.api.renewWorkbookSession).toHaveBeenCalledWith('a', 'session-a')
  expect(page!.get('input[name="access_token"]').attributes('value')).toBe('fixture-token')
  expect(HTMLFormElement.prototype.submit).toHaveBeenCalledOnce()
})

it('opens a read-only session for a viewer and reloads the list after closing', async () => {
  state.canEdit = false
  state.api.workbookSession.mockResolvedValue(session('View'))
  await action('a', '打开').trigger('click')
  await flushPromises()
  expect(state.api.workbookSession).toHaveBeenCalledWith('a', {
    mode: 'View',
    baseContentVersion: '1',
  })
  expect(page!.find('iframe').exists()).toBe(true)
  await page!.get('.close').trigger('click')
  await flushPromises()
  expect(ElMessageBox.confirm).not.toHaveBeenCalled()
  expect(state.api.closeWorkbookSession).toHaveBeenCalledWith('a', 'session-a')
  expect(state.reload).toHaveBeenCalledTimes(2)
  expect(page!.find('iframe').exists()).toBe(false)
})

it('releases a late session after navigation and never mounts or polls the departed editor', async () => {
  let resolve!: (value: ReturnType<typeof session>) => void
  state.api.workbookSession.mockImplementation(
    () =>
      new Promise((done) => {
        resolve = done
      }),
  )
  await action('a', '打开').trigger('click')
  expect(action('b', '版本历史').attributes('disabled')).toBeDefined()
  expect(await state.leave!()).toBe(true)
  resolve(session())
  await flushPromises()
  expect(state.api.closeWorkbookSession).toHaveBeenCalledWith('a', 'session-a')
  expect(page!.find('iframe').exists()).toBe(false)
  expect(state.api.workbookVersions).not.toHaveBeenCalled()
})

async function officeMessage(
  MessageId: string,
  Values: Record<string, unknown>,
  origin = 'https://office.example.test',
) {
  const frame = page!.get('iframe').element as HTMLIFrameElement
  window.dispatchEvent(
    new MessageEvent('message', {
      data: JSON.stringify({ MessageId, Values }),
      source: frame.contentWindow,
      origin,
    }),
  )
  await flushPromises()
}

it('closes a saved workbook without the unconditional unsaved prompt', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('Doc_ModifiedStatus', { Modified: true })
  await officeMessage('UI_Save', {})
  await officeMessage('Doc_ModifiedStatus', { Modified: false })
  await officeMessage('Action_Save_Resp', { success: true })
  await page!.get('.close').trigger('click')
  await flushPromises()
  expect(state.api.closeWorkbookSession).not.toHaveBeenCalled()
  await officeMessage('Action_Save_Resp', { success: false, result: 'unmodified' })
  expect(ElMessageBox.confirm).not.toHaveBeenCalled()
  expect(state.api.closeWorkbookSession).toHaveBeenCalledOnce()
  expect(page!.find('iframe').exists()).toBe(false)
})

it('edits workbook metadata without creating a history version', async () => {
  await action('a', '编辑').trigger('click')
  await flushPromises()
  await page!.get('input').setValue('Renamed workbook')
  await page!.get('.submit').trigger('click')
  await flushPromises()
  expect(state.api.editWorkbook).toHaveBeenCalledWith(
    'a',
    expect.objectContaining({ name: 'Renamed workbook' }),
  )
  expect(state.api.saveWorkbookVersion).not.toHaveBeenCalled()
  expect(page!.find('aside').exists()).toBe(false)
})

it('saves Office content before checkpointing and uses the acknowledged revision', async () => {
  state.api.saveWorkbookVersion.mockResolvedValue({
    nId: 'a',
    contentVersion: '2',
    historyVersion: '2',
    hasWorkingCopy: false,
  })
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('Doc_ModifiedStatus', { Modified: true })
  await page!
    .findAll('aside button')
    .find((b) => b.text() === '保存版本')!
    .trigger('click')
  await flushPromises()
  expect(state.api.saveWorkbookVersion).not.toHaveBeenCalled()
  await officeMessage('Action_Save_Resp', { success: true })
  expect(state.api.saveWorkbookVersion).toHaveBeenCalledWith('a', {
    baseContentVersion: '2',
    note: '',
  })
  expect(page!.find('iframe').exists()).toBe(true)
})

it('never creates a checkpoint when Office saving fails', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await page!
    .findAll('aside button')
    .find((b) => b.text() === '保存版本')!
    .trigger('click')
  await officeMessage('Action_Save_Resp', { success: false, result: 'failed' })
  expect(state.api.saveWorkbookVersion).not.toHaveBeenCalled()
  expect(page!.find('iframe').exists()).toBe(true)
  expect(page!.text()).toContain('保存未成功')
})

it('does not let a late save state response overwrite a different workbook session', async () => {
  let resolveOld!: (value: unknown) => void
  state.api.workbookState
    .mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolveOld = done
        }),
    )
    .mockResolvedValueOnce({
      nId: 'a',
      contentVersion: '2',
      historyVersion: '1',
      hasWorkingCopy: true,
    })
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('UI_Save', {})
  await officeMessage('Action_Save_Resp', { success: true })
  await page!.get('.close').trigger('click')
  await officeMessage('Action_Save_Resp', { result: 'unmodified' })
  expect(page!.find('iframe').exists()).toBe(false)
  state.api.workbookSession.mockResolvedValue({
    ...session(),
    sessionNId: 'session-b',
    historyVersion: '4',
    contentVersion: '9',
  })
  await action('b', '打开').trigger('click')
  await flushPromises()
  resolveOld({ nId: 'a', contentVersion: '900', historyVersion: '900', hasWorkingCopy: true })
  await flushPromises()
  expect(page!.text()).toContain('历史版本: 4')
  expect(page!.text()).not.toContain('历史版本: 900')
})

it('confirms dirty edits, saves before closing, and retains the editor on failure', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('Doc_ModifiedStatus', { Modified: true })
  await page!.get('.close').trigger('click')
  await flushPromises()
  expect(ElMessageBox.confirm).toHaveBeenCalledOnce()
  expect(state.api.closeWorkbookSession).not.toHaveBeenCalled()
  await officeMessage('Action_Save_Resp', { success: false, result: 'failed' })
  expect(state.api.closeWorkbookSession).not.toHaveBeenCalled()
  expect(page!.find('iframe').exists()).toBe(true)
  expect(page!.text()).toContain('保存未成功')
})

it('does not let a save acknowledgement hide edits made while saving', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('UI_Save', {})
  await officeMessage('Doc_ModifiedStatus', { Modified: true })
  await officeMessage('Action_Save_Resp', { success: true })
  expect(page!.text()).toContain('保存期间又有新修改')
  expect(state.api.workbookState).not.toHaveBeenCalled()
})

it('does not trust save acknowledgements from another origin', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  await officeMessage('App_LoadingStatus', { Status: 'Document_Loaded' })
  await officeMessage('Doc_ModifiedStatus', { Modified: true })
  await officeMessage('UI_Save', {})
  await officeMessage('Action_Save_Resp', { success: true }, 'https://untrusted.example.test')
  expect(state.api.workbookState).not.toHaveBeenCalled()
  await officeMessage('Action_Save_Resp', { success: true })
  expect(state.api.workbookState).toHaveBeenCalledOnce()
})

it('keeps an editor open on close failure and retries against its original workbook', async () => {
  await action('a', '打开').trigger('click')
  await flushPromises()
  state.api.closeWorkbookSession.mockRejectedValueOnce(new Error('Close unavailable'))
  await page!.get('.close').trigger('click')
  await flushPromises()
  expect(page!.text()).toContain('Close unavailable')
  expect(page!.find('iframe').exists()).toBe(true)
  await page!.get('.close').trigger('click')
  await flushPromises()
  expect(state.api.closeWorkbookSession.mock.calls).toEqual([
    ['a', 'session-a'],
    ['a', 'session-a'],
  ])
  expect(page!.find('iframe').exists()).toBe(false)
  expect(state.reload).toHaveBeenCalledTimes(2)
})

it('clears previous workbook versions before a new history request fails', async () => {
  state.api.workbookVersions.mockResolvedValueOnce([{ nId: 'version-a', contentVersion: '1' }])
  await action('a', '版本历史').trigger('click')
  await flushPromises()
  expect(page!.text()).toContain('version-a')
  await page!.get('.close').trigger('click')
  await flushPromises()
  state.api.workbookVersions.mockRejectedValueOnce(new Error('Versions unavailable'))
  await action('b', '版本历史').trigger('click')
  await flushPromises()
  expect(page!.text()).toContain('Versions unavailable')
  expect(page!.text()).not.toContain('version-a')
})

it('reuses the copy operation key after pending scan but changes it for changed input', async () => {
  state.api.copyWorkbook.mockRejectedValue(new Error('WORKBOOK_SCAN_PENDING'))
  await action('a', '另存工作簿').trigger('click')
  await page!.get('.submit').trigger('click')
  await flushPromises()
  await page!.get('.submit').trigger('click')
  await flushPromises()
  expect(state.api.copyWorkbook).toHaveBeenCalledTimes(2)
  expect(state.api.copyWorkbook.mock.calls[0]![2]).toBeTruthy()
  expect(state.api.copyWorkbook.mock.calls[1]![2]).toBe(state.api.copyWorkbook.mock.calls[0]![2])
  await page!.get('input').setValue('New copy name')
  await page!.get('.submit').trigger('click')
  await flushPromises()
  expect(state.api.copyWorkbook.mock.calls[2]![2]).not.toBe(
    state.api.copyWorkbook.mock.calls[0]![2],
  )
})
