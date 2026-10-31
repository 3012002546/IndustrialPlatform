import type { RouteRecordRaw } from 'vue-router'
import { ROUTE_NAMES } from './routeNames'
const list = () => import('@/pages/pc/studio/StudioListPage.vue')
const design = () => import('@/pages/pc/studio/StudioDesignerPage.vue')
const run = () => import('@/pages/pc/studio/StudioRuntimePage.vue')
export const loadDashboardDesigner = () =>
  import('@/pages/pc/studio/dashboard/DashboardDesignerPage.vue')
const dashboardRun = () => import('@/pages/pc/studio/dashboard/DashboardRuntimePage.vue')
export const loadPlaylistDesigner = () =>
  import('@/pages/pc/studio/dashboard/PlaylistDesignerPage.vue')
const playlistRun = () => import('@/pages/pc/studio/dashboard/PlaylistRuntimePage.vue')
// 公开大屏不进入管理布局和工作区标签；访问权由服务端分享密钥决定。
export const studioScreenRoutes: RouteRecordRaw[] = [
  {
    path: '/screen/playlists/:screenId',
    name: ROUTE_NAMES.studioPlaylistScreen,
    component: playlistRun,
    meta: { title: '轮播大屏', requiresAuth: false, workspace: 'none', playlistScreen: true },
  },
  {
    path: '/screen/dashboards/:screenId',
    name: ROUTE_NAMES.studioDashboardScreen,
    component: () => import('@/pages/pc/studio/dashboard/DashboardScreenPage.vue'),
    meta: { title: '看板大屏', requiresAuth: false, workspace: 'none' },
  },
]
export const studioRoutes: RouteRecordRaw[] = [
  ...['reports', 'report-bundles'].flatMap(
    (section) =>
      [
        {
          path: `studio/${section}/:nId/design`,
          name: `studio-${section}-design`,
          component: () => import('@/pages/pc/studio/report/ReportDesignerPage.vue'),
          meta: {
            title: 'Report design',
            titleKey: 'studio.reportDesign',
            requiresAuth: true,
            permission: 'studio.report.design',
            terminal: 'pc',
            workspace: 'business',
            pageId: `studio-${section}-design`,
          },
        },
        {
          path: `studio/${section}/:nId/run`,
          name: `studio-${section}-run`,
          component: () => import('@/pages/pc/studio/report/ReportRuntimePage.vue'),
          meta: {
            title: 'Report runtime',
            titleKey: 'studio.reportRun',
            requiresAuth: true,
            permission: 'studio.report.run',
            terminal: 'pc',
            workspace: 'business',
            pageId: `studio-${section}-run`,
          },
        },
      ] as RouteRecordRaw[],
  ),
  {
    path: 'studio/workbooks',
    name: 'studio-workbooks',
    component: () => import('@/pages/pc/studio/report/WorkbookPage.vue'),
    meta: {
      title: 'Online Excel',
      titleKey: 'studio.workbooks',
      requiresAuth: true,
      permission: 'studio.workbook.view',
      terminal: 'pc',
      workspace: 'business',
      pageId: 'studio-workbooks',
    },
  },
  ...[
    ['low-code', 'studio-lowcode', '低代码', 'lowcode', 'studio.lowcode.view'],
    ['sources', 'studio-sources', '数据源', 'sources', 'studio.source.view'],
    ['datasets', 'studio-datasets', '数据集', 'datasets', 'studio.dataset.view'],
    ['ledger-models', 'studio-ledger-models', '台账模型', 'ledger', 'studio.lowcode.design'],
    ['dashboards', ROUTE_NAMES.studioDashboards, '看板', 'dashboards', 'studio.dashboard.view'],
    ['playlists', ROUTE_NAMES.studioPlaylists, '轮播', 'playlists', 'studio.dashboard.view'],
    ['reports', 'studio-reports', '单报表', 'reports', 'studio.report.view'],
    ['report-bundles', 'studio-report-bundles', '组合报表', 'reportBundles', 'studio.report.view'],
  ].map(
    ([path, name, title, key, permission]) =>
      ({
        path: 'studio/' + path,
        name,
        component: list,
        meta: {
          title,
          titleKey: 'studio.' + key,
          requiresAuth: true,
          permission,
          terminal: 'pc',
          workspace: 'business',
          pageId: name,
        },
      }) as RouteRecordRaw,
  ),
  ...[
    ['low-code', 'studio-lowcode-design', 'studio.lowcode.design'],
    ['datasets', 'studio-dataset-design', 'studio.dataset.design'],
    ['ledger-models', 'studio-ledger-design', 'studio.lowcode.design'],
  ].map(
    ([path, name, permission]) =>
      ({
        path: `studio/${path}/:nId/design`,
        name,
        component: design,
        meta: {
          title: '设计',
          titleKey: 'studio.design',
          requiresAuth: true,
          permission,
          terminal: 'pc',
          workspace: 'business',
          pageId: name,
        },
      }) as RouteRecordRaw,
  ),
  {
    path: 'studio/dashboards/:nId/design',
    name: ROUTE_NAMES.studioDashboardDesign,
    component: loadDashboardDesigner,
    meta: {
      title: '看板设计',
      titleKey: 'studio.dashboardDesign',
      requiresAuth: true,
      permission: 'studio.dashboard.design',
      terminal: 'pc',
      workspace: 'business',
      pageId: ROUTE_NAMES.studioDashboardDesign,
    },
  },
  {
    path: 'studio/dashboards/:nId/run',
    name: ROUTE_NAMES.studioDashboardRun,
    component: dashboardRun,
    meta: {
      title: '看板运行',
      titleKey: 'studio.dashboardRun',
      requiresAuth: true,
      permission: 'studio.dashboard.run',
      terminal: 'pc',
      workspace: 'business',
      pageId: ROUTE_NAMES.studioDashboardRun,
    },
  },
  {
    path: 'studio/playlists/:nId/design',
    name: ROUTE_NAMES.studioPlaylistDesign,
    component: loadPlaylistDesigner,
    meta: {
      title: '轮播设计',
      titleKey: 'studio.playlistDesign',
      requiresAuth: true,
      permission: 'studio.dashboard.design',
      terminal: 'pc',
      workspace: 'business',
      pageId: ROUTE_NAMES.studioPlaylistDesign,
    },
  },
  {
    path: 'studio/playlists/:nId/run',
    name: ROUTE_NAMES.studioPlaylistRun,
    component: playlistRun,
    meta: {
      title: '轮播运行',
      titleKey: 'studio.playlistRun',
      requiresAuth: true,
      permission: 'studio.dashboard.run',
      terminal: 'pc',
      workspace: 'business',
      pageId: ROUTE_NAMES.studioPlaylistRun,
    },
  },
  ...[
    ['run', 'studio-lowcode-run'],
    ['records/new', 'studio-lowcode-create'],
    ['records/:recordNId/edit', 'studio-lowcode-edit'],
    ['records/:recordNId', 'studio-lowcode-detail'],
  ].map(
    ([path, name]) =>
      ({
        path: `studio/low-code/:nId/${path}`,
        name,
        component: run,
        meta: {
          title: '运行',
          titleKey: 'studio.run',
          requiresAuth: true,
          permission: 'studio.lowcode.run',
          terminal: 'pc',
          workspace: 'business',
          pageId: name,
        },
      }) as RouteRecordRaw,
  ),
]
