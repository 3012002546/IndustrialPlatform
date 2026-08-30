<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus'
import { FullScreen } from '@element-plus/icons-vue'

import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import LocaleControl from '@/components/localization/LocaleControl.vue'
import PcExperienceModeControl from '@/components/shell/PcExperienceModeControl.vue'
import PlatformContextSwitcher from '@/components/shell/PlatformContextSwitcher.vue'
import PlatformEnvironmentBadge from '@/components/shell/PlatformEnvironmentBadge.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'

const router = useRouter()
const authStore = useAuthStore()
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
  if (command !== 'logout') return
  void (async () => {
    await authStore.logout()
    await router.push({ name: 'login' })
  })()
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
        <PlatformContextSwitcher :tenant="tenant" />
        <PlatformEnvironmentBadge :environment="runtimeConfig.deploymentEnvironment" />
      </div>
      <div class="ip-operation-topbar__right">
        <div class="ip-operation-topbar__actions">
          <LocaleControl />
          <PcExperienceModeControl mode="operation" />
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
          <button type="button" class="ip-operation-user" data-testid="operation-user-menu">
            <span class="ip-operation-user__name">{{
              displayName || copy.common.state.unauthenticated
            }}</span>
          </button>
          <template #dropdown>
            <ElDropdownMenu
              ><ElDropdownItem command="logout">{{
                copy.common.action.logout
              }}</ElDropdownItem></ElDropdownMenu
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
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
  min-height: var(--ip-shell-topbar-height);
  padding: 0 var(--ip-space-5);
  overflow: hidden;
  color: var(--ip-shell-topbar-text);
  background: var(--ip-shell-topbar-background);
}

.ip-operation-topbar__brand {
  display: inline-flex;
  flex: 0 1 auto;
  align-items: center;
  min-width: 0;
}

.ip-operation-topbar__context {
  display: inline-flex;
  flex: 1 1 auto;
  align-items: center;
  gap: var(--ip-space-3);
  min-width: 0;
  overflow: hidden;
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
  gap: var(--ip-space-2);
  margin-left: auto;
}

.ip-operation-action,
.ip-operation-user {
  box-sizing: border-box;
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
  width: clamp(120px, 14vw, 220px);
  min-width: 120px;
  overflow: hidden;
  text-align: left;
  white-space: nowrap;
}

.ip-operation-user__name {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  padding: var(--ip-space-6);
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
