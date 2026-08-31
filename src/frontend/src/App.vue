<script setup lang="ts">
/**
 * 根组件:路由出口 + 全局权限变化响应。
 * 会话权限被刷新/变更时,若当前页面所需权限已失去,跳转 /403(§13.2)。
 */
import { computed, watch, type DefineComponent } from 'vue'
import { ElConfigProvider } from 'element-plus'
import elementPlusEn from 'element-plus/es/locale/lang/en'
import elementPlusZhCn from 'element-plus/es/locale/lang/zh-cn'
import type { Language } from 'element-plus/es/locale'
import { useRoute, useRouter } from 'vue-router'

import { ROUTE_NAMES } from '@/router/routes'
import { setDocumentTitle } from '@/router/guards'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useAuthStore } from '@/stores/authStore'
import SystemDataRuntimeStatus from '@/components/systemData/SystemDataRuntimeStatus.vue'
import { setVxeLocale } from '@/localization/vxeLocale'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()
const localization = useLocalizationStore()
// Element Plus 的安装包装器在当前声明中暴露了原始 prop 定义;将其收窄为官方组件的公开 locale 契约。
const PlatformConfigProvider = ElConfigProvider as unknown as DefineComponent<{
  locale?: Language
}>
const elementLocale = computed(() =>
  localization.locale === 'en-US' ? elementPlusEn : elementPlusZhCn,
)

watch(
  () => localization.locale,
  (value) => setVxeLocale(value),
  { immediate: true },
)

watch(
  [() => route.name, () => localization.locale],
  () => setDocumentTitle(route.meta.title, route.meta.titleKey, route.meta.fallbackTitle),
  { immediate: true },
)

watch(
  () => authStore.user?.permissions,
  () => {
    // 登出场景由守卫跳转登录页处理,这里只处理「已登录但权限被收回」。
    if (!authStore.isAuthenticated) return
    const permission = route.meta.permission
    const anyPermissions = route.meta.anyPermissions as readonly string[] | undefined
    if (
      permission !== undefined &&
      !authStore.hasPermission(permission) &&
      route.name !== ROUTE_NAMES.forbidden
    ) {
      void router.push({ name: ROUTE_NAMES.forbidden })
      return
    }
    if (
      anyPermissions !== undefined &&
      !anyPermissions.some((required) => authStore.hasPermission(required)) &&
      route.name !== ROUTE_NAMES.forbidden
    ) {
      void router.push({ name: ROUTE_NAMES.forbidden })
    }
  },
)
</script>

<template>
  <PlatformConfigProvider :locale="elementLocale">
    <SystemDataRuntimeStatus />
    <RouterView />
  </PlatformConfigProvider>
</template>
