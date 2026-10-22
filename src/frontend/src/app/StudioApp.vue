<script setup lang="ts">
import { computed, onMounted, ref, type DefineComponent } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElConfigProvider } from 'element-plus'
import elementEn from 'element-plus/es/locale/lang/en'
import elementZh from 'element-plus/es/locale/lang/zh-cn'
import type { Language } from 'element-plus/es/locale'
import { getStudioApi, type Json, type StudioCapabilities } from '@/api/studio'
import { studioAuthSession } from './studioSession'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useThemeStore } from '@/stores/themeStore'
import { useStudioCopy } from '@/components/studio/shared/copy'
const auth = useAuthStore(),
  locale = useLocalizationStore(),
  theme = useThemeStore(),
  route = useRoute(),
  router = useRouter(),
  t = useStudioCopy()
const ready = ref(false),
  busy = ref(false),
  error = ref(''),
  tenant = ref('default'),
  login = ref('admin'),
  password = ref(''),
  oldPassword = ref(''),
  newPassword = ref(''),
  confirmPassword = ref(''),
  capabilities = ref<StudioCapabilities | null>(null)
const provider = ref(import.meta.env.VITE_STUDIO_IDENTITY_PROVIDER ?? 'LocalAdmin')
const ConfigProvider = ElConfigProvider as unknown as DefineComponent<{ locale?: Language }>
const english = computed(() => locale.locale === 'en-US'),
  tr = (zh: string, en: string) => (english.value ? en : zh)
