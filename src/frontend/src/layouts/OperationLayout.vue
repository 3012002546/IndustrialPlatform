<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElMessage, ElMessageBox } from 'element-plus'
import { Delete, FullScreen, Lock, SwitchButton, UserFilled } from '@element-plus/icons-vue'

import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import LocaleControl from '@/components/localization/LocaleControl.vue'
import PcExperienceModeControl from '@/components/shell/PcExperienceModeControl.vue'
import PlatformContextSwitcher from '@/components/shell/PlatformContextSwitcher.vue'
import PlatformEnvironmentBadge from '@/components/shell/PlatformEnvironmentBadge.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import PlatformSessionControls from '@/components/shell/PlatformSessionControls.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'
import { clearCurrentUserUiCache } from '@/stores/uiCacheStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { useLockStore } from '@/stores/lockStore'

const router = useRouter()
const authStore = useAuthStore()
const tabsStore = useWorkspaceTabsStore()
const lockStore = useLockStore()
const runtimeConfig = loadRuntimeConfig()
const locale = usePlatformLocale()
const browserFullscreen = ref(false)
const copy = computed(() => localeMessages[locale.value])
const displayName = computed(() => authStore.user?.displayName ?? '')
const tenant = computed(() => {
  const user = authStore.user
  return user === null ? null : { id: user.tenantId, name: user.tenantId }
})

function onFullscreenChange(): void {
  browserFullscreen.value = document.fullscreenElement !== null
}

async function toggleBrowserFullscreen(): Promise<void> {
  try {
    if (document.fullscreenElement !== null) await document.exitFullscreen()
    else await document.documentElement.requestFullscreen()
  } catch {
    browserFullscreen.value = false
  }
}

function onUserCommand(command: unknown): void {
  if (command === 'profile') void router.push({ name: 'profile' })
  else if (command === 'clear-cache') void clearCache()
  else if (command === 'lock') {
    lockStore.lock()
  } else if (command === 'logout') void (async () => {
    await authStore.logout()
    await router.push({ name: 'login' })
  })()
}

async function clearCache(): Promise<void> {
  const user = authStore.user
  if (user === null) return
  try {
    await ElMessageBox.confirm(copy.value.shell.top.clearCacheConfirm, copy.value.shell.top.clearCache, {
      confirmButtonText: copy.value.shell.top.clearCache,
      cancelButtonText: copy.value.common.action.cancel,
      type: 'warning',
    })
  } catch {
    return
  }
  clearCurrentUserUiCache({ tenantId: user.tenantId, userId: user.userId })
  tabsStore.clearUiCache()
  ElMessage.success(copy.value.shell.top.cacheCleared)
}

onMounted(() => {
  document.addEventListener('fullscreenchange', onFullscreenChange)
  onFullscreenChange()
})
onBeforeUnmount(() => document.removeEventListener('fullscreenchange', onFullscreenChange))
</script>

<template>
  <div class="ip-operation-layout">
    <header class="ip-operation-topbar">
      <div class="ip-operation-topbar__brand">
        <PlatformBrand variant="dark" />
      </div>
      <div class="ip-operation-topbar__context">
        <span class="ip-operation-terminal">{{ copy.shell.top.terminal }} PC</span>
        <PlatformContextSwitcher :tenant="tenant" />
        <PlatformEnvironmentBadge :environment="runtimeConfig.deploymentEnvironment" />
      </div>
      <div class="ip-operation-topbar__right">
        <div class="ip-operation-topbar__actions">
          <PcExperienceModeControl mode="operation" />
          <PlatformSessionControls />
          <LocaleControl />
          <button
            type="button"
            class="ip-operation-action"
            data-testid="operation-fullscreen"
            :aria-label="
              browserFullscreen ? copy.common.action.exitFullscreen : copy.common.action.fullscreen
            "
            @click="toggleBrowserFullscreen"
          >
            <FullScreen aria-hidden="true" />
          </button>
          <ThemeControl terminal="pc" />
        </div>
        <ElDropdown trigger="click" @command="onUserCommand">
          <button type="button" class="ip-operation-user" data-testid="operation-user-menu" :aria-label="copy.shell.top.userMenu">
            <span class="ip-operation-user__avatar"><UserFilled width="18" height="18" aria-hidden="true" /></span>
            <span class="ip-operation-user__copy">
              <strong class="ip-operation-user__name">{{ displayName || copy.common.state.unauthenticated }}</strong>
              <small>{{ authStore.user?.username ?? '' }}</small>
            </span>
            <svg class="ip-operation-user__caret" width="11" height="11" viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false">
              <path d="M4 6l4 4 4-4" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </button>
          <template #dropdown>
            <ElDropdownMenu
              ><ElDropdownItem command="profile"><UserFilled aria-hidden="true" />{{ copy.shell.top.profile }}</ElDropdownItem>
              <ElDropdownItem command="clear-cache"><Delete aria-hidden="true" />{{ copy.shell.top.clearCache }}</ElDropdownItem>
              <ElDropdownItem command="lock"><Lock aria-hidden="true" />{{ copy.shell.top.lock }}</ElDropdownItem>
              <ElDropdownItem command="logout"><SwitchButton aria-hidden="true" />{{ copy.common.action.logout }}</ElDropdownItem></ElDropdownMenu
            >
          </template>
        </ElDropdown>
      </div>
    </header>
    <main id="main-content" class="ip-operation-main" tabindex="-1">
      <RouterView />
      <slot />
    </main>
  </div>
