<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import { createNotificationRealtime, type NotificationRealtime } from '@/api/systemData/notificationHub'
import type { AnnouncementDto, NotificationInboxItemDto } from '@/api/systemData/pf04Types'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { systemDataPageCopy } from '@/localization/systemData'

const localization = useLocalizationStore()
const router = useRouter()
const authStore = useAuthStore()
const copy = computed(() => {
  const page = systemDataPageCopy(localization.locale, 'notificationsManagement')
  const text = (key: string): string => page[key] ?? ''
  return {
    title: text('title'),
    description: text('description'),
    newAnnouncement: text('newAnnouncement'),
    sendSystemMessage: text('sendSystemMessage'),
    announcementQuery: text('announcementQuery'),
    titleColumn: text('titleColumn'),
    status: text('status'),
    recipients: text('recipients'),
    published: text('published'),
    actions: text('actions'),
    edit: text('edit'),
    revoke: text('revoke'),
    myInbox: text('myInbox'),
    markAllRead: text('markAllRead'),
    inboxTitle: text('inboxTitle'),
    kind: text('kind'),
    read: text('read'),
    delivered: text('delivered'),
    markRead: text('markRead'),
    open: text('open'),
    titleLabel: text('titleLabel'),
    bodyLabel: text('bodyLabel'),
    usersLabel: text('usersLabel'),
    usersPlaceholder: text('usersPlaceholder'),
    publish: text('publish'),
    loadFailed: text('loadFailed'),
    operationFailed: text('operationFailed'),
    announcementPublished: text('announcementPublished'),
    draftUpdatedPublished: text('draftUpdatedPublished'),
    draftSaved: text('draftSaved'),
    systemMessageSent: text('systemMessageSent'),
    retry: text('retry'),
  }
})
const announcements = ref<AnnouncementDto[]>([])
const announcementTotal = ref(0)
const inbox = ref<NotificationInboxItemDto[]>([])
const inboxTotal = ref(0)
const search = ref('')
const open = ref(false)
const busy = ref(false)
const form = reactive({ title: '', body: '', recipients: '', mode: 'announcement' as 'announcement' | 'system', publish: true })
const editingId = ref<string | null>(null)
let pollTimer: number | undefined
let realtime: NotificationRealtime | null = null
const errorMessage = ref('')
const loading = ref(false)
const canReadInbox = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationInboxRead))
const canReadAnnouncements = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationAnnouncementRead))
const canManageAnnouncements = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationAnnouncementManage))
const canPublishAnnouncements = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationAnnouncementPublish))
const canSendSystemMessages = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationSystemSend))
const unsafeRouteCharacterPattern = /[\u0000-\u001f\u007f]/
let mutationIdempotencyKey = ''
const columns = computed(() => [
  { field: 'title', title: copy.value.titleColumn, minWidth: 220 },
  { field: 'status', title: copy.value.status, width: 120 },
  { field: 'recipientCount', title: copy.value.recipients, width: 100 },
  { field: 'publishedOn', title: copy.value.published, minWidth: 180 },
  { field: 'actions', title: copy.value.actions, width: 220 },
])
const inboxColumns = computed(() => [
  { field: 'title', title: copy.value.inboxTitle, minWidth: 220 },
  { field: 'kind', title: copy.value.kind, width: 120 },
  { field: 'isRead', title: copy.value.read, width: 100 },
  { field: 'deliveredOn', title: copy.value.delivered, minWidth: 180 },
])
async function load(): Promise<void> {
  const api = getPf04Api()
  if (api === null) return
  loading.value = true
  try {
    const tasks: Promise<unknown>[] = []
    if (canReadAnnouncements.value) {
      tasks.push(loadAnnouncementsPage({ pageIndex: 1, pageSize: 25 } as AppDataTableRequest))
    } else {
      announcements.value = []; announcementTotal.value = 0
    }
    if (canReadInbox.value) {
      tasks.push(loadInboxPage({ pageIndex: 1, pageSize: 25 } as AppDataTableRequest))
    } else {
      inbox.value = []
      inboxTotal.value = 0
    }
    await Promise.all(tasks)
    errorMessage.value = ''
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed
  } finally { loading.value = false }
}

async function loadAnnouncementsPage(request: AppDataTableRequest): Promise<{ items: AnnouncementDto[]; total: number; pageIndex: number; pageSize: number }> {
  const api = getPf04Api()
  if (api === null || !canReadAnnouncements.value) return { items: [], total: 0, pageIndex: request.pageIndex, pageSize: request.pageSize }
  const result = await api.listAnnouncements(search.value, request.pageIndex, request.pageSize)
  announcements.value = result.items
  announcementTotal.value = result.total
  return { items: result.items, total: result.total, pageIndex: result.page, pageSize: result.pageSize }
}

