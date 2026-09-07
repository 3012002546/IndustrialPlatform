<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElAlert, ElButton, ElDialog, ElDrawer, ElEmpty, ElMessage, ElMessageBox } from 'element-plus'
import { Bell, Promotion, Refresh, UserFilled } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'

import { getManagementApi } from '@/api/identity/managementRegistry'
import type { IdentityActiveSessionDto } from '@/api/identity/management/types'
import { createNotificationRealtime, type NotificationRealtime } from '@/api/systemData/notificationHub'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { NotificationInboxItemDto } from '@/api/systemData/pf04Types'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableColumn } from '@/components/management/AppDataTable'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'

const locale = usePlatformLocale()
const authStore = useAuthStore()
const router = useRouter()
const copy = computed(() => localeMessages[locale.value].shell.top)
const common = computed(() => localeMessages[locale.value].common)
const sessionsOpen = ref(false)
const sessionsLoading = ref(false)
const sessionsError = ref(false)
const sessions = ref<IdentityActiveSessionDto[]>([])
const notificationOpen = ref(false)
const notificationTrigger = ref<HTMLButtonElement | null>(null)
const notificationPanel = ref<HTMLElement | null>(null)
const notificationPanelStyle = ref<Record<string, string>>({})
const notificationItems = ref<NotificationInboxItemDto[]>([])
const notificationUnreadCount = ref(0)
const notificationLoading = ref(false)
const notificationError = ref(false)
let notificationPollTimer: number | undefined
let notificationRealtime: NotificationRealtime | null = null
const sendMessageOpen = ref(false)
const sendMessageBusy = ref(false)
const sendMessageError = ref('')
const sendMessageTarget = ref<IdentityActiveSessionDto | null>(null)
const sendMessageTitle = ref('')
const sendMessageBody = ref('')
let sendMessageIdempotencyKey = ''

interface ActiveSessionTableRow extends IdentityActiveSessionDto {
  index: number
  loginOn: string
  lastRefreshedOn: string
  expiresOn: string
  currentSessionLabel: string
}

const canViewSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionView))
const canRevokeSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionRevoke))
const canViewNotifications = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationInboxRead))
const canSendMessages = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationSystemSend))

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.valueOf())
    ? '—'
    : new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

const sessionColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'index', title: copy.value.index, width: 58, sortable: false, filter: false },
  { field: 'loginName', title: copy.value.profileAccount, minWidth: 120, sortable: false, filter: false },
  { field: 'name', title: copy.value.profileName, minWidth: 100, sortable: false, filter: false },
  { field: 'loginOn', title: copy.value.loginTime, minWidth: 150, sortable: false, filter: false },
  { field: 'lastRefreshedOn', title: copy.value.lastRefresh, minWidth: 150, sortable: false, filter: false },
  { field: 'expiresOn', title: copy.value.expires, minWidth: 150, sortable: false, filter: false },
  { field: 'currentSessionLabel', title: copy.value.currentSession, width: 92, sortable: false, filter: false },
])

const sessionRows = computed<readonly ActiveSessionTableRow[]>(() =>
  sessions.value.map((session, index) => ({
    ...session,
    index: index + 1,
    loginOn: formatDate(session.loginOn),
    lastRefreshedOn: formatDate(session.lastRefreshedOn),
    expiresOn: formatDate(session.expiresOn),
    currentSessionLabel: session.isCurrent ? copy.value.currentSession : '—',
  })),
)

async function loadSessions(): Promise<void> {
  if (!canViewSessions.value) return
  sessionsLoading.value = true
  sessionsError.value = false
  try {
    const result = await getManagementApi().listActiveSessions()
    sessions.value = result.items
  } catch {
    sessions.value = []
    sessionsError.value = true
  } finally {
    sessionsLoading.value = false
  }
}

async function openSessions(): Promise<void> {
  sessionsOpen.value = true
  await loadSessions()
}

