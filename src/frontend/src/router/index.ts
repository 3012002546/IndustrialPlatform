/** 路由工厂:唯一创建入口,测试可用 createMemoryHistory 自建 Router。 */

import { createRouter, createWebHistory, type Router } from 'vue-router'

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
