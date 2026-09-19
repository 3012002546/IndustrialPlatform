/** 路由工厂:唯一创建入口,测试可用 createMemoryHistory 自建 Router。 */

import { createRouter, createWebHistory, type Router } from 'vue-router'
import IdentityUsersPage from '@/pages/pc/identity/IdentityUsersPage.vue'

import { installRouterGuards } from './guards'
import { routes } from './routes'

export function createAppRouter(): Router {
  const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes,
  })
  installRouterGuards(router)
  return router
}

/** 单页仅登记用户管理路由，原平台路由表及守卫保持原样。 */
export function createSingleUsersRouter(): Router {
  return createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [{ path: '/pc/identity/users', component: IdentityUsersPage }],
  })
}
