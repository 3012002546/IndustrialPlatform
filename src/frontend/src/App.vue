<script setup lang="ts">
/**
 * 根组件:路由出口 + 全局权限变化响应。
 * 会话权限被刷新/变更时,若当前页面所需权限已失去,跳转 /403(§13.2)。
 */
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

watch(
  () => authStore.user?.permissions,
  () => {
    // 登出场景由守卫跳转登录页处理,这里只处理「已登录但权限被收回」。
    if (!authStore.isAuthenticated) return
    const permission = route.meta.permission
    if (
      permission !== undefined &&
      !authStore.hasPermission(permission) &&
      route.name !== ROUTE_NAMES.forbidden
    ) {
      void router.push({ name: ROUTE_NAMES.forbidden })
    }
  },
)
</script>

<template>
  <RouterView />
</template>
