/**
 * 稳定路由表(§12.1)。公共页面、PC 首页(FE-007)、PDA 基础壳(FE-008)
 * 与 Mobile 基础壳(FE-009)均为真实页面。
 * PC 管理框架 / PDA 现场壳 / Mobile 壳布局已接入(作为 /pc、/pda、/mobile 父路由)。
 * 路由名全局唯一,守卫/导航一律使用 name,不硬编码路径。
 */

import { defineComponent, h } from 'vue'
import type { RouteRecordRaw } from 'vue-router'

import PcLayout from '@/layouts/PcLayout.vue'
import PdaLayout from '@/layouts/PdaLayout.vue'
import MobileLayout from '@/layouts/MobileLayout.vue'
import ForbiddenPage from '@/pages/public/ForbiddenPage.vue'
import LoginPage from '@/pages/public/LoginPage.vue'
import NotFoundPage from '@/pages/public/NotFoundPage.vue'
import PcHomePage from '@/pages/pc/PcHomePage.vue'
import PdaHomePage from '@/pages/pda/PdaHomePage.vue'
import MobileHomePage from '@/pages/mobile/MobileHomePage.vue'
import MobileMyPage from '@/pages/mobile/MobileMyPage.vue'

export const ROUTE_NAMES = {
  root: 'root',
  login: 'login',
  forbidden: 'forbidden',
  pcHome: 'pc-home',
  pdaHome: 'pda-home',
  mobileHome: 'mobile-home',
  mobileMy: 'mobile-my',
  notFound: 'not-found',
} as const

/**
 * 根路由占位:守卫对 root 总是按生效终端分流(§12.3),组件不会实际渲染;
 * 保留最小占位组件满足路由类型要求。
 */
function rootStub() {
  return defineComponent({
    name: 'RootStub',
    render: () => h('div', { 'data-testid': 'stub-root' }),
  })
}

export const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: ROUTE_NAMES.root,
    component: rootStub(),
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
    path: '/pda',
    component: PdaLayout,
    children: [
      {
        path: 'home',
        name: ROUTE_NAMES.pdaHome,
        component: PdaHomePage,
        meta: {
          title: 'PDA 首页',
          requiresAuth: true,
          permission: 'platform.pda.view',
          terminal: 'pda',
        },
      },
    ],
  },
  {
    path: '/mobile',
    component: MobileLayout,
    children: [
      {
        path: 'home',
        name: ROUTE_NAMES.mobileHome,
        component: MobileHomePage,
        meta: {
          title: 'Mobile 首页',
          requiresAuth: true,
          permission: 'platform.mobile.view',
          terminal: 'mobile',
        },
      },
      {
        path: 'my',
        name: ROUTE_NAMES.mobileMy,
        component: MobileMyPage,
        meta: {
          title: '我的',
          requiresAuth: true,
          permission: 'platform.mobile.view',
          terminal: 'mobile',
        },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: ROUTE_NAMES.notFound,
    component: NotFoundPage,
    meta: { title: '页面不存在' },
  },
]