async function loadInboxPage(request: AppDataTableRequest): Promise<{ items: NotificationInboxItemDto[]; total: number; pageIndex: number; pageSize: number }> {
  const api = getPf04Api()
  if (api === null || !canReadInbox.value) return { items: [], total: 0, pageIndex: request.pageIndex, pageSize: request.pageSize }
  const result = await api.getInbox(request.pageIndex, request.pageSize)
  inbox.value = result.items
  inboxTotal.value = result.total
  return { items: result.items, total: result.total, pageIndex: result.page, pageSize: result.pageSize }
}
function reset(): void { search.value = ''; void load() }
async function create(): Promise<void> {
  const api = getPf04Api()
  if (api === null || busy.value) return
  if (form.mode === 'system' && !canSendSystemMessages.value) return
  if (form.mode === 'announcement' && !canManageAnnouncements.value) return
  busy.value = true
  try {
    const recipients = form.recipients.split(',').map((value) => value.trim()).filter(Boolean)
    const publish = form.publish && canPublishAnnouncements.value
    if (form.mode === 'system') {
      await api.sendSystemMessage({ title: form.title, body: form.body, recipientUserNIds: recipients, idempotencyKey: mutationIdempotencyKey })
      ElMessage.success(copy.value.systemMessageSent)
    } else if (editingId.value !== null) {
      await api.updateAnnouncement(editingId.value, { title: form.title, body: form.body, ...(recipients.length ? { recipientUserNIds: recipients } : {}) })
      if (publish) await api.publishAnnouncement(editingId.value)
      ElMessage.success(publish ? copy.value.draftUpdatedPublished : copy.value.draftSaved)
    } else {
      const announcement = await api.createAnnouncement({ title: form.title, body: form.body, recipientUserNIds: recipients, idempotencyKey: mutationIdempotencyKey })
      if (publish) await api.publishAnnouncement(announcement.announcementNId)
      ElMessage.success(publish ? copy.value.announcementPublished : copy.value.draftSaved)
    }
    open.value = false; editingId.value = null; Object.assign(form, { title: '', body: '', recipients: '', mode: 'announcement', publish: true }); await load()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : copy.value.operationFailed
    ElMessage.error(errorMessage.value)
    open.value = true
  } finally { busy.value = false }
}
async function markRead(item: NotificationInboxItemDto): Promise<void> { const api = getPf04Api(); if (api !== null && canReadInbox.value && !item.isRead) { await api.markRead(item.notificationNId); await load() } }
async function revoke(row: AnnouncementDto): Promise<void> { const api = getPf04Api(); if (api !== null && canManageAnnouncements.value && row.status === 'Published') { await api.revokeAnnouncement(row.announcementNId); await load() } }
async function confirmRevoke(row: AnnouncementDto): Promise<void> {
  if (!canManageAnnouncements.value || row.status !== 'Published') return
  try {
    await ElMessageBox.confirm('撤回后该公告不再作为有效通知继续展示，确认撤回吗？', '撤回公告确认', { type: 'warning' })
    await revoke(row)
  } catch { /* 用户取消或业务失败时保留当前页面状态 */ }
}
async function markAllRead(): Promise<void> { const api = getPf04Api(); if (api !== null && canReadInbox.value) { const ids = inbox.value.filter((item) => !item.isRead).map((item) => item.notificationNId); if (ids.length) { await api.batchRead(ids); await load() } } }
function openAnnouncement(): void { if (!canManageAnnouncements.value) return; mutationIdempotencyKey = `ui-${crypto.randomUUID?.() ?? Date.now()}`; form.mode = 'announcement'; editingId.value = null; open.value = true }
function openSystemMessage(): void { if (!canSendSystemMessages.value) return; mutationIdempotencyKey = `ui-${crypto.randomUUID?.() ?? Date.now()}`; form.mode = 'system'; editingId.value = null; open.value = true }
function edit(row: AnnouncementDto): void { mutationIdempotencyKey = `ui-${crypto.randomUUID?.() ?? Date.now()}`; editingId.value = row.announcementNId; Object.assign(form, { title: row.title, body: row.body, recipients: '', mode: 'announcement', publish: false }); open.value = true }
function handleAnnouncementAction(row: AnnouncementDto, command: string | number | object): void { if (command === 'edit') edit(row); if (command === 'revoke') void confirmRevoke(row) }
defineExpose({ confirmRevoke })
function safeNotificationRoute(route: string | null | undefined): string | null { if (route === undefined || route === null || !route.startsWith('/') || route.startsWith('//') || unsafeRouteCharacterPattern.test(route)) return null; return route }
function openTarget(route: string | null | undefined): void { const safeRoute = safeNotificationRoute(route); if (safeRoute !== null) void router.push(safeRoute) }
onMounted(() => {
  void load()
  if (canReadInbox.value) {
    realtime = createNotificationRealtime({ getAccessToken: () => authStore.session?.accessToken ?? null, onRefresh: load })
    void realtime.start()
    pollTimer = window.setInterval(() => { if (document.visibilityState === 'visible') void load() }, 15000)
  }
})
onBeforeUnmount(() => { if (pollTimer !== undefined) window.clearInterval(pollTimer); if (realtime !== null) void realtime.stop() })
</script>

