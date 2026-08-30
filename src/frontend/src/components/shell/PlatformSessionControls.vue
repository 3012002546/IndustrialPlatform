<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElAlert, ElButton, ElDrawer, ElEmpty, ElMessage, ElMessageBox, ElTable, ElTableColumn } from 'element-plus'
import { Bell, Refresh, UserFilled } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'

import { getManagementApi } from '@/api/identity/managementRegistry'
import type { IdentityActiveSessionDto } from '@/api/identity/management/types'
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

const canViewSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionView))
const canRevokeSessions = computed(() => authStore.hasPermission(PERMISSIONS.sessionRevoke))

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.valueOf())
    ? '—'
    : new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

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
</script>

<template>
  <button
    type="button"
    class="ip-shell-action ip-shell-message"
    data-testid="shell-notifications"
    :aria-label="copy.notification"
    :title="copy.notificationUnavailable"
    disabled
  >
    <Bell aria-hidden="true" />
  </button>
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
      <ElButton :loading="sessionsLoading" :aria-label="common.action.refresh" @click="loadSessions">
        <Refresh aria-hidden="true" />
        {{ common.action.refresh }}
      </ElButton>
    </div>
    <ElAlert v-if="sessionsError" type="error" :closable="false" show-icon>
      {{ common.state.error }}
      <ElButton link type="danger" @click="loadSessions">{{ common.action.retry }}</ElButton>
    </ElAlert>
    <ElEmpty v-else-if="!sessionsLoading && sessions.length === 0" :description="copy.onlineUsersEmpty" />
    <ElTable v-else v-loading="sessionsLoading" :data="sessions" row-key="sessionNId" size="small">
      <ElTableColumn type="index" :label="copy.index" width="58" />
      <ElTableColumn prop="loginName" :label="copy.profileAccount" min-width="120" />
      <ElTableColumn prop="name" :label="copy.profileName" min-width="100" />
      <ElTableColumn :label="copy.loginTime" min-width="150">
        <template #default="scope">{{ formatDate(scope.row.loginOn) }}</template>
      </ElTableColumn>
      <ElTableColumn :label="copy.lastRefresh" min-width="150">
        <template #default="scope">{{ formatDate(scope.row.lastRefreshedOn) }}</template>
      </ElTableColumn>
      <ElTableColumn :label="copy.expires" min-width="150">
        <template #default="scope">{{ formatDate(scope.row.expiresOn) }}</template>
      </ElTableColumn>
      <ElTableColumn :label="copy.currentSession" width="92">
        <template #default="scope">
          <span v-if="scope.row.isCurrent" class="ip-online-sessions__current">●</span>
          <span class="ip-online-sessions__visually-hidden">{{ scope.row.isCurrent ? copy.currentSession : '' }}</span>
        </template>
      </ElTableColumn>
      <ElTableColumn :label="common.table.actions" width="108" fixed="right">
        <template #default="scope">
          <ElButton
            v-if="canRevokeSessions"
            link
            type="danger"
            @click="revokeSession(scope.row as IdentityActiveSessionDto)"
          >
            {{ copy.revokeSession }}
          </ElButton>
        </template>
      </ElTableColumn>
    </ElTable>
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