function safeNotificationRoute(route: string | null | undefined): string | null {
  if (route === undefined || route === null || !route.startsWith('/') || route.startsWith('//')) return null
  if (/[\u0000-\u001f\u007f]/.test(route)) return null
  return route
}

async function loadNotifications(): Promise<void> {
  if (!canViewNotifications.value) return
  const api = getPf04Api()
  if (api === null) {
    notificationError.value = true
    return
  }
  notificationLoading.value = true
  notificationError.value = false
  try {
    const result = await api.getInbox(1, 20)
    notificationItems.value = result.items
    notificationUnreadCount.value = result.unreadCount
  } catch {
    notificationError.value = true
  } finally {
    notificationLoading.value = false
  }
}

function openNotificationTarget(route: string | null | undefined): void {
  const safeRoute = safeNotificationRoute(route)
  if (safeRoute !== null) void router.push(safeRoute)
}

async function markNotificationRead(item: NotificationInboxItemDto): Promise<void> {
  if (item.isRead) return
  const api = getPf04Api()
  if (api === null) return
  try {
    await api.markRead(item.notificationNId)
    await loadNotifications()
  } catch {
    notificationError.value = true
  }
}

async function markAllNotificationsRead(): Promise<void> {
  const api = getPf04Api()
  const ids = notificationItems.value.filter((item) => !item.isRead).map((item) => item.notificationNId)
  if (api === null || ids.length === 0) return
  try {
    await api.batchRead(ids)
    await loadNotifications()
  } catch {
    notificationError.value = true
  }
}

function toggleNotifications(): void {
  if (!canViewNotifications.value) return
  notificationOpen.value = !notificationOpen.value
  if (notificationOpen.value) {
    void loadNotifications()
    void nextTick(() => {
      positionNotificationPanel()
      notificationPanel.value?.focus()
    })
  }
}

function openSendMessage(row: IdentityActiveSessionDto): void {
  if (!canSendMessages.value) return
  sendMessageTarget.value = row
  sendMessageTitle.value = ''
  sendMessageBody.value = ''
  sendMessageError.value = ''
  sendMessageIdempotencyKey = `shell-${row.userNId}-${crypto.randomUUID?.() ?? `${Date.now()}`}`
  sendMessageOpen.value = true
}

function closeSendMessage(): void {
  if (sendMessageBusy.value) return
  sendMessageOpen.value = false
  sendMessageTarget.value = null
  sendMessageError.value = ''
}

async function submitSendMessage(): Promise<void> {
  const target = sendMessageTarget.value
  const api = getPf04Api()
  if (api === null || target === null || sendMessageBusy.value) return
  if (sendMessageTitle.value.trim() === '' || sendMessageBody.value.trim() === '') {
    sendMessageError.value = copy.value.sendMessageRequired
    return
  }
  sendMessageBusy.value = true
  sendMessageError.value = ''
  try {
    await api.sendSystemMessage({
      title: sendMessageTitle.value.trim(),
      body: sendMessageBody.value.trim(),
      recipientUserNIds: [target.userNId],
      idempotencyKey: sendMessageIdempotencyKey,
    })
    ElMessage.success(copy.value.sendMessageSuccess)
    sendMessageBusy.value = false
    closeSendMessage()
  } catch {
    sendMessageError.value = copy.value.sendMessageFailed
    ElMessage.error(sendMessageError.value)
  } finally {
    sendMessageBusy.value = false
  }
}

function beforeCloseSendMessage(done: () => void): void {
  if (sendMessageBusy.value) return
  done()
}

function positionNotificationPanel(): void {
  const triggerRect = notificationTrigger.value?.getBoundingClientRect()
  if (triggerRect === undefined) return
  const panelWidth = notificationPanel.value?.getBoundingClientRect().width || 360
  const panelHeight = notificationPanel.value?.getBoundingClientRect().height || 180
  const gap = 8
  const left = Math.max(gap, Math.min(triggerRect.right - panelWidth, window.innerWidth - panelWidth - gap))
  const below = triggerRect.bottom + gap
  const top = below + panelHeight <= window.innerHeight - gap
    ? below
    : Math.max(gap, triggerRect.top - panelHeight - gap)
  notificationPanelStyle.value = { top: `${top}px`, left: `${left}px` }
}

