<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import { createNotificationRealtime, type NotificationRealtime } from '@/api/systemData/notificationHub'
import type { AnnouncementDto, NotificationInboxItemDto } from '@/api/systemData/pf04Types'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { systemDataPageCopy } from '@/localization/systemData'

const localization = useLocalizationStore()
const router = useRouter()
const api = getPf04Api()
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
  }
})
const announcements = ref<AnnouncementDto[]>([])
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
  if (api === null) return
  try {
    announcements.value = await api.listAnnouncements(search.value)
    const inboxPage = await api.getInbox()
    inbox.value = inboxPage.items
    inboxTotal.value = inboxPage.total
    errorMessage.value = ''
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed
  }
}
function reset(): void { search.value = ''; void load() }
async function create(): Promise<void> {
  if (api === null || busy.value) return
  busy.value = true
  try {
    const recipients = form.recipients.split(',').map((value) => value.trim()).filter(Boolean)
    if (form.mode === 'system') {
      await api.sendSystemMessage({ title: form.title, body: form.body, recipientUserNIds: recipients, idempotencyKey: `ui-${Date.now()}` })
      ElMessage.success(copy.value.systemMessageSent)
    } else if (editingId.value !== null) {
      await api.updateAnnouncement(editingId.value, { title: form.title, body: form.body, ...(recipients.length ? { recipientUserNIds: recipients } : {}) })
      if (form.publish) await api.publishAnnouncement(editingId.value)
      ElMessage.success(form.publish ? copy.value.draftUpdatedPublished : copy.value.draftSaved)
    } else {
      const announcement = await api.createAnnouncement({ title: form.title, body: form.body, recipientUserNIds: recipients, idempotencyKey: `ui-${Date.now()}` })
      if (form.publish) await api.publishAnnouncement(announcement.announcementNId)
      ElMessage.success(form.publish ? copy.value.announcementPublished : copy.value.draftSaved)
    }
    open.value = false; editingId.value = null; Object.assign(form, { title: '', body: '', recipients: '', mode: 'announcement', publish: true }); await load()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : copy.value.operationFailed
    ElMessage.error(errorMessage.value)
    open.value = true
  } finally { busy.value = false }
}
async function markRead(item: NotificationInboxItemDto): Promise<void> { if (api !== null && !item.isRead) { await api.markRead(item.notificationNId); await load() } }
async function revoke(row: AnnouncementDto): Promise<void> { if (api !== null && row.status === 'Published') { await api.revokeAnnouncement(row.announcementNId); await load() } }
async function markAllRead(): Promise<void> { if (api !== null) { const ids = inbox.value.filter((item) => !item.isRead).map((item) => item.notificationNId); if (ids.length) { await api.batchRead(ids); await load() } } }
function edit(row: AnnouncementDto): void { editingId.value = row.announcementNId; Object.assign(form, { title: row.title, body: row.body, recipients: '', mode: 'announcement', publish: false }); open.value = true }
function openTarget(route: string | null | undefined): void { if (route && route.startsWith('/') && !route.startsWith('//') && !/[\r\n]/.test(route)) void router.push(route) }
onMounted(() => {
  void load()
  realtime = createNotificationRealtime({ getAccessToken: () => authStore.session?.accessToken ?? null, onRefresh: load })
  void realtime.start()
  pollTimer = window.setInterval(() => { if (document.visibilityState === 'visible') void load() }, 15000)
})
onBeforeUnmount(() => { if (pollTimer !== undefined) window.clearInterval(pollTimer); if (realtime !== null) void realtime.stop() })
</script>

<template>
  <AppPage :title="copy.title" :description="copy.description">
    <template #actions><PermissionGate :permission-n-id="PERMISSIONS.systemDataNotificationAnnouncementManage"><el-button type="primary" @click="form.mode = 'announcement'; editingId = null; open = true">{{ copy.newAnnouncement }}</el-button><el-button @click="form.mode = 'system'; editingId = null; open = true">{{ copy.sendSystemMessage }}</el-button></PermissionGate></template>
    <AppQueryPanel :title="copy.announcementQuery" show-actions @submit="load" @reset="reset"><el-input v-model="search" clearable /></AppQueryPanel>
    <p v-if="errorMessage" class="pf04-error" role="alert">{{ errorMessage }}</p>
    <AppDataTable table-key="systemdata-announcements" route-key="systemdata-notifications" row-key="announcementNId" :rows="announcements" :total="announcements.length" :columns="columns"><template #cell-actions="{ row }"><el-button v-if="row.status === 'Draft'" link type="primary" @click="edit(row)">{{ copy.edit }}</el-button><el-button v-if="row.status === 'Published'" link type="danger" @click="revoke(row)">{{ copy.revoke }}</el-button></template></AppDataTable>
    <h2 class="pf04-section-title">{{ copy.myInbox }}</h2>
    <el-button v-if="inbox.some((item) => !item.isRead)" @click="markAllRead">{{ copy.markAllRead }}</el-button><AppDataTable table-key="systemdata-notification-inbox" route-key="systemdata-notifications" row-key="notificationNId" :rows="inbox" :total="inboxTotal" :columns="inboxColumns"><template #cell-isRead="{ row }"><el-button v-if="!row.isRead" link type="primary" @click="markRead(row)">{{ copy.markRead }}</el-button><el-button v-if="row.targetRoute" link @click="openTarget(row.targetRoute)">{{ copy.open }}</el-button><span v-else>✓</span></template></AppDataTable>
  </AppPage>
  <AppFormDrawer v-model="open" :busy="busy" :title="form.mode === 'system' ? copy.sendSystemMessage : (editingId ? copy.edit : copy.newAnnouncement)" @submit="create"><el-form label-width="100px"><el-form-item :label="copy.titleLabel"><el-input v-model="form.title" /></el-form-item><el-form-item :label="copy.bodyLabel"><el-input v-model="form.body" type="textarea" :rows="6" /></el-form-item><el-form-item :label="copy.usersLabel"><el-input v-model="form.recipients" :placeholder="copy.usersPlaceholder" /></el-form-item><el-checkbox v-if="form.mode === 'announcement'" v-model="form.publish">{{ copy.publish }}</el-checkbox></el-form></AppFormDrawer>
</template>

<style scoped>.pf04-section-title { margin: 24px 0 12px; color: var(--ip-color-text-primary); }</style>
