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
import StandaloneLayout from '@/layouts/StandaloneLayout.vue'
import ChangePasswordPage from '@/pages/public/ChangePasswordPage.vue'
import ForbiddenPage from '@/pages/public/ForbiddenPage.vue'
import LoginPage from '@/pages/public/LoginPage.vue'
import NotFoundPage from '@/pages/public/NotFoundPage.vue'
import SsoCallbackPage from '@/pages/sso/SsoCallbackPage.vue'
import SsoLoginPage from '@/pages/sso/SsoLoginPage.vue'
import PcHomePage from '@/pages/pc/PcHomePage.vue'
import PcOperationHomePage from '@/pages/pc/PcOperationHomePage.vue'
import OperationLayout from '@/layouts/OperationLayout.vue'
import TerminalPreviewPage from '@/pages/pc/TerminalPreviewPage.vue'
import IdentityAuditsPage from '@/pages/pc/identity/IdentityAuditsPage.vue'
import IdentityPermissionsPage from '@/pages/pc/identity/IdentityPermissionsPage.vue'
import IdentityRolesPage from '@/pages/pc/identity/IdentityRolesPage.vue'
import IdentityUserGroupsPage from '@/pages/pc/identity/IdentityUserGroupsPage.vue'
import IdentityUsersPage from '@/pages/pc/identity/IdentityUsersPage.vue'
import ProfilePage from '@/pages/pc/ProfilePage.vue'
import SsoClientsPage from '@/pages/pc/identity/sso/SsoClientsPage.vue'
import SsoProvidersPage from '@/pages/pc/identity/sso/SsoProvidersPage.vue'
import OrganizationsPage from '@/pages/pc/systemData/OrganizationsPage.vue'
import AssignmentsPage from '@/pages/pc/systemData/AssignmentsPage.vue'
import NavigationPage from '@/pages/pc/systemData/NavigationPage.vue'
import FeaturesPage from '@/pages/pc/systemData/FeaturesPage.vue'
import ServicesPage from '@/pages/pc/systemData/ServicesPage.vue'
import ThemesPage from '@/pages/pc/systemData/ThemesPage.vue'
import ServiceInitializationPage from '@/pages/pc/systemData/ServiceInitializationPage.vue'
import WorkspaceTabsSandboxPage from '@/pages/dev/WorkspaceTabsSandboxPage.vue'
import PdaHomePage from '@/pages/pda/PdaHomePage.vue'
import MobileHomePage from '@/pages/mobile/MobileHomePage.vue'
import MobileMyPage from '@/pages/mobile/MobileMyPage.vue'
import { PERMISSIONS } from '@/permissions'