function closeNotifications(restoreFocus = false): void {
  notificationOpen.value = false
  if (restoreFocus) void nextTick(() => notificationTrigger.value?.focus())
}

function onNotificationKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return
  event.preventDefault()
  closeNotifications(true)
}

function onDocumentPointerDown(event: PointerEvent): void {
  const target = event.target as Node | null
  if (notificationTrigger.value?.contains(target) || notificationPanel.value?.contains(target)) return
  closeNotifications()
}

async function revokeSession(row: IdentityActiveSessionDto): Promise<void> {
  if (!canRevokeSessions.value) return
  try {
    await ElMessageBox.confirm(
      copy.value.revokeSessionConfirm.replace('{loginName}', row.loginName),
      copy.value.revokeSession,
      { type: 'warning', confirmButtonText: copy.value.revokeSession, cancelButtonText: common.value.action.cancel },
    )
  } catch {
    return
  }
  try {
    const result = await getManagementApi().revokeSession(row.sessionNId)
    ElMessage.success(copy.value.revokeSessionSuccess)
    if (result.isCurrent) {
      authStore.clearLocalSession()
      await router.push({ name: 'login' })
      return
    }
    await loadSessions()
  } catch {
    sessionsError.value = true
  }
}

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown)
  window.addEventListener('resize', positionNotificationPanel)
  window.addEventListener('scroll', positionNotificationPanel, true)
  if (canViewNotifications.value) {
    void loadNotifications()
    notificationRealtime = createNotificationRealtime({
      getAccessToken: () => authStore.session?.accessToken ?? null,
      onRefresh: loadNotifications,
    })
    void notificationRealtime.start()
    notificationPollTimer = window.setInterval(() => {
      if (document.visibilityState === 'visible') void loadNotifications()
    }, 15000)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
  window.removeEventListener('resize', positionNotificationPanel)
  window.removeEventListener('scroll', positionNotificationPanel, true)
  if (notificationPollTimer !== undefined) window.clearInterval(notificationPollTimer)
  if (notificationRealtime !== null) void notificationRealtime.stop()
})
</script>

