<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import AppErrorAlert from '@/components/base/AppErrorAlert.vue'
import AppPage from '@/components/base/AppPage.vue'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

const HOME_ROUTES = [
  { name: ROUTE_NAMES.pcHome, permission: 'platform.home.view' },
  { name: ROUTE_NAMES.pdaHome, permission: 'platform.pda.view' },
  { name: ROUTE_NAMES.mobileHome, permission: 'platform.mobile.view' },
] as const

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()

/** 返回有权限首页(§15.3):优先当前终端首页,否则任一有权限终端首页。 */
const permittedHome = computed(() => {
  const preferred = `${deviceStore.terminal}-home`
  const preferredRoute = HOME_ROUTES.find((r) => r.name === preferred)
  if (preferredRoute !== undefined && authStore.hasPermission(preferredRoute.permission)) {
    return preferredRoute
  }
  return HOME_ROUTES.find((r) => authStore.hasPermission(r.permission)) ?? null
})

/** TraceId 仅在可用时展示(§15.3);无则隐藏该区段。 */
const traceId = computed(() =>
  typeof route.query.traceId === 'string' ? route.query.traceId : undefined,
)

function goHome(): void {
  if (permittedHome.value === null) return
  void router.push({ name: permittedHome.value.name })
}

async function relogin(): Promise<void> {
  await authStore.logout()
  await router.push({ name: ROUTE_NAMES.login })
}
</script>

<template>
  <main class="forbidden-page">
    <AppPage title="无权限">
      <AppErrorAlert
        v-bind="{
          title: '无权访问',
          message: '你当前账号没有访问该页面的权限。',
          ...(traceId === undefined ? {} : { traceId }),
        }"
      >
        <button
          v-if="permittedHome !== null"
          type="button"
          class="forbidden-page__btn forbidden-page__btn--primary"
          data-testid="go-home"
          @click="goHome"
        >
          返回有权限首页
        </button>
        <button type="button" class="forbidden-page__btn" data-testid="relogin" @click="relogin">
          重新登录
        </button>
      </AppErrorAlert>
    </AppPage>
  </main>
</template>

<style scoped>
.forbidden-page {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  background: var(--ip-color-bg-page);
}

.forbidden-page__btn {
  box-sizing: border-box;
  height: 36px;
  padding: 0 var(--ip-space-4);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  cursor: pointer;
}

.forbidden-page__btn--primary {
  border-color: var(--ip-color-primary);
  background: var(--ip-color-primary);
  color: #fff;
}
</style>
