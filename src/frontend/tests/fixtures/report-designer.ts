// Isolated browser fixture: the production designer with in-memory API responses.
import { createApp, h } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory, RouterView } from 'vue-router'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/base.css'
import ReportDesignerPage from '@/pages/pc/studio/report/ReportDesignerPage.vue'
import { registerStudioApi, type StudioApi, type Json } from '@/api/studio'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { initialReportDocument } from '@/components/studio/report/model'
import photoresist from '@/components/studio/report/samples/photoresist-example.json'
import { cmpSampleBundle, cmpSampleReport } from '@/components/studio/report/samples'

let doc = initialReportDocument()
const row = {
  type: 'Object',
  children: Array.from(
    { length: new URLSearchParams(location.search).has('large') ? 2000 : 12 },
    (_, i) => ({ name: `field${i}`, type: 'String' }),
  ),
}
doc.instances = [
  {
    instanceId: 'rows',
    sourceType: 'Constant',
    value: [{ field0: 'Example' }],
    parameters: [],
    dependsOn: [],
  },
]
doc.bands[0].id = 'detail'
doc.bands[0].heightMm = 50
doc.bands[0].data.instanceId = 'rows'
doc.bands[0].controls = [
  {
    id: 'label',
    type: 'Text',
    xMm: 0,
    yMm: 0,
    widthMm: 60,
    heightMm: 8,
    styleId: 'body',
    canGrow: true,
    value: { from: 'Constant', value: 'Example label', nullPolicy: 'Error' },
  },
  {
    id: 'table',
    type: 'Table',
    xMm: 0,
    yMm: 15,
    widthMm: 160,
    heightMm: 12,
    styleId: 'body',
    canGrow: true,
    flow: { mode: 'Follow', afterControlIds: [] },
    table: {
      rowMode: 'BandRow',
      repeatHeader: true,
      headerMode: 'OncePerParent',
      columns: Array.from({ length: 8 }, (_, i) => ({
        id: `col${i}`,
        label: `Column ${i}`,
        widthMm: 20,
        value: { from: 'Row', path: [`field${i}`], nullPolicy: 'Error' },
      })),
    },
  },
]
const resource = {
  nId: 'fixture',
  name: '报表设计器 · 独立验证',
  kind: 'Report',
  optimisticVersion: '1',
  concurrencyVersion: '1',
}
const bundleMode = new URLSearchParams(location.search).get('bundle')
const childId = 'c03e1b2d-a4df-44a5-973d-9df697517e3e'
if (bundleMode) {
  resource.kind = 'ReportBundle'
  doc = cmpSampleBundle(bundleMode, childId, 'revision-child')
  if (bundleMode === 'Simple') delete doc.items[0].name
  if (new URLSearchParams(location.search).has('driven')) {
    doc.assembly.stitchMode = 'DataDriven'
    doc.assembly.templates = [
      {
        id: 'candidate',
        reportNId: childId,
        revisionNId: 'revision-child',
        parameterMap: [],
        instanceMap: [],
        scope: 'Operation',
        required: true,
      },
    ]
  }
}
if (bundleMode && new URLSearchParams(location.search).has('many-templates')) {
  doc.assembly.templates = Array.from({ length: 100 }, (_, i) => ({
    ...structuredClone(doc.assembly.templates[0]),
    id: 'candidate-' + i,
    name: '工序模板 ' + (i + 1),
  }))
}
if (new URLSearchParams(location.search).has('photoresist')) {
  doc = structuredClone(photoresist.bundle.document)
  resource.kind = 'ReportBundle'
}
if (new URLSearchParams(location.search).has('json-data') && !doc.instances.length)
  doc.instances = [
    { instanceId: 'snapshot', sourceType: 'Constant', value: [], parameters: [], dependsOn: [] },
  ]