<template>
  <AppPage :title="copy.title" :description="copy.description">
    <template #actions><PermissionGate :permission-n-id="PERMISSIONS.systemDataNotificationAnnouncementManage"><el-button type="primary" data-testid="notification-new-announcement" @click="openAnnouncement">{{ copy.newAnnouncement }}</el-button></PermissionGate><PermissionGate :permission-n-id="PERMISSIONS.systemDataNotificationSystemSend"><el-button data-testid="notification-send-system" @click="openSystemMessage">{{ copy.sendSystemMessage }}</el-button></PermissionGate></template>
    <AppQueryPanel :title="copy.announcementQuery" show-actions @submit="load" @reset="reset"><el-input v-model="search" clearable /></AppQueryPanel>
    <p v-if="errorMessage" class="pf04-error" role="alert">{{ errorMessage }} <el-button link type="danger" @click="load">{{ copy.retry }}</el-button></p>
    <AppDataTable table-key="systemdata-announcements" route-key="systemdata-notifications" row-key="announcementNId" :rows="announcements" :total="announcementTotal" :columns="columns" :loading="loading" :loader="loadAnnouncementsPage"><template #cell-actions="{ row }"><ElDropdown v-if="canManageAnnouncements && (row.status === 'Draft' || row.status === 'Published')" trigger="click" @command="(command) => handleAnnouncementAction(row, command)"><el-button link data-testid="notification-row-more">{{ copy.actions }}</el-button><template #dropdown><ElDropdownMenu><ElDropdownItem v-if="row.status === 'Draft'" command="edit">{{ copy.edit }}</ElDropdownItem><ElDropdownItem v-if="row.status === 'Published'" command="revoke" divided>{{ copy.revoke }}</ElDropdownItem></ElDropdownMenu></template></ElDropdown></template></AppDataTable>
    <template v-if="canReadInbox"><h2 class="pf04-section-title">{{ copy.myInbox }}</h2><el-button v-if="inbox.some((item) => !item.isRead)" @click="markAllRead">{{ copy.markAllRead }}</el-button><AppDataTable table-key="systemdata-notification-inbox" route-key="systemdata-notifications" row-key="notificationNId" :rows="inbox" :total="inboxTotal" :columns="inboxColumns" :loading="loading" :loader="loadInboxPage"><template #cell-isRead="{ row }"><el-button v-if="!row.isRead" link type="primary" @click="markRead(row)">{{ copy.markRead }}</el-button><el-button v-if="safeNotificationRoute(row.targetRoute) !== null" link @click="openTarget(row.targetRoute)">{{ copy.open }}</el-button><span v-else>✓</span></template></AppDataTable></template>
  </AppPage>
  <AppFormDrawer v-model="open" :busy="busy" :title="form.mode === 'system' ? copy.sendSystemMessage : (editingId ? copy.edit : copy.newAnnouncement)" @submit="create"><el-form label-width="100px"><el-form-item :label="copy.titleLabel"><el-input v-model="form.title" /></el-form-item><el-form-item :label="copy.bodyLabel"><el-input v-model="form.body" type="textarea" :rows="6" /></el-form-item><el-form-item :label="copy.usersLabel"><el-input v-model="form.recipients" :placeholder="copy.usersPlaceholder" /></el-form-item><el-checkbox v-if="form.mode === 'announcement' && canPublishAnnouncements" v-model="form.publish">{{ copy.publish }}</el-checkbox></el-form></AppFormDrawer>
</template>

<style scoped>.pf04-section-title { margin: 24px 0 12px; color: var(--ip-color-text-primary); }</style>
