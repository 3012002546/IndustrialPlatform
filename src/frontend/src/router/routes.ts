/**
 * 稳定路由表(§12.1)。公共页面与 PC 首页为真实页面(FE-007);
 * PDA/Mobile 首页仍为最小测试桩(FE-008/FE-009 替换)。
 * PC 管理框架布局壳已接入(FE-006,作为 /pc 父路由)。
 * 路由名全局唯一,守卫/导航一律使用 name,不硬编码路径。
 */

import { defineComponent, h } from 'vue'
import type { RouteRecordRaw } from 'vue-router'

import PcLayout from '@/layouts/PcLayout.vue'
import ForbiddenPage from '@/pages/public/ForbiddenPage.vue'
import LoginPage from '@/pages/public/LoginPage.vue'
import NotFoundPage from '@/pages/public/NotFoundPage.vue'
import PcHomePage from '@/pages/pc/PcHomePage.vue'

export const ROUTE_NAMES = {
  root: 'root',
  login: 'login',
  forbidden: 'forbidden',
  pcHome: 'pc-home',
  pdaHome: 'pda-home',
  mobileHome: 'mobile-home',
  notFound: 'not-found',
} as const

/** 最小测试桩:渲染占位文本,供守卫/导航测试断言(页面在主布局 <main> 内,桩用 div)。 */
function stub(label: string) {
  return defineComponent({
    name: `Stub${label}`,
    render: () => h('div', { 'data-testid': `stub-${label}` }, label),
  })
}

export const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: ROUTE_NAMES.root,
    component: stub('Root'),
    meta: { title: '工业平台' },
  },
  {
    path: '/login',
    name: ROUTE_NAMES.login,
    component: LoginPage,
    meta: { title: '登录' },
  },
  {
    path: '/403',
    name: ROUTE_NAMES.forbidden,
    component: ForbiddenPage,
    meta: { title: '无权限' },
  },
  {
    path: '/pc',
    component: PcLayout,
    children: [
      {
        path: 'home',
        name: ROUTE_NAMES.pcHome,
        component: PcHomePage,
        meta: {
          title: 'PC 首页',
          requiresAuth: true,
          permission: 'platform.home.view',
          terminal: 'pc',
        },
      },
    ],
  },
  {
    path: '/pda/home',
    name: ROUTE_NAMES.pdaHome,
    component: stub('PdaHome'),
    meta: {
      title: 'PDA 首页',
      requiresAuth: true,
      permission: 'platform.pda.view',
      terminal: 'pda',
    },
  },
  {
    path: '/mobile/home',
    name: ROUTE_NAMES.mobileHome,
    component: stub('MobileHome'),
    meta: {
      title: 'Mobile 首页',
      requiresAuth: true,
      permission: 'platform.mobile.view',
      terminal: 'mobile',
    },
  },
  {
    path: '/:pathMatch(.*)*',
    name: ROUTE_NAMES.notFound,
    component: NotFoundPage,
    meta: { title: '页面不存在' },
  },
]