const modulesReady = computed(
  () =>
    capabilities.value?.coreReady &&
    capabilities.value.modules.some((m) => m.key === 'LowCode' && m.installed && m.enabled),
)
const links = computed(() =>
  [
    {
      path: '/pc/studio/low-code',
      label: t.value('lowcode'),
      permission: 'studio.lowcode.view',
      module: 'LowCode',
    },
    {
      path: '/pc/studio/sources',
      label: t.value('sources'),
      permission: 'studio.source.view',
      module: 'Core',
    },
    {
      path: '/pc/studio/datasets',
      label: t.value('datasets'),
      permission: 'studio.dataset.view',
      module: 'Core',
    },
    {
      path: '/pc/studio/ledger-models',
      label: t.value('ledger'),
      permission: 'studio.lowcode.design',
      module: 'LowCode',
    },
  ].filter(
    (link) => auth.hasPermission(link.permission) && (link.module === 'Core' || modulesReady.value),
  ),
)
async function loadIdentity() {
  const client = getStudioApi().client
  const me = await client.get<Json>('/api/v1/studio/auth/me')
  auth.adoptSession(studioAuthSession(me))
  if (!me.mustChangePassword) {
    const profile = await client.get<Json>('/api/v1/studio/deployment/profile')
    provider.value = profile.identityProvider
    capabilities.value = await getStudioApi().capabilities()
    await theme.bindUser({ tenantId: me.principal.tenantNId, userId: me.principal.subjectNId })
  }
}
async function guard(fn: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await fn()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function signIn() {
  await guard(async () => {
    await getStudioApi().client.post('/api/v1/studio/auth/local-login', {
      tenantNId: tenant.value,
      loginName: login.value,
      password: password.value,
    })
    password.value = ''
    await loadIdentity()
  })
}
async function changePassword() {
  await guard(async () => {
    if (newPassword.value !== confirmPassword.value)
      throw new Error(tr('两次密码不一致', 'Passwords do not match'))
    await getStudioApi().client.post('/api/v1/studio/auth/local-change-password', {
      currentPassword: oldPassword.value,
      newPassword: newPassword.value,
    })
    oldPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    auth.clearLocalSession()
    error.value = tr('密码已修改，请重新登录', 'Password changed. Sign in again.')
  })
}
async function logout() {
  await guard(async () => {
    try {
      await getStudioApi().client.post('/api/v1/studio/auth/logout')
    } finally {
      auth.clearLocalSession()
      capabilities.value = null
      void router.push('/pc/studio/low-code')
    }
  })
}
function mesLogin() {
  const base = import.meta.env.VITE_API_BASE_URL ?? ''
  window.location.assign(
    base + '/api/v1/studio/auth/login?returnPath=' + encodeURIComponent(route.fullPath),
  )
}
onMounted(async () => {
  try {
    await loadIdentity()
  } catch (e) {
    if (!(e && typeof e === 'object' && 'kind' in e && e.kind === 'unauthorized'))
      error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    ready.value = true
  }
})
</script>
<template>
  <ConfigProvider :locale="english ? elementEn : elementZh"
    ><div class="studio-standalone">
      <header class="studio-standalone-header">
        <strong>Platform Studio</strong><span class="studio-fill" /><el-select
          :model-value="locale.locale"
          :aria-label="tr('语言', 'Language')"
          style="width: 110px"
          @update:model-value="(v: Json[string]) => locale.setLocale(v)"
          ><el-option label="中文" value="zh-CN" /><el-option
            label="English"
            value="en-US" /></el-select
        ><el-select
          :model-value="theme.preferences.mode"
          :aria-label="tr('主题', 'Theme')"
          style="width: 110px"
          @update:model-value="(v: Json[string]) => theme.setMode(v)"
          ><el-option :label="tr('浅色', 'Light')" value="light" /><el-option
            :label="tr('深色', 'Dark')"
            value="dark" /><el-option :label="tr('跟随系统', 'System')" value="system" /></el-select
        ><template v-if="auth.isAuthenticated"
          ><span>{{ auth.user?.displayName }}</span
          ><el-button :disabled="busy" @click="logout">{{
            tr('退出', 'Sign out')
          }}</el-button></template
        >
      </header>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <el-skeleton v-if="!ready" :rows="8" animated />
      <main v-else-if="!auth.isAuthenticated" class="studio-login">
        <h1>{{ tr('登录 Studio', 'Sign in to Studio') }}</h1>
        <el-form v-if="provider === 'LocalAdmin'" label-position="top" @submit.prevent="signIn"
          ><el-form-item :label="tr('租户编号', 'Tenant ID')"
            ><el-input v-model="tenant" autocomplete="organization" /></el-form-item
          ><el-form-item :label="tr('账号', 'Account')"
            ><el-input v-model="login" autocomplete="username" /></el-form-item
          ><el-form-item :label="tr('密码', 'Password')"
            ><el-input
              v-model="password"
              type="password"
              autocomplete="current-password"
              show-password /></el-form-item
          ><el-button type="primary" native-type="submit" :loading="busy">{{
            tr('登录', 'Sign in')
          }}</el-button></el-form
        ><el-button v-else type="primary" @click="mesLogin">{{
          tr('使用 MES / OIDC 登录', 'Sign in with MES / OIDC')
        }}</el-button>
      </main>
      <main v-else-if="auth.user?.mustChangePassword" class="studio-login">
        <h1>{{ tr('首次登录修改密码', 'Change your initial password') }}</h1>
        <el-form label-position="top" @submit.prevent="changePassword"
          ><el-form-item :label="tr('原密码', 'Current password')"
            ><el-input
              v-model="oldPassword"
              type="password"
              autocomplete="current-password" /></el-form-item
          ><el-form-item :label="tr('新密码', 'New password')"
            ><el-input
              v-model="newPassword"
              type="password"
              autocomplete="new-password" /></el-form-item
          ><el-form-item :label="tr('确认新密码', 'Confirm password')"
            ><el-input
              v-model="confirmPassword"
              type="password"
              autocomplete="new-password" /></el-form-item
          ><el-button native-type="submit" type="primary" :loading="busy">{{
            t('submit')
          }}</el-button></el-form
        >
      </main>
      <template v-else
        ><nav class="studio-standalone-nav" :aria-label="t('navigation')">
          <el-button
            v-for="link in links"
            :key="link.path"
            :type="route.path.startsWith(link.path) ? 'primary' : 'default'"
            @click="router.push(link.path)"
            >{{ link.label }}</el-button
          ><el-button :disabled="busy" @click="guard(loadIdentity)">{{ t('refresh') }}</el-button>
        </nav>
        <main class="studio-standalone-body">
          <el-alert
            v-if="route.path.includes('/low-code') && !modulesReady"
            :title="t('unavailable')"
            type="warning"
            :closable="false"
          /><RouterView
            v-else-if="
              typeof route.meta.permission !== 'string' || auth.hasPermission(route.meta.permission)
            "
            :key="route.path"
          /><el-alert v-else :title="t('noPermission')" type="error" :closable="false" /></main
      ></template></div
  ></ConfigProvider>
</template>
<style scoped>
.studio-standalone {
  min-height: 100dvh;
  background: var(--ip-color-bg-page);
  color: var(--ip-color-text-primary);
}
.studio-standalone-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 24px;
  border-bottom: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}
.studio-fill {
  flex: 1;
}
.studio-standalone-nav {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  padding: 12px 24px;
}
.studio-standalone-nav .el-button {
  margin: 0;
}
.studio-standalone-body {
  padding: 12px 24px;
}
.studio-login {
  max-width: 420px;
  margin: 7vh auto;
  padding: 24px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
}
@media (max-width: 768px) {
  .studio-standalone-header {
    flex-wrap: wrap;
    padding: 12px;
  }
  .studio-standalone-body,
  .studio-standalone-nav {
    padding: 12px;
  }
  .studio-login {
    margin: 24px 12px;
  }
}
</style>
