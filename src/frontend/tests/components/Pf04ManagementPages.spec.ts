import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'

import FileManagementPage from '@/pages/pc/systemData/FileManagementPage.vue'
import NotificationManagementPage from '@/pages/pc/systemData/NotificationManagementPage.vue'
import AuditManagementPage from '@/pages/pc/systemData/AuditManagementPage.vue'
import { registerPf04Api } from '@/api/systemData/pf04Registry'
import type { Pf04Api } from '@/api/systemData/pf04Types'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { persistAuthSession } from '../fixtures/session'

const AppPageStub = defineComponent({ setup(_, { slots }) { return () => h('div', [slots.actions?.(), slots.default?.()]) } })
const AppQueryPanelStub = defineComponent({ emits: ['submit', 'reset'], setup(_, { slots, emit }) { return () => h('section', [slots.default?.(), h('button', { 'data-testid': 'query-submit', onClick: () => emit('submit') }, '查询'), h('button', { 'data-testid': 'query-reset', onClick: () => emit('reset') }, '重置')]) } })
const AppFormDrawerStub = defineComponent({ props: { modelValue: Boolean, busy: Boolean, title: String }, emits: ['update:modelValue', 'submit'], setup(props, { slots, emit }) { return () => props.modelValue ? h('section', { 'data-testid': 'form-drawer' }, [slots.default?.(), h('button', { 'data-testid': 'form-submit', disabled: props.busy, onClick: () => emit('submit') }, '提交')]) : null } })
const AppDataTableStub = defineComponent({
  props: { rows: { type: Array, default: () => [] }, loader: { type: Function }, columns: { type: Array, default: () => [] } },
  setup(props, { slots }) {
    return () => h('section', { 'data-testid': 'data-table' }, [
      h('button', { 'data-testid': 'table-next', onClick: () => props.loader?.({ pageIndex: 2, pageSize: 50, queryMode: 'top', filters: {}, columns: [] }) }, '下一页'),
      ...(props.rows as Record<string, unknown>[]).map((row) => h('article', { key: String(row.announcementNId ?? row.fileNId ?? row.rowKey ?? row.notificationNId) }, [
        h('span', { 'data-testid': 'row-title' }, String(row.title ?? row.fileName ?? row.action ?? '')),
        slots['cell-actions']?.({ row }),
        slots['cell-isRead']?.({ row }),
      ])),
    ])
  },
})

const baseGlobal = (pinia: ReturnType<typeof createPinia>, router: ReturnType<typeof createRouter>) => ({
  plugins: [ElementPlus, pinia, router],
  stubs: { AppPage: AppPageStub, AppQueryPanel: AppQueryPanelStub, AppFormDrawer: AppFormDrawerStub, AppDataTable: AppDataTableStub },
})

async function mountPage(component: typeof FileManagementPage | typeof NotificationManagementPage | typeof AuditManagementPage, permissions: string[], router?: ReturnType<typeof createRouter>) {
  const pinia = createPinia()
  setActivePinia(pinia)
  sessionStorage.clear()
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const activeRouter = router ?? createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }] })
  if (router === undefined) { await activeRouter.push('/'); await activeRouter.isReady() }
  return mount(component, { global: baseGlobal(pinia, activeRouter) })
}

const filePage = { items: [{ tenantNId: 't1', fileNId: 'F-1', fileName: 'a.txt', contentType: 'text/plain', length: 2, sha256: 'hash', scanStatus: 'Clean', restricted: false, purpose: 'systemdata', ownerUserNId: 'u1', referenceCount: 1, referenceSummary: [{ referenceNId: 'R-1', ownerUserNId: 'u2', purpose: 'report', createdOn: '2026-08-30T01:00:00Z' }], deletionStatus: 'Active', createdOn: '2026-08-30T01:00:00Z', retentionUntil: null }], page: 1, pageSize: 25, total: 1 }
const announcementPage = { items: [{ tenantNId: 't1', announcementNId: 'A-1', title: '公告', body: '正文', priority: 1, status: 'Draft', recipientCount: 1, publishedOn: null, expiresOn: null, targetRoute: '/safe' }], page: 1, pageSize: 25, total: 1 }
const inboxPage = { items: [{ notificationNId: 'N-1', kind: 'System', title: '通知', body: '正文', senderUserNId: 'u1', isRead: false, deliveredOn: '2026-08-30T01:00:00Z', readOn: null, expiresOn: null, targetRoute: '/safe' }, { notificationNId: 'N-2', kind: 'System', title: '危险通知', body: '正文', senderUserNId: 'u1', isRead: true, deliveredOn: '2026-08-30T02:00:00Z', readOn: '2026-08-30T03:00:00Z', expiresOn: null, targetRoute: '/bad\u0000route' }], page: 1, pageSize: 25, total: 2, unreadCount: 1 }
const auditPage = { items: [{ tenantNId: 't1', producerServiceKey: 'systemdata', auditEventNId: 'E-1', occurredOn: '2026-08-30T01:00:00Z', receivedOn: '2026-08-30T01:00:00Z', actorUserNId: 'u1', action: 'file.upload.complete', objectType: 'File', objectNId: 'F-1', payloadJson: '{}', traceId: 'trace', severity: 'Info' }], page: 1, pageSize: 25, total: 1 }

