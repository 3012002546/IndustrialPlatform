<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { ApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import { getSsoApi, type SsoDiscoveryProviderDto } from '@/api/identity/sso'
import { mapAuthSession } from '@/api/identity/mapper'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()
const router = useRouter()
const route = useRoute()
const ssoApi = getSsoApi()

const loading = ref(true)
const redirecting = ref(false)
const processingProvider = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const providers = ref<SsoDiscoveryProviderDto[]>([])

/** 安全 redirect:仅接受站内相对路径,拒绝开放重定向与协议相对地址(§26.4 服务端同规则)。 */
const returnUrl = computed<string | undefined>(() => {
  const raw = route.query.redirect
  return typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//') ? raw : undefined
})

function toErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  return DEFAULT_ERROR_MESSAGES.unknown
}

function resolveRedirect(raw: string | null | undefined): string {
  if (typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//')) return raw
  return '/'
}

function protocolLabel(protocol: string): string {
  if (protocol === 'Oidc') return 'OIDC'
  if (protocol === 'Saml2') return 'SAML 2.0'
  return protocol
}

/** 一次性票据交换:采纳线上会话后跳转已校验回跳地址(§26.5)。 */
async function exchangeTicket(
  ticket: string,
  fallbackReturnUrl: string | null | undefined,
): Promise<void> {
  const response = await ssoApi.exchange({ ticket })
  authStore.adoptSession(mapAuthSession(response.session))
  await router.replace(resolveRedirect(response.returnUrl ?? fallbackReturnUrl))
}

/** 开始授权:无 providerNId 时后端按单源/多源返回跳转地址或选择列表。 */
async function beginAuthorize(providerNId?: string | undefined): Promise<void> {
  loading.value = true
  errorMessage.value = null
  try {
    const response = await ssoApi.authorize({ returnUrl: returnUrl.value, providerNId })
    if (response.reused && response.ticket !== null) {
      await exchangeTicket(response.ticket, response.returnUrl ?? returnUrl.value)
      return
    }
    if (response.authorizeUri !== null && response.authorizeUri !== '') {
      redirecting.value = true
      window.location.assign(response.authorizeUri)
      return
    }
    providers.value = response.providers ?? []
    if (providers.value.length === 0) {
      errorMessage.value = '未配置可用的企业登录源,请联系管理员。'
    }
  } catch (error) {
    errorMessage.value = toErrorMessage(error)
  } finally {
    loading.value = false
  }
}

async function selectProvider(provider: SsoDiscoveryProviderDto): Promise<void> {
  if (processingProvider.value !== null) return
  processingProvider.value = provider.providerNId
  errorMessage.value = null
  try {
    const response = await ssoApi.authorize({
      returnUrl: returnUrl.value,
      providerNId: provider.providerNId,
    })
    if (response.reused && response.ticket !== null) {
      await exchangeTicket(response.ticket, response.returnUrl ?? returnUrl.value)
      return
    }
    if (response.authorizeUri !== null && response.authorizeUri !== '') {
      redirecting.value = true
      window.location.assign(response.authorizeUri)
      return
    }
  } catch (error) {
    errorMessage.value = toErrorMessage(error)
  } finally {
    processingProvider.value = null
  }
}

onMounted(() => {
  void beginAuthorize()
})
</script>

<template>
  <main class="sso-login-page">
    <section class="sso-login-card" aria-labelledby="sso-login-title">
      <header class="sso-login-card__header">
        <PlatformBrand variant="light" />
        <h1 id="sso-login-title" class="sso-login-card__title">企业登录</h1>
        <p class="sso-login-card__subtitle">Industrial Platform · 统一身份认证</p>
      </header>

      <p v-if="loading" class="sso-login-card__status" role="status">正在连接企业登录服务…</p>
      <p v-else-if="redirecting" class="sso-login-card__status" role="status">
        正在跳转企业身份源…
      </p>
      <p v-else-if="errorMessage" class="sso-login-card__error" role="alert">
        {{ errorMessage }}
      </p>

      <ul v-else-if="providers.length > 0" class="sso-login-card__providers">
        <li v-for="provider in providers" :key="provider.providerNId">
          <button
            type="button"
            class="sso-login-card__provider"
            :disabled="processingProvider !== null"
            data-testid="sso-provider"
            @click="selectProvider(provider)"
          >
            <span class="sso-login-card__provider-name">{{ provider.name }}</span>
            <span class="sso-login-card__provider-protocol">
              {{ protocolLabel(provider.protocol) }}
            </span>
          </button>
        </li>
      </ul>

      <div class="sso-login-card__footer">
        <router-link class="sso-login-card__link" :to="{ name: ROUTE_NAMES.login }">
          返回密码登录
        </router-link>
      </div>
    </section>
  </main>
</template>

<style scoped>
.sso-login-page {
  display: grid;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  place-items: center;
  background: var(--ip-color-bg-page);
}

.sso-login-card {
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

.sso-login-card__header {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.sso-login-card__title {
  margin: 0;
  font-size: var(--ip-font-size-2xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
  color: var(--ip-color-text-primary);
}

.sso-login-card__subtitle {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.sso-login-card__status {
  margin: 0;
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-secondary);
}

.sso-login-card__error {
  margin: 0;
  padding: var(--ip-space-2) var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-danger);
  background: var(--ip-color-danger-bg);
  border-radius: var(--ip-radius-md);
}

.sso-login-card__providers {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.sso-login-card__provider {
  display: flex;
  align-items: center;
  justify-content: space-between;
  box-sizing: border-box;
  width: 100%;
  height: 44px;
  padding: 0 var(--ip-space-3);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  cursor: pointer;
}

.sso-login-card__provider:hover:not(:disabled) {
  border-color: var(--ip-color-primary);
}

.sso-login-card__provider:disabled {
  color: var(--ip-color-text-disabled);
  cursor: not-allowed;
}

.sso-login-card__provider-protocol {
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.sso-login-card__footer {
  display: flex;
  justify-content: center;
}

.sso-login-card__link {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-primary);
  text-decoration: none;
}

.sso-login-card__link:hover {
  text-decoration: underline;
}
</style>
