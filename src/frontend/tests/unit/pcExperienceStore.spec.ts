import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import {
  buildPcExperiencePreferenceKey,
  canEnterPcExperienceMode,
  resolvePcExperienceMode,
  usePcExperienceStore,
} from '@/stores/pcExperienceStore'

describe('pcExperienceStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('只按权威权限决定两种 PC 体验模式', () => {
    expect(canEnterPcExperienceMode('management', ['platform.home.view'])).toBe(true)
    expect(canEnterPcExperienceMode('operation', ['platform.operation.view'])).toBe(true)
    expect(canEnterPcExperienceMode('operation', ['operator'])).toBe(false)
    expect(resolvePcExperienceMode(['platform.home.view'])).toBe('management')
    expect(resolvePcExperienceMode(['platform.operation.view'])).toBe('operation')
    expect(resolvePcExperienceMode([])).toBeNull()
  })

  it('双权限按保存值进入,已保存模式失权时回退到仍授权模式', () => {
    const permissions = ['platform.home.view', 'platform.operation.view']
    const scope = { tenantId: 't1', userId: 'u1', device: 'pc' as const }
    localStorage.setItem(buildPcExperiencePreferenceKey(scope), 'operation')
    expect(resolvePcExperienceMode(permissions, localStorage.getItem(buildPcExperiencePreferenceKey(scope)))).toBe(
      'operation',
    )
    expect(resolvePcExperienceMode(['platform.home.view'], 'operation')).toBe('management')
  })

  it('切换只保存偏好,不清理认证或管理工作区状态', () => {
    const store = usePcExperienceStore()
    store.bind({ tenantId: 't1', userId: 'u1', device: 'pc' }, ['platform.home.view', 'platform.operation.view'])
    store.setMode('operation')
    expect(store.mode).toBe('operation')
    expect(localStorage.getItem('industrial-platform.pc.experience-mode.v1:t1:u1:pc')).toBe('operation')
  })
})
