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
import { cmpSampleReport } from '@/components/studio/report/samples'

const doc = initialReportDocument()
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
const fixture = {
  calls: [] as string[],
  saved: null as Json | null,
  original: structuredClone(doc),
}
if (new URLSearchParams(location.search).has('sample')) {
  Object.assign(doc, cmpSampleReport())
  fixture.original = structuredClone(doc)
}
Object.assign(window, { reportFixture: fixture })
registerStudioApi({
  draft: async () => ({ resource, draftDocument: structuredClone(doc) }),
  list: async () =>
    new URLSearchParams(location.search).has('slow-options')
      ? new Promise(() => {})
      : { items: [] },
  reportDraftContract: async () => {
    fixture.calls.push('contract')
    return { bandSchemas: { detail: row }, instanceSchemas: { rows: { type: 'Array', item: row } } }
  },
  save: async (_resource: unknown, value: Json) => {
    fixture.saved = JSON.parse(JSON.stringify(value))
    return { resource, draftDocument: fixture.saved }
  },
  reportPlan: async () => ({ instances: [] }),
  reportPreview: async () => ({ previewNId: 'fixture-preview' }),
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
