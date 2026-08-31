import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import AppDataTable from '@/components/management/AppDataTable.vue'
import { setCurrentSession } from '@/auth/gateway'
import { makeAuthSession } from '../fixtures/session'
import { clearCurrentUserUiCache } from '@/stores/uiCacheStore'
import { buildPageStateKey } from '@/workspace/pageState'
import {
  buildAppDataTablePreferenceKey,
  buildScopedAppDataTableUserKey,
  createDefaultAppDataTablePreferences,
} from '@/components/management/appDataTable/preferences'

describe('AppDataTable UI cache reset', () => {
  const scope = { tenantId: 't1', userId: 'u1' }
  const otherScope = { tenantId: 't2', userId: 'u2' }

  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    setActivePinia(createPinia())
    setCurrentSession(makeAuthSession(['identity.user.view']))
  })

  afterEach(() => {
    setCurrentSession(null)
    document.body.innerHTML = ''
  })

  it('clears mounted table memory and current scoped storage without touching another user', async () => {
    const currentPreferenceKey = buildAppDataTablePreferenceKey(
      buildScopedAppDataTableUserKey(scope),
      'identity-users',
      'users',
    )
    const otherPreferenceKey = buildAppDataTablePreferenceKey(
      buildScopedAppDataTableUserKey(otherScope),
      'identity-users',
      'users',
    )
    const currentDefaults = createDefaultAppDataTablePreferences([
      { field: 'loginName', title: 'Login name' },
    ])
    localStorage.setItem(
      currentPreferenceKey,
      JSON.stringify({ ...currentDefaults, density: 'compact', border: false, showIndex: true }),
    )
    localStorage.setItem(otherPreferenceKey, 'other-user-table')
    sessionStorage.setItem(buildPageStateKey(scope, 'identity-users'), 'current-page')
    sessionStorage.setItem(buildPageStateKey(otherScope, 'identity-users'), 'other-user-page')

    const loader = vi.fn(async (request: { pageIndex: number; pageSize: number }) => ({
      items: [],
      total: 0,
      pageIndex: request.pageIndex,
      pageSize: request.pageSize,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'users',
        routeKey: 'identity-users',
        columns: [{ field: 'loginName', title: 'Login name', filter: { kind: 'text' } }],
        loader,
      },
      global: {
        plugins: [createPinia()],
        stubs: { 'el-pagination': true },
      },
    })

    const vm = wrapper.vm as unknown as {
      preferences: { density: string; border: boolean; showIndex: boolean }
      topQuery: Record<string, unknown>
      headerFilters: Record<string, unknown>
      currentPage: number
      setTopQuery: (value: Record<string, unknown>) => void
      switchQueryMode: (mode: 'top' | 'header') => void
      setHeaderFilter: (field: string, value: unknown) => void
    }
    vm.setTopQuery({ loginName: 'old-user' })
    vm.switchQueryMode('header')
    vm.setHeaderFilter('loginName', 'old-header-user')
    vm.preferences.density = 'compact'
    vm.preferences.border = false
    vm.preferences.showIndex = true
    await flushPromises()
    expect(vm.topQuery).toEqual({})
    expect(vm.headerFilters).toEqual({ loginName: 'old-header-user' })

    const queryChangesBeforeClear = wrapper.emitted('query-change')?.length ?? 0
    clearCurrentUserUiCache(scope)
    await flushPromises()
    await wrapper.vm.$nextTick()
    await flushPromises()

    expect(vm.topQuery).toEqual({})
    expect(vm.headerFilters).toEqual({})
    expect(vm.currentPage).toBe(1)
    expect(vm.preferences).toEqual(expect.objectContaining({
      density: 'comfortable',
      border: true,
      showIndex: false,
    }))
    expect(wrapper.emitted('query-change')?.length ?? 0).toBe(queryChangesBeforeClear)
    expect(loader).toHaveBeenCalled()
    expect(localStorage.getItem(currentPreferenceKey)).toBeNull()
    expect(localStorage.getItem(otherPreferenceKey)).toBe('other-user-table')
    expect(sessionStorage.getItem(buildPageStateKey(scope, 'identity-users'))).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey(otherScope, 'identity-users'))).toBe('other-user-page')

    wrapper.unmount()
  })
})