describe('PF-04 PC management pages', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>

  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    api = {
      listFiles: vi.fn().mockResolvedValue(filePage),
      setFileRestriction: vi.fn().mockResolvedValue(filePage.items[0]),
      requestFileDeletion: vi.fn().mockResolvedValue(filePage.items[0]),
      listAnnouncements: vi.fn().mockResolvedValue(announcementPage),
      getInbox: vi.fn().mockResolvedValue(inboxPage),
      markRead: vi.fn().mockResolvedValue(undefined),
      batchRead: vi.fn().mockResolvedValue(undefined),
      createAnnouncement: vi.fn().mockResolvedValue(announcementPage.items[0]),
      updateAnnouncement: vi.fn().mockResolvedValue(announcementPage.items[0]),
      publishAnnouncement: vi.fn().mockResolvedValue(announcementPage.items[0]),
      revokeAnnouncement: vi.fn().mockResolvedValue(announcementPage.items[0]),
      sendSystemMessage: vi.fn().mockResolvedValue(inboxPage.items[0]),
      listAudits: vi.fn().mockResolvedValue(auditPage),
      getAudit: vi.fn().mockResolvedValue(auditPage.items[0]),
      exportAudits: vi.fn().mockResolvedValue(new Blob(['csv'])),
      updateAuditLifecycle: vi.fn().mockResolvedValue(undefined),
    }
    registerPf04Api(api as unknown as Pf04Api)
  })

  afterEach(() => vi.unstubAllEnvs())

  it('denies file reads without calling the file API and uses server page/filter parameters when allowed', async () => {
    const denied = await mountPage(FileManagementPage, [])
    await flushPromises()
    expect(api.listFiles).not.toHaveBeenCalled()
    denied.unmount()

    const wrapper = await mountPage(FileManagementPage, [PERMISSIONS.systemDataFileRead])
    await flushPromises()
    expect(api.listFiles).toHaveBeenCalledWith('', 1, 25, '', '', '', undefined)
    await wrapper.get('[data-testid="table-next"]').trigger('click')
    await flushPromises()
    expect(api.listFiles).toHaveBeenCalledWith('', 2, 50, '', '', '', undefined)
    expect(wrapper.get('[data-testid="row-title"]').text()).toContain('a.txt')
  })

  it('renders file load errors with a working retry action', async () => {
    api.listFiles!.mockRejectedValueOnce(new Error('file down')).mockResolvedValueOnce(filePage)
    const wrapper = await mountPage(FileManagementPage, [PERMISSIONS.systemDataFileRead])
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('file down')
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(api.listFiles).toHaveBeenCalledTimes(2)
  })

  it('splits notification announcement and inbox permissions and paginates each server source', async () => {
    const denied = await mountPage(NotificationManagementPage, [])
    await flushPromises()
    expect(api.listAnnouncements).not.toHaveBeenCalled()
    expect(api.getInbox).not.toHaveBeenCalled()
    denied.unmount()

    const wrapper = await mountPage(NotificationManagementPage, [PERMISSIONS.systemDataNotificationAnnouncementRead])
    await flushPromises()
    expect(api.listAnnouncements).toHaveBeenCalledWith('', 1, 25)
    expect(api.getInbox).not.toHaveBeenCalled()
    await wrapper.get('[data-testid="table-next"]').trigger('click')
    await flushPromises()
    expect(api.listAnnouncements).toHaveBeenCalledWith('', 2, 50)
  })

  it('sends a system message with recipients and hides unsafe inbox targets', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
    await router.push('/')
    await router.isReady()
    const wrapper = await mountPage(NotificationManagementPage, [PERMISSIONS.systemDataNotificationAnnouncementRead, PERMISSIONS.systemDataNotificationInboxRead, PERMISSIONS.systemDataNotificationSystemSend], router)
    await flushPromises()
    expect(wrapper.findAll('button').filter((button) => button.text().includes('打开'))).toHaveLength(1)
    await wrapper.get('[data-testid="notification-send-system"]').trigger('click')
    await wrapper.findAll('input').at(1)!.setValue('系统提醒')
    await wrapper.find('textarea').setValue('正文')
    await wrapper.find('[data-testid="form-submit"]').trigger('click')
    await flushPromises()
    expect(api.sendSystemMessage).toHaveBeenCalledWith(expect.objectContaining({ title: '系统提醒', body: '正文', recipientUserNIds: [] }))
  })

  it('requires business confirmation before file restriction/deletion and announcement revoke', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as never)
    const fileWrapper = await mountPage(FileManagementPage, [PERMISSIONS.systemDataFileRead, PERMISSIONS.systemDataFileManage, PERMISSIONS.systemDataFileDelete])
    await flushPromises()
    const fileRow = filePage.items[0]!
    const fileVm = fileWrapper.vm as unknown as { confirmRestriction: (row: typeof fileRow) => Promise<void>; confirmDeletion: (row: typeof fileRow) => Promise<void> }
    await fileVm.confirmRestriction(fileRow)
    await fileVm.confirmDeletion(fileRow)
    expect(confirm).toHaveBeenCalledTimes(2)
    expect(api.setFileRestriction).toHaveBeenCalledWith('F-1', true)
    expect(api.requestFileDeletion).toHaveBeenCalledWith('F-1')
    fileWrapper.unmount()

    const notificationWrapper = await mountPage(NotificationManagementPage, [PERMISSIONS.systemDataNotificationAnnouncementRead, PERMISSIONS.systemDataNotificationAnnouncementManage])
    await flushPromises()
    const published = { ...announcementPage.items[0]!, status: 'Published' }
    const notificationVm = notificationWrapper.vm as unknown as { confirmRevoke: (row: typeof published) => Promise<void> }
    await notificationVm.confirmRevoke(published)
    expect(confirm).toHaveBeenCalledTimes(3)
    expect(api.revokeAnnouncement).toHaveBeenCalledWith('A-1')
  })

  it('denies audit reads, supports server pagination, retry, export, and lifecycle payload', async () => {
    const denied = await mountPage(AuditManagementPage, [])
    await flushPromises()
    expect(api.listAudits).not.toHaveBeenCalled()
    denied.unmount()

    const wrapper = await mountPage(AuditManagementPage, [PERMISSIONS.systemDataAuditRead, PERMISSIONS.systemDataAuditExport, PERMISSIONS.systemDataAuditRetentionManage])
    await flushPromises()
    expect(api.listAudits).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 25 }))
    expect(wrapper.text()).toContain('导出 CSV')
    await wrapper.get('[data-testid="table-next"]').trigger('click')
    await flushPromises()
    expect(api.listAudits).toHaveBeenCalledWith(expect.objectContaining({ page: 2, pageSize: 50 }))
    const more = wrapper.findAll('button').find((button) => button.text().includes('更多'))
    expect(more).toBeDefined()
    await more!.trigger('click')
    await flushPromises()
    const lifecycle = Array.from(document.body.querySelectorAll('.el-dropdown-menu__item')).find((button) => button.textContent?.includes('生命周期'))
    expect(lifecycle).toBeDefined()
    ;(wrapper.vm as unknown as { openLifecycle: (row: unknown) => void }).openLifecycle({ ...auditPage.items[0], rowKey: 'systemdata:E-1' })
    await flushPromises()
    await vi.waitFor(() => expect(wrapper.find('.el-dialog').exists()).toBe(true))
    const save = wrapper.findAll('button').find((button) => button.text().includes('保存'))
    expect(save).toBeDefined()
    await save!.trigger('click')
    await flushPromises()
    expect(api.updateAuditLifecycle).toHaveBeenCalledWith('systemdata', 'E-1', expect.objectContaining({ state: 'Archived', legalHold: false }))
  })
})