const fixture = {
  calls: [] as string[],
  loadEvents: [] as string[],
  saved: null as Json | null,
  previewInput: null as Json | null,
  exportInput: null as Json | null,
  original: structuredClone(doc),
}
if (new URLSearchParams(location.search).has('sample')) {
  Object.assign(doc, cmpSampleReport())
  fixture.original = structuredClone(doc)
}
Object.assign(window, { reportFixture: fixture })
registerStudioApi({
  revisions: async (id: string) => {
    fixture.calls.push('revisions:' + id)
    return [
      {
        nId: 'revision-child',
        revisionNo: '3',
        createdOn: '2026-11-17T01:00:00Z',
        releaseNote: '调整表头',
      },
      {
        nId: 'revision-child-v2',
        revisionNo: '2',
        createdOn: '2026-11-16T01:00:00Z',
        releaseNote: '初始发布版本',
      },
    ]
  },
  draft: async () => {
    fixture.loadEvents.push('draft-start')
    if (new URLSearchParams(location.search).has('slow-draft'))
      await new Promise((resolve) => setTimeout(resolve, 600))
    fixture.loadEvents.push('draft-end')
    return { resource, draftDocument: structuredClone(doc) }
  },
  list: async (kind: string, params: Json = {}) => {
    fixture.loadEvents.push('list:' + kind)
    if (kind === 'Report' && new URLSearchParams(location.search).has('late-child')) {
      fixture.loadEvents.push('report-page:' + (params.page ?? 1))
      if ((params.page ?? 1) === 1)
        return {
          items: Array.from({ length: 200 }, (_, i) => ({
            nId: 'other-' + i,
            name: '其他模板 ' + i,
          })),
          totalCount: 201,
        }
    }
    return new URLSearchParams(location.search).has('slow-options') ||
      (kind === 'Dataset' && new URLSearchParams(location.search).has('slow-datasets'))
      ? new Promise(() => {})
      : {
          items:
            kind === 'Report'
              ? [
                  {
                    nId: childId,
                    name: '工序检验记录',
                    publishedRevisionNo: '3',
                    publishedRevisionNId: 'revision-child',
                    pageMode: 'Fixed',
                  },
                  ...(new URLSearchParams(location.search).has('query-candidate')
                    ? [
                        {
                          nId: 'query-child',
                          name: '查询报表候选',
                          pageMode: 'Query',
                          publishedRevisionNId: 'revision-query',
                        },
                      ]
                    : []),
                ]
              : [],
        }
  },
  reportContract: async () => {
    fixture.calls.push('child-contract')
    return { entryBands: [{ id: 'detail', name: '明细' }] }
  },
  reportDraftContract: async () => {
    fixture.calls.push('contract')
    return { bandSchemas: { detail: row }, instanceSchemas: { rows: { type: 'Array', item: row } } }
  },
  save: async (_resource: unknown, value: Json) => {
    fixture.saved = JSON.parse(JSON.stringify(value))
    return { resource, draftDocument: fixture.saved }
  },
  reportPlan: async () => ({ instances: [] }),
  reportPreview: async (_input: Json, signal: AbortSignal) => {
    if (
      new URLSearchParams(location.search).has('slow-preview') &&
      !fixture.calls.includes('abort-preview')
    ) {
      return new Promise((_, reject) => {
        signal.addEventListener(
          'abort',
          () => {
            fixture.calls.push('abort-preview')
            reject(new DOMException('Preview cancelled', 'AbortError'))
          },
          { once: true },
        )
      })
    }
    fixture.previewInput = structuredClone(_input)
    return {
      status: 'Succeeded',
      pages: [
        { pageNo: 1, html: '<!doctype html><html><body><p>Engine page fixture</p></body></html>' },
      ],
    }
  },
  reportPreviewExport: async (input: Json, format: string) => {
    fixture.exportInput = JSON.parse(JSON.stringify(input))
    fixture.calls.push('export:' + format)
    return new Blob(['Preview export fixture ' + format], { type: 'application/octet-stream' })
  },
  reportPreviewState: async () => ({
    status: 'Succeeded',
    expiresOn: new Date(Date.now() + 3600000).toISOString(),
    pages: [{ pageNo: 1, contentUrl: '/fixture-page' }],
  }),
  client: {
    getBlob: async () =>
      new Blob(['<!doctype html><html><body><p>Engine page fixture</p></body></html>'], {
        type: 'text/html',
      }),
  },
} as unknown as StudioApi)
const pinia = createPinia()
const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/:pathMatch(.*)*',
      component: ReportDesignerPage,
      beforeEnter: (to) => {
        to.params.nId = 'fixture'
      },
    },
  ],
})
const app = createApp({ render: () => h(RouterView) })
app.use(pinia).use(router).use(ElementPlus)
useAuthStore(pinia).hasPermission = () => true
if (new URLSearchParams(location.search).has('english'))
  useLocalizationStore(pinia).setLocale('en-US', null)
if (new URLSearchParams(location.search).has('dark'))
  document.documentElement.dataset.ipColorMode = 'dark'
await router.isReady()
app.mount('#app')
