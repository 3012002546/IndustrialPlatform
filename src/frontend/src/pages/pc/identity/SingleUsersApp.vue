<script setup lang="ts">
import { computed, onMounted, ref, watch, type DefineComponent } from 'vue'
import { ElConfigProvider } from 'element-plus'
import type { Language } from 'element-plus/es/locale'
import elementPlusEn from 'element-plus/es/locale/lang/en'
import elementPlusZhCn from 'element-plus/es/locale/lang/zh-cn'
import { createHttpClient } from '@/api/httpClient'
import { createIdentityAuthApi } from '@/api/identity/identityApi'
import { mapAuthSession, mapAuthUser } from '@/api/identity/mapper'
import { getSsoApi } from '@/api/identity/sso'
import { getAuthGateway } from '@/auth/gateway'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useThemeStore } from '@/stores/themeStore'
import { setVxeLocale } from '@/localization/vxeLocale'
import { PERMISSIONS } from '@/permissions'
import IdentityUsersPage from './IdentityUsersPage.vue'

const auth = useAuthStore()
const localization = useLocalizationStore()
const theme = useThemeStore()
const loading = ref(true)
const error = ref('')
const PlatformConfigProvider = ElConfigProvider as unknown as DefineComponent<{ locale?: Language }>
const elementLocale = computed(() =>
  localization.locale === 'en-US' ? elementPlusEn : elementPlusZhCn,
)
watch(() => localization.locale, setVxeLocale, { immediate: true })

function tokenExpiry(token: string): string {
  const payload = token.split('.')[1]
  if (!payload) throw new Error('访问凭证格式无效。')
  try {
    const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as {
      exp?: unknown
    }
    if (typeof claims.exp !== 'number' || claims.exp * 1000 <= Date.now())
      throw new Error('访问凭证已过期。')
    return new Date(claims.exp * 1000).toISOString()
  } catch {
    throw new Error('访问凭证无效或已过期。')
  }
}

async function enter(): Promise<void> {
  const url = new URL(window.location.href)
  const mode = url.searchParams.getAll('mode')
  const tokens = url.searchParams.getAll('token')
  const tickets = url.searchParams.getAll('ticket')
  // 先清除敏感查询参数，避免留在浏览器历史、后续导航或错误页面。
  url.searchParams.delete('token')
  url.searchParams.delete('ticket')
  window.history.replaceState(window.history.state, '', url.pathname + url.search + url.hash)
  try {
    if (
      window.location.pathname !== '/pc/identity/users' ||
      loadRuntimeConfig().authMode !== 'http' ||
      mode.length !== 1 ||
      mode[0] !== 'single' ||
      tokens.length > 1 ||
      tickets.length > 1 ||
      (tokens.length && tickets.length)
    )
      throw new Error('单页入口参数无效。')

    if (tokens.length === 1) {
      const token = tokens[0] ?? ''
      const expiresAt = tokenExpiry(token)
      const config = loadRuntimeConfig()
      const client = createHttpClient({
        baseUrl: config.apiBaseUrl,
        timeoutMs: config.requestTimeoutMs,
        getToken: () => token,
      })
      const user = mapAuthUser(await createIdentityAuthApi(client).getCurrentUser())
      auth.adoptSession({ accessToken: token, expiresAt, user, transport: 'single-access' })
    } else if (tickets.length === 1 && tickets[0]) {
      const response = await getSsoApi().exchange({ ticket: tickets[0] })
      if (response.returnUrl) {
        const target = new URL(response.returnUrl, window.location.origin)
        if (target.origin !== window.location.origin || target.pathname !== '/pc/identity/users')
          throw new Error('登录票据的目标页面不匹配。')
      }
      auth.adoptSession(mapAuthSession(response.session))
    } else {
      await auth.restore()
      if (auth.session) {
        const currentUser = await getAuthGateway().getCurrentUser()
        auth.adoptSession({ ...auth.session, user: currentUser })
      }
    }
    if (!auth.isAuthenticated) throw new Error('单页登录会话不可用。')
    if (!auth.hasPermission(PERMISSIONS.userView)) throw new Error('没有用户管理查看权限。')
    if (auth.user?.mustChangePassword) throw new Error('请先在平台完成首次密码修改。')
    await theme.initialize()
    if (auth.user) await theme.bindUser({ tenantId: auth.user.tenantId, userId: auth.user.userId })
  } catch (cause) {
    auth.clearLocalSession()
    error.value = cause instanceof Error ? cause.message : '单页登录失败。'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void enter()
})
</script>

<template>
  <PlatformConfigProvider :locale="elementLocale">
    <main class="single-users" data-testid="single-users-entry">
      <p v-if="loading" role="status">正在验证登录…</p>
      <p v-else-if="error" role="alert">{{ error }}</p>
      <IdentityUsersPage v-else :key="localization.locale" />
    </main>
  </PlatformConfigProvider>
</template>

<style scoped>
.single-users {
  box-sizing: border-box;
  min-height: 100vh;
  padding: 16px;
  background: var(--ip-color-bg-page);
  color: var(--ip-color-text-primary);
}
</style>
