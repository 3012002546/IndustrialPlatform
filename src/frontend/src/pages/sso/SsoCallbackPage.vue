<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { ApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import { mapAuthSession } from '@/api/identity/mapper'
import { getSsoApi } from '@/api/identity/sso'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()
const router = useRouter()
const route = useRoute()
const ssoApi = getSsoApi()

const exchanging = ref(true)
const errorMessage = ref<string | null>(null)

function toErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  return DEFAULT_ERROR_MESSAGES.unknown
}

function resolveRedirect(raw: string | null | undefined): string {
  if (typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//')) return raw
  return '/'
}

/** 回调页(§26.5):消费一次性票据交换完整会话,采纳后跳转已校验回跳地址。 */
async function exchange(): Promise<void> {
  const ticket = typeof route.query.ticket === 'string' ? route.query.ticket : null
  if (ticket === null || ticket === '') {
    errorMessage.value = '登录票据缺失或已失效。'
    exchanging.value = false
    return
  }

  const fallbackReturnUrl = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : null
  try {
    const response = await ssoApi.exchange({ ticket })
    authStore.adoptSession(mapAuthSession(response.session))
    await router.replace(resolveRedirect(response.returnUrl ?? fallbackReturnUrl))
  } catch (error) {
    errorMessage.value = toErrorMessage(error)
    exchanging.value = false
  }
}

onMounted(() => {
  void exchange()
})
</script>

<template>
  <main class="sso-callback-page">
    <section class="sso-callback-card" aria-labelledby="sso-callback-title">
      <h1 id="sso-callback-title" class="sso-callback-card__title">企业登录</h1>
      <p v-if="exchanging" class="sso-callback-card__status" role="status">正在完成登录…</p>
      <p v-else class="sso-callback-card__error" role="alert">{{ errorMessage }}</p>
      <router-link
        v-if="!exchanging"
        class="sso-callback-card__link"
        :to="{ name: ROUTE_NAMES.login }"
      >
        返回密码登录
      </router-link>
    </section>
  </main>
</template>

<style scoped>
.sso-callback-page {
  display: grid;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  place-items: center;
  background: var(--ip-color-bg-page);
}

.sso-callback-card {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
  width: 100%;
  max-width: 360px;
  padding: var(--ip-space-8) var(--ip-space-6);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-md);
}

.sso-callback-card__title {
  margin: 0;
  font-size: var(--ip-font-size-2xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
  color: var(--ip-color-text-primary);
}

.sso-callback-card__status {
  margin: 0;
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-secondary);
}

.sso-callback-card__error {
  margin: 0;
  padding: var(--ip-space-2) var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-danger);
  background: var(--ip-color-danger-bg);
  border-radius: var(--ip-radius-md);
}

.sso-callback-card__link {
  align-self: center;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-primary);
  text-decoration: none;
}

.sso-callback-card__link:hover {
  text-decoration: underline;
}
</style>
