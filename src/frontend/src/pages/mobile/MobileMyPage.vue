<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import AppPage from '@/components/base/AppPage.vue'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const router = useRouter()
const authStore = useAuthStore()

const displayName = computed(() => authStore.user?.displayName ?? '')
const username = computed(() => authStore.user?.username ?? '')
const roles = computed(() => authStore.user?.roles ?? [])

async function handleLogout(): Promise<void> {
  try {
    await authStore.logout()
  } catch {
    // logout 内部已吞掉网关失败;此处兜底确保仍能回登录页
  } finally {
    await router.push({ name: ROUTE_NAMES.login })
  }
}
</script>

<template>
  <AppPage title="我的">
    <div class="mobile-my__profile">
      <p class="mobile-my__display-name" data-testid="display-name">
        {{ displayName || '未登录' }}
      </p>
      <p class="mobile-my__username" data-testid="username">{{ username }}</p>
      <p v-if="roles.length > 0" class="mobile-my__roles" data-testid="roles">
        {{ roles.join(' / ') }}
      </p>
    </div>

    <button
      type="button"
      class="mobile-my__logout"
      data-testid="logout-button"
      @click="handleLogout"
    >
      退出登录
    </button>
  </AppPage>
</template>

<style scoped>
.mobile-my__profile {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.mobile-my__display-name {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 600;
  color: var(--ip-color-text-primary);
}

.mobile-my__username {
  margin: 0;
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-secondary);
}

.mobile-my__roles {
  margin: var(--ip-space-2) 0 0;
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

/* 44px 触控目标(§17:最小 44×44) */
.mobile-my__logout {
  box-sizing: border-box;
  width: 100%;
  min-height: var(--ip-touch-min-size-mobile);
  margin-top: var(--ip-space-6);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  cursor: pointer;
}

.mobile-my__logout:hover {
  background: var(--ip-color-bg-muted);
}
</style>