<template>
  <button
    v-if="canViewNotifications"
    ref="notificationTrigger"
    type="button"
    class="ip-shell-action ip-shell-message"
    data-testid="shell-notifications"
    :aria-label="copy.notification"
    :aria-expanded="notificationOpen"
    aria-haspopup="dialog"
    :title="copy.notification"
    @click="toggleNotifications"
  >
    <Bell aria-hidden="true" />
    <span v-if="notificationUnreadCount > 0" class="ip-shell-notification-badge" data-testid="shell-notification-unread-count">
      {{ notificationUnreadCount > 99 ? '99+' : notificationUnreadCount }}
    </span>
  </button>

  <Teleport to="body">
    <div
      v-if="notificationOpen"
      ref="notificationPanel"
      class="ip-shell-notifications-panel"
      data-testid="shell-notification-panel"
      role="dialog"
      tabindex="-1"
      :aria-label="copy.notification"
      :style="notificationPanelStyle"
      @keydown="onNotificationKeydown"
    >
      <div class="ip-shell-notifications-panel__header">
        <div class="ip-shell-notifications-panel__title">{{ copy.notification }}</div>
        <ElButton link :loading="notificationLoading" :aria-label="copy.notificationRetry" @click="loadNotifications">
          <Refresh aria-hidden="true" />
        </ElButton>
      </div>
      <p v-if="notificationLoading" class="ip-shell-notifications-panel__state">{{ copy.notificationLoading }}</p>
      <ElAlert v-else-if="notificationError" type="error" :closable="false" show-icon>
        {{ copy.notificationLoadFailed }}
        <ElButton data-testid="shell-notification-retry" link type="danger" @click="loadNotifications">{{ copy.notificationRetry }}</ElButton>
      </ElAlert>
      <ElEmpty v-else-if="notificationItems.length === 0" :description="copy.notificationEmpty" />
      <div v-else class="ip-shell-notifications-list">
        <article v-for="item in notificationItems" :key="item.notificationNId" class="ip-shell-notification-item" :class="{ 'is-unread': !item.isRead }">
          <div class="ip-shell-notification-item__content">
            <strong>{{ item.title }}</strong>
            <p>{{ item.body }}</p>
            <time :datetime="item.deliveredOn">{{ formatDate(item.deliveredOn) }}</time>
            <span>{{ item.isRead ? copy.notificationRead : copy.notificationUnread }}</span>
          </div>
          <div class="ip-shell-notification-item__actions">
            <ElButton v-if="!item.isRead" :data-testid="`shell-notification-read-${item.notificationNId}`" link @click="markNotificationRead(item)">{{ copy.notificationMarkRead }}</ElButton>
            <ElButton v-if="safeNotificationRoute(item.targetRoute) !== null" :data-testid="`shell-notification-open-${item.notificationNId}`" link @click="openNotificationTarget(item.targetRoute)">{{ copy.notificationOpen }}</ElButton>
          </div>
        </article>
        <ElButton v-if="notificationItems.some((item) => !item.isRead)" data-testid="shell-notification-mark-all-read" link type="primary" @click="markAllNotificationsRead">{{ copy.notificationMarkAllRead }}</ElButton>
      </div>
    </div>
  </Teleport>
  <button
    v-if="canViewSessions"
    type="button"
    class="ip-shell-action"
    data-testid="online-users-button"
    :aria-label="copy.onlineUsers"
    :title="copy.onlineUsers"
    @click="openSessions"
  >
    <UserFilled aria-hidden="true" />
  </button>

  <ElDrawer
    v-model="sessionsOpen"
    class="ip-online-sessions"
    direction="rtl"
    size="min(680px, 100vw)"
    :title="copy.onlineUsers"
    :destroy-on-close="false"
  >
    <p class="ip-online-sessions__description">{{ copy.onlineUsersDescription }}</p>
    <div class="ip-online-sessions__toolbar">
      <ElButton data-testid="online-users-refresh" :loading="sessionsLoading" :aria-busy="sessionsLoading" :aria-label="common.action.refresh" @click="loadSessions">
        <Refresh aria-hidden="true" />
        {{ common.action.refresh }}
      </ElButton>
    </div>
    <ElAlert v-if="sessionsError" type="error" :closable="false" show-icon>
      {{ common.state.error }}
      <ElButton data-testid="online-users-retry" link type="danger" @click="loadSessions">{{ common.action.retry }}</ElButton>
    </ElAlert>
    <ElEmpty v-else-if="!sessionsLoading && sessions.length === 0" :description="copy.onlineUsersEmpty" />
    <div v-else class="ip-online-sessions__table" data-testid="online-users-table">
      <AppDataTable
        table-key="online-sessions"
        route-key="online-sessions"
        row-key="sessionNId"
        :columns="sessionColumns"
        :rows="sessionRows"
        :total="sessionRows.length"
        :loading="sessionsLoading"
        :page-size="100"
      >
        <template #cell-currentSessionLabel="{ row }">
          <span v-if="row.isCurrent" class="ip-online-sessions__current">●</span>
          <span class="ip-online-sessions__visually-hidden">{{ row.currentSessionLabel }}</span>
        </template>
        <template #actions="{ row }">
          <ElButton
            v-if="canSendMessages"
            link
            :title="copy.sendMessage"
            :aria-label="copy.sendMessage"
            data-testid="shell-send-message"
            @click="openSendMessage(row as IdentityActiveSessionDto)"
          >
            <Promotion aria-hidden="true" />
          </ElButton>
          <ElButton
            v-if="canRevokeSessions"
            link
            type="danger"
            @click="revokeSession(row as ActiveSessionTableRow)"
          >
            {{ copy.revokeSession }}
          </ElButton>
        </template>
      </AppDataTable>
    </div>
  </ElDrawer>

  <ElDialog v-if="sendMessageOpen" v-model="sendMessageOpen" :title="copy.sendMessage" width="min(520px, calc(100vw - 32px))" destroy-on-close data-testid="shell-send-message-dialog" :before-close="beforeCloseSendMessage" @close="closeSendMessage">
    <el-form label-width="90px" @submit.prevent="submitSendMessage">
      <el-form-item :label="copy.sendMessageRecipient">
        <el-input data-testid="shell-send-message-recipient" :model-value="sendMessageTarget?.name || sendMessageTarget?.loginName || ''" disabled />
      </el-form-item>
      <el-form-item :label="copy.sendMessageTitleLabel">
        <el-input v-model="sendMessageTitle" data-testid="shell-send-message-title" />
      </el-form-item>
      <el-form-item :label="copy.sendMessageBodyLabel">
        <el-input v-model="sendMessageBody" data-testid="shell-send-message-body" type="textarea" :rows="5" />
      </el-form-item>
      <p v-if="sendMessageError" role="alert" class="ip-shell-send-message-error">{{ sendMessageError }}</p>
    </el-form>
    <template #footer>
      <ElButton :disabled="sendMessageBusy" @click="closeSendMessage">{{ common.action.cancel }}</ElButton>
      <ElButton data-testid="shell-send-message-submit" type="primary" :loading="sendMessageBusy" @click="submitSendMessage">{{ common.action.confirm }}</ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.ip-shell-action {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 32px;
  width: 32px;
  height: 32px;
  padding: 0;
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.ip-shell-action:hover:not(:disabled),
.ip-shell-action:focus-visible:not(:disabled) {
  background: rgb(255 255 255 / 0.12);
}

.ip-shell-action:disabled {
  cursor: not-allowed;
  opacity: 0.62;
}

.ip-shell-action :deep(svg) {
  width: 18px;
  height: 18px;
}

.ip-shell-notification-badge {
  position: absolute;
  top: 0;
  right: -2px;
  min-width: 16px;
  padding: 0 4px;
  color: var(--ip-color-on-primary);
  background: var(--ip-color-danger);
  border-radius: 999px;
  font-size: 10px;
  line-height: 16px;
  text-align: center;
}

.ip-shell-notifications-panel {
  position: fixed;
  z-index: 2200;
  width: min(360px, calc(100vw - 16px));
  max-height: calc(100vh - 16px);
  overflow: auto;
  padding: var(--ip-space-3) var(--ip-space-4);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-lg);
}

