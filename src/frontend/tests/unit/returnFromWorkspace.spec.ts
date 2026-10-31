import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { buildTabId } from '@/workspace/identity'
import { returnFromWorkspace } from '@/workspace/returnFromWorkspace'

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

async function setup() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/design/:nId', name: 'design', component: {} },
      { path: '/list', name: 'list', component: {} },
      { path: '/denied', name: 'denied', component: {} },
    ],
  })
  const store = useWorkspaceTabsStore()
  store.bindUser({ tenantId: 'tenant', userId: 'user' })
  router.afterEach((to, _from, failure) => {
    if (failure) return
    store.requestOpen({
      id: buildTabId(String(to.name), to.params, to.query),
      title: String(to.name),
      kind: 'business',
      route: {
        name: String(to.name),
        params: to.params as Record<string, string>,
        query: to.query as Record<string, string>,
      },
    })
  })
  await router.push('/design/page?returnTo=bundle')
  const sourceId = store.activeTabId
  return { router, store, sourceId }
}

describe('returnFromWorkspace', () => {
  it('closes the source after returning to the list and keeps the destination active', async () => {
    const { router, store, sourceId } = await setup()
    await returnFromWorkspace(router, '/list')
    expect(router.currentRoute.value.path).toBe('/list')
    expect(store.tabs.some((tab) => tab.id === sourceId)).toBe(false)
    expect(store.activeTab?.route.name).toBe('list')
  })
  it('keeps the source and route when a leave guard cancels navigation', async () => {
    const { router, store, sourceId } = await setup()
    router.beforeEach(() => false)
    await returnFromWorkspace(router, '/list')
    expect(router.currentRoute.value.path).toBe('/design/page')
    expect(store.activeTabId).toBe(sourceId)
    expect(store.tabs.some((tab) => tab.id === sourceId)).toBe(true)
  })
  it('closes only the child identity when returning to a parent design page', async () => {
    const { router, store, sourceId } = await setup()
    await returnFromWorkspace(router, { name: 'design', params: { nId: 'bundle' } })
    expect(router.currentRoute.value.path).toBe('/design/bundle')
    expect(store.tabs.some((tab) => tab.id === sourceId)).toBe(false)
    expect(store.activeTabId).toBe(buildTabId('design', { nId: 'bundle' }))
  })
  it('preserves the source if navigation is redirected away from the requested destination', async () => {
    const { router, store, sourceId } = await setup()
    router.beforeEach((to) => (to.path === '/list' ? '/denied' : true))
    await returnFromWorkspace(router, '/list')
    expect(store.tabs.some((tab) => tab.id === sourceId)).toBe(true)
  })
  it('honors the workspace pin protection', async () => {
    const { router, store, sourceId } = await setup()
    store.setTabPinned(sourceId, true)
    await returnFromWorkspace(router, '/list')
    expect(store.tabs.some((tab) => tab.id === sourceId)).toBe(true)
    expect(store.activeTab?.route.name).toBe('list')
  })
})
