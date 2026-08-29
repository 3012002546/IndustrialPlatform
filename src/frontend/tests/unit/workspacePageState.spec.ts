import { beforeEach, describe, expect, it } from 'vitest'

import {
  buildPageStateKey,
  clearPageState,
  readPageState,
  writePageState,
  type WorkspacePageState,
} from '@/workspace/pageState'

describe('workspace page state', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('按 tab identity 写入当前浏览器会话,不污染长期 localStorage', () => {
    const state: WorkspacePageState = {
      query: { keyword: 'pump' },
      pageIndex: 2,
      pageSize: 25,
      sort: [{ field: 'createdOn', direction: 'desc' }],
      scrollTop: 320,
    }
    expect(writePageState(sessionStorage, 'identity-users&p=1', state)).toBe(true)
    expect(readPageState(sessionStorage, 'identity-users&p=1')).toEqual(state)
    expect(sessionStorage.getItem(buildPageStateKey('identity-users&p=1'))).not.toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('非法状态安全回退,关闭标签清理状态', () => {
    const tabId = 'identity-users&p=1'
    sessionStorage.setItem(buildPageStateKey(tabId), '{"pageIndex":0}')
    expect(readPageState(sessionStorage, tabId)).toBeNull()
    writePageState(sessionStorage, tabId, { pageIndex: 1 })
    clearPageState(sessionStorage, tabId)
    expect(readPageState(sessionStorage, tabId)).toBeNull()
  })
})