export const ROUTE_NAMES = {
  root: 'root',
  login: 'login',
  changePassword: 'change-password',
  ssoLogin: 'sso-login',
  ssoCallback: 'sso-callback',
  forbidden: 'forbidden',
  pcHome: 'pc-home',
  pcOperation: 'pc-operation',
  profile: 'profile',
  terminalPreview: 'terminal-preview',
  identityUsers: 'identity-users',
  identityUserGroups: 'identity-user-groups',
  identityRoles: 'identity-roles',
  identityPermissions: 'identity-permissions',
  identityAudits: 'identity-audits',
  ssoProviders: 'sso-providers',
  ssoClients: 'sso-clients',
  systemDataOrganizations: 'systemdata-organizations',
  systemDataAssignments: 'systemdata-assignments',
  systemDataNavigation: 'systemdata-navigation',
  systemDataFeatures: 'systemdata-features',
  systemDataServices: 'systemdata-services',
  systemDataThemes: 'systemdata-themes',
  systemDataServiceInitialization: 'systemdata-service-initialization',
  workspaceTabsSandbox: 'workspace-tabs-sandbox',
  uiBaseline: 'ui-baseline',
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
    meta: {
      title: '登录',
      titleKey: 'login.title',
      fallbackTitle: '登录',
    },
  },
  {
    path: '/change-password',
    name: ROUTE_NAMES.changePassword,
    component: ChangePasswordPage,
    meta: {
      title: '修改密码',
      titleKey: 'changePassword.title',
      fallbackTitle: '修改密码',
      requiresAuth: true,
    },
  },
  {
    path: '/sso',
    component: StandaloneLayout,
    children: [
      {
        path: 'login',
        name: ROUTE_NAMES.ssoLogin,
        component: SsoLoginPage,
        meta: { title: '企业登录' },
      },
    ],
  },
  {
    path: '/auth/sso/callback',
    name: ROUTE_NAMES.ssoCallback,
    component: SsoCallbackPage,
    meta: { title: '企业登录回调' },
  },
  {
    path: '/403',
    name: ROUTE_NAMES.forbidden,
    component: ForbiddenPage,
    meta: { title: '无权限' },
  },
  {
    path: '/pc/operation',
    component: OperationLayout,
    children: [
      {
        path: '',
        name: ROUTE_NAMES.pcOperation,
        component: PcOperationHomePage,
        meta: {
          title: '生产操作',
          titleKey: 'operation.title',
          fallbackTitle: '生产操作',
          requiresAuth: true,
          permission: PERMISSIONS.platformOperationView,
          terminal: 'pc',
          experience: 'operation',
          workspace: 'none',
        },
      },
    ],
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
          titleKey: 'shell.navigation.workspace',
          fallbackTitle: '工作台',
          requiresAuth: true,
          permission: PERMISSIONS.platformHomeView,
          terminal: 'pc',
          workspace: 'fixed',
        },
      },
      {
        path: 'profile',
        name: ROUTE_NAMES.profile,
        component: ProfilePage,
        meta: {
          title: '个人中心',
          titleKey: 'shell.top.profile',
          fallbackTitle: '个人中心',
          requiresAuth: true,
          terminal: 'pc',
          workspace: 'none',
        },
      },
      {
        path: 'terminal-preview',
        name: ROUTE_NAMES.terminalPreview,
        component: TerminalPreviewPage,
        meta: {
          title: '终端预览',
          titleKey: 'shell.navigation.item.terminal-preview',
          fallbackTitle: '终端预览',
          requiresAuth: true,
          anyPermissions: [PERMISSIONS.platformPdaView, PERMISSIONS.platformMobileView],
          terminal: 'pc',
          workspace: 'none',
        },
      },
      {
        path: 'identity/users',
        name: ROUTE_NAMES.identityUsers,
        component: IdentityUsersPage,
        meta: {
          title: '用户管理',
          titleKey: 'shell.navigation.item.identity-users',
          fallbackTitle: '用户管理',
          requiresAuth: true,
          permission: PERMISSIONS.userView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/user-groups',
        name: ROUTE_NAMES.identityUserGroups,
        component: IdentityUserGroupsPage,
        meta: {
          title: '用户组管理',
          titleKey: 'shell.navigation.item.identity-user-groups',
          fallbackTitle: '用户组管理',
          requiresAuth: true,
          permission: PERMISSIONS.userGroupView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/roles',
        name: ROUTE_NAMES.identityRoles,
        component: IdentityRolesPage,
        meta: {
          title: '角色权限',
          titleKey: 'shell.navigation.item.identity-roles',
          fallbackTitle: '角色权限',
          requiresAuth: true,
          permission: PERMISSIONS.roleView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/permissions',
        name: ROUTE_NAMES.identityPermissions,
        component: IdentityPermissionsPage,
        meta: {
          title: '权限目录',
          titleKey: 'shell.navigation.item.identity-permissions',
          fallbackTitle: '权限目录',
          requiresAuth: true,
          permission: PERMISSIONS.permissionView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/audits',
        name: ROUTE_NAMES.identityAudits,
        component: IdentityAuditsPage,
        meta: {
          title: '登录审计',
          titleKey: 'shell.navigation.item.identity-audits',
          fallbackTitle: '登录审计',
          requiresAuth: true,
          permission: PERMISSIONS.auditLoginView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/sso/providers',
        name: ROUTE_NAMES.ssoProviders,
        component: SsoProvidersPage,
        meta: {
          title: '企业登录源',
          titleKey: 'shell.navigation.item.identity-sso-providers',
          fallbackTitle: '企业登录源',
          requiresAuth: true,
          permission: PERMISSIONS.ssoView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'identity/sso/clients',
        name: ROUTE_NAMES.ssoClients,
        component: SsoClientsPage,
        meta: {
          title: 'SSO Client',
          titleKey: 'shell.navigation.item.identity-sso-clients',
          fallbackTitle: 'SSO Client',
          requiresAuth: true,
          permission: PERMISSIONS.ssoView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/organizations',
        name: ROUTE_NAMES.systemDataOrganizations,
        component: OrganizationsPage,
        meta: {
          title: '行政组织与岗位',
          titleKey: 'shell.navigation.item.systemdata-organizations',
          fallbackTitle: '行政组织与岗位',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataOrganizationView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/assignments',
        name: ROUTE_NAMES.systemDataAssignments,
        component: AssignmentsPage,
        meta: {
          title: '用户任职',
          titleKey: 'shell.navigation.item.systemdata-assignments',
          fallbackTitle: '用户任职',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataAssignmentView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/navigation',
        name: ROUTE_NAMES.systemDataNavigation,
        component: NavigationPage,
        meta: {
          title: '导航与资源发布',
          titleKey: 'shell.navigation.item.systemdata-navigation',
          fallbackTitle: '导航与资源发布',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataNavigationView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/features',
        name: ROUTE_NAMES.systemDataFeatures,
        component: FeaturesPage,
        meta: {
          title: '功能开关',
          titleKey: 'shell.navigation.item.systemdata-features',
          fallbackTitle: '功能开关',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataFeatureView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/services',
        name: ROUTE_NAMES.systemDataServices,
        component: ServicesPage,
        meta: {
          title: '服务目录',
          titleKey: 'shell.navigation.item.systemdata-services',
          fallbackTitle: '服务目录',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataServiceCatalogView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/themes',
        name: ROUTE_NAMES.systemDataThemes,
        component: ThemesPage,
        meta: {
          title: '租户主题策略',
          titleKey: 'shell.navigation.item.systemdata-themes',
          fallbackTitle: '租户主题策略',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataThemePolicyView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      {
        path: 'systemdata/service-initialization',
        name: ROUTE_NAMES.systemDataServiceInitialization,
        component: ServiceInitializationPage,
        meta: {
          title: '服务初始化编排',
          titleKey: 'shell.navigation.item.systemdata-service-initialization',
          fallbackTitle: '服务初始化编排',
          requiresAuth: true,
          permission: PERMISSIONS.systemDataServiceInitializationView,
          terminal: 'pc',
          workspace: 'business',
        },
      },
      // DEV-only 工作区沙箱:仅注册于 DEV/E2E,生产构建不含此路由与导航入口。
      // 无权限门槛,供 12→13 上限阻断/关闭/复用/恢复 E2E 使用。
      ...(import.meta.env.DEV
        ? [
            {
              path: 'dev/workspace-tabs',
              name: ROUTE_NAMES.workspaceTabsSandbox,
              component: WorkspaceTabsSandboxPage,
              meta: {
                title: '工作区沙箱',
                requiresAuth: true,
                terminal: 'pc' as const,
                workspace: 'business' as const,
              },
            },
          ]
        : []),
      // DEV-only 视觉基线页:仅注册于 DEV/E2E,生产构建不含此路由与导航入口。
      // workspace 置 none 跳过业务标签治理,供主题/密度视觉矩阵与键盘/缩放/无横向滚动验收。
      ...(import.meta.env.DEV
        ? [
            {
              path: 'ui-baseline',
              name: ROUTE_NAMES.uiBaseline,
              component: () => import('@/pages/dev/UiBaselinePage.vue'),
              meta: {
                title: 'UI 基线',
                requiresAuth: true,
                terminal: 'pc' as const,
                workspace: 'none' as const,
              },
            },
          ]
        : []),
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
          permission: PERMISSIONS.platformPdaView,
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
          permission: PERMISSIONS.platformMobileView,
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
          permission: PERMISSIONS.platformMobileView,
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
