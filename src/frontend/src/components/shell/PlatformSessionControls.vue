<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElAlert, ElButton, ElDrawer, ElEmpty, ElMessage, ElMessageBox } from 'element-plus'
import { Bell, Promotion, Refresh, UserFilled } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'

import { getManagementApi } from '@/api/identity/managementRegistry'
import type { IdentityActiveSessionDto } from '@/api/identity/management/types'
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

interface ActiveSessionTableRow extends IdentityActiveSessionDto {
  index: number
  loginOn: string
  lastRefreshedOn: string
  expiresOn: string
  currentSessionLabel: string
}

const canViewSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionView))
const canRevokeSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionRevoke))

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

function toggleNotifications(): void {
  notificationOpen.value = !notificationOpen.value
  if (notificationOpen.value) {
    void nextTick(() => {
      positionNotificationPanel()
      notificationPanel.value?.focus()
    })
  }
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
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
  window.removeEventListener('resize', positionNotificationPanel)
  window.removeEventListener('scroll', positionNotificationPanel, true)
})
</script>

<template>
  <button
    ref="notificationTrigger"
    type="button"
    class="ip-shell-action ip-shell-message"
    data-testid="shell-notifications"
    :aria-label="copy.notification"
    :aria-expanded="notificationOpen"
    aria-haspopup="dialog"
    :title="copy.notificationUnavailable"
    @click="toggleNotifications"
  >
    <Bell aria-hidden="true" />
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
      <div class="ip-shell-notifications-panel__title">{{ copy.notification }}</div>
      <ElEmpty :description="copy.notificationEmpty" />
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
            link
            disabled
            :title="copy.sendMessageUnavailable"
            :aria-label="copy.sendMessageUnavailable"
            data-testid="shell-send-message"
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
</template>

<style scoped>
.ip-shell-action {
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