</template>

<style scoped>
.ip-operation-layout {
  min-height: 100vh;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-page);
}

.ip-operation-topbar {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 20px;
  min-height: var(--ip-shell-topbar-height);
  padding: 0 4px;
  overflow: visible;
  color: var(--ip-shell-topbar-text);
  background: var(--ip-shell-topbar-background);
}

.ip-operation-topbar__brand {
  display: inline-flex;
  flex: 0 0 184px;
  align-items: center;
  width: 184px;
  min-width: 0;
  margin-right: 4px;
}

.ip-operation-topbar__brand :deep(.ip-brand__image) {
  width: 184px;
  height: 30px;
  max-width: none;
  max-height: none;
  object-fit: contain;
}

.ip-operation-topbar__context {
  display: inline-flex;
  flex: 1 1 auto;
  align-items: center;
  gap: var(--ip-space-3);
  min-width: 0;
  overflow: hidden;
}

.ip-operation-terminal {
  flex: 0 0 auto;
  color: var(--ip-shell-topbar-text-secondary);
  font-size: var(--ip-font-size-xs);
  white-space: nowrap;
}

.ip-operation-topbar__right,
.ip-operation-topbar__actions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-width: 0;
}

.ip-operation-topbar__right {
  flex: 0 0 auto;
  gap: 4px;
  margin-left: auto;
}

.ip-operation-action,
.ip-operation-user {
  display: inline-flex;
  box-sizing: border-box;
  align-items: center;
  gap: 9px;
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: var(--ip-shell-topbar-text);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font: inherit;
}

.ip-operation-user {
  width: 190px;
  min-width: 144px;
  overflow: hidden;
  text-align: left;
  white-space: nowrap;
}

.ip-operation-action > :deep(svg),
.ip-operation-user__avatar > :deep(svg) {
  flex: 0 0 auto;
  width: 18px;
  height: 18px;
}

.ip-operation-user__avatar {
  display: inline-flex;
  flex: 0 0 28px;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  color: #35728e;
  background: #d9e9f2;
  border-radius: 50%;
}

.ip-operation-user__copy {
  display: flex;
  min-width: 0;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 1px;
}

.ip-operation-user > * {
  min-width: 0;
  max-width: 100%;
}

.ip-operation-user__name {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-operation-user__copy small {
  overflow: hidden;
  color: var(--ip-shell-topbar-text-secondary);
  font-size: 11px;
  line-height: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-operation-user__caret {
  flex: 0 0 11px;
  color: var(--ip-shell-topbar-text-secondary);
}

.ip-operation-action:focus-visible,
.ip-operation-user:focus-visible {
  outline: 2px solid currentColor;
  outline-offset: 2px;
}

.ip-operation-main {
  box-sizing: border-box;
  max-width: 1600px;
  min-height: calc(100vh - var(--ip-shell-topbar-height));
  margin: 0 auto;
  padding: 16px;
}

@media (max-width: 899px) {
  .ip-operation-topbar {
    flex-wrap: wrap;
    padding: var(--ip-space-2) var(--ip-space-3);
  }

  .ip-operation-topbar__context {
    margin-left: 0;
  }

  .ip-operation-topbar__right {
    margin-left: 0;
  }
}
</style>
