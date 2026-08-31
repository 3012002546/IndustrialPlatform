import { beforeEach, describe, expect, it } from 'vitest'

import {
  buildPageStateKey,
  clearPageState,
  readPageState,
  writePageState,
  type WorkspacePageState,
} from '@/workspace/pageState'
import type { UserUiScope } from '@/theme/types'

describe('workspace page state', () => {
  const scope: UserUiScope = { tenantId: 'tenant-1', userId: 'user-1' }

  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('按 tab identity 写入当前浏览器会话,不污染长期 localStorage', () => {
    const state: WorkspacePageState = {
      query: { keyword: 'pump' },
      queryMode: 'header',
      headerFilters: { loginName: 'pump', createdOn: ['2026-01-01', '2026-01-31'] },
      pageIndex: 2,
      pageSize: 25,
      sort: [{ field: 'createdOn', direction: 'desc' }],
      scrollTop: 320,
    }
    expect(writePageState(sessionStorage, scope, 'identity-users&p=1', state)).toBe(true)
    expect(readPageState(sessionStorage, scope, 'identity-users&p=1')).toEqual(state)
    expect(sessionStorage.getItem(buildPageStateKey(scope, 'identity-users&p=1'))).not.toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('同一 tab id 也按 tenant/user 隔离,非法状态安全回退并可清理', () => {
    const tabId = 'identity-users&p=1'
    const otherScope: UserUiScope = { tenantId: 'tenant-2', userId: 'user-2' }
    sessionStorage.setItem(buildPageStateKey(scope, tabId), '{"pageIndex":0}')
    expect(readPageState(sessionStorage, scope, tabId)).toBeNull()
    expect(readPageState(sessionStorage, otherScope, tabId)).toBeNull()
    writePageState(sessionStorage, scope, tabId, { pageIndex: 1 })
    writePageState(sessionStorage, otherScope, tabId, { pageIndex: 2 })
    expect(readPageState(sessionStorage, scope, tabId)).toEqual({ pageIndex: 1 })
    expect(readPageState(sessionStorage, otherScope, tabId)).toEqual({ pageIndex: 2 })
    clearPageState(sessionStorage, scope, tabId)
    expect(readPageState(sessionStorage, scope, tabId)).toBeNull()
    expect(readPageState(sessionStorage, otherScope, tabId)).toEqual({ pageIndex: 2 })
  })
})
