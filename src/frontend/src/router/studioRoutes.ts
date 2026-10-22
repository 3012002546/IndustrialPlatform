import type { RouteRecordRaw } from 'vue-router'
const list = () => import('@/pages/pc/studio/StudioListPage.vue')
const design = () => import('@/pages/pc/studio/StudioDesignerPage.vue')
const run = () => import('@/pages/pc/studio/StudioRuntimePage.vue')
export const studioRoutes: RouteRecordRaw[] = [
  ...[
    ['low-code', 'studio-lowcode', '低代码', 'lowcode', 'studio.lowcode.view'],
    ['sources', 'studio-sources', '数据源', 'sources', 'studio.source.view'],
    ['datasets', 'studio-datasets', '数据集', 'datasets', 'studio.dataset.view'],
    ['ledger-models', 'studio-ledger-models', '台账模型', 'ledger', 'studio.lowcode.design'],
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