.ip-shell-notifications-panel__title {
  font-size: var(--ip-font-size-md);
  font-weight: 650;
}

.ip-shell-notifications-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.ip-shell-notifications-panel__state {
  margin: var(--ip-space-4) 0;
  color: var(--ip-color-text-secondary);
}

.ip-shell-notifications-list {
  display: grid;
  gap: var(--ip-space-2);
  margin-top: var(--ip-space-3);
}

.ip-shell-notification-item {
  display: grid;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.ip-shell-notification-item.is-unread {
  border-color: var(--ip-color-primary);
}

.ip-shell-notification-item__content {
  display: grid;
  gap: var(--ip-space-1);
}

.ip-shell-notification-item__content p,
.ip-shell-notification-item__content time,
.ip-shell-notification-item__content span {
  margin: 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}

.ip-shell-notification-item__content p {
  white-space: pre-wrap;
}

.ip-shell-notification-item__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
}

.ip-shell-send-message-error {
  margin: var(--ip-space-3) 0 0;
  color: var(--ip-color-danger);
}

.ip-shell-notifications-panel :deep(.el-empty) {
  padding: var(--ip-space-4) 0 var(--ip-space-2);
}

.ip-online-sessions__description {
  margin: 0 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.ip-online-sessions__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: var(--ip-space-3);
}

.ip-online-sessions__table :deep(.app-data-table__toolbar),
.ip-online-sessions__table :deep(.app-data-table__footer) {
  display: none;
}

.ip-online-sessions__current {
  color: var(--ip-color-success);
}

.ip-online-sessions__visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>
