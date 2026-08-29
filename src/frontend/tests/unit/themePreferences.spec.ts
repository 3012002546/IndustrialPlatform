/**
 * 主题偏好解析/合并/存储安全回退测试(PF-01 §7.2 / §11.1)。
 * 覆盖非法 JSON、未知版本、非法枚举、字段优先级与旧侧栏键迁移读取。
 */

import { describe, expect, it } from 'vitest'

import {
  DEFAULT_UI_PREFERENCES,
  buildPcNavigationModeKey,
  buildUserUiPreferenceKey,
  mergeUiPreferences,
  parseBootstrapAppearance,
  parseUiPreferences,
  readLegacyPcSidebarCollapsed,
  readPcNavigationMode,
  readUiPreferences,
  removeLegacyPcSidebarCollapsed,
  serializeUiPreferences,
  writeUiPreferences,
  writePcNavigationMode,
  type UiPreferencesStorage,
} from '@/theme'

function storageMock(initial: Record<string, string> = {}): UiPreferencesStorage & {
  backing: Record<string, string>
} {
  const backing: Record<string, string> = { ...initial }
  return {
    backing,
    getItem: (key) => backing[key] ?? null,
    setItem: (key, value) => {
      backing[key] = value
    },
    removeItem: (key) => {
      delete backing[key]
    },
  }
}

const SCOPE = { tenantId: 't1', userId: 'u1' }
const VALID = {
  version: 1,
  palette: 'technology-blue',
  mode: 'dark',
  density: 'compact',
  pcFunctionTreeCollapsed: true,
  updatedAt: '2026-08-12T00:00:00.000Z',
} as const

describe('parseUiPreferences', () => {
  it('合法快照解析为对象', () => {
    expect(parseUiPreferences(JSON.stringify(VALID))).toEqual(VALID)
  })

  it('null 或非法 JSON → null', () => {
    expect(parseUiPreferences(null)).toBeNull()
    expect(parseUiPreferences('not-json')).toBeNull()
  })

  it('非对象值 → null', () => {
    expect(parseUiPreferences('"str"')).toBeNull()
    expect(parseUiPreferences('42')).toBeNull()
  })

  it('未知版本 → null(安全回退)', () => {
    expect(parseUiPreferences(JSON.stringify({ ...VALID, version: 2 }))).toBeNull()
  })

  it.each(['palette', 'mode', 'density'] as const)('非法 %s 枚举 → null', (field) => {
    const data = { ...VALID, [field]: 'unknown-value' }
    expect(parseUiPreferences(JSON.stringify(data))).toBeNull()
  })

  it('pcFunctionTreeCollapsed 非布尔 → null', () => {
    expect(
      parseUiPreferences(JSON.stringify({ ...VALID, pcFunctionTreeCollapsed: 'true' })),
    ).toBeNull()
  })

  it('updatedAt 非字符串 → null', () => {
    expect(parseUiPreferences(JSON.stringify({ ...VALID, updatedAt: 0 }))).toBeNull()
  })
})

describe('serialize / round-trip', () => {
  it('序列化后可无损解析', () => {
    const raw = serializeUiPreferences({ ...VALID })
    expect(parseUiPreferences(raw)).toEqual(VALID)
  })
})

describe('parseBootstrapAppearance', () => {
  it('合法 bootstrap 快照解析', () => {
    expect(
      parseBootstrapAppearance(
        JSON.stringify({ version: 1, palette: 'neutral-gray', mode: 'light', density: 'compact' }),
      ),
    ).toEqual({ version: 1, palette: 'neutral-gray', mode: 'light', density: 'compact' })
  })

  it('非法/未知版本 → null', () => {
    expect(parseBootstrapAppearance('bad')).toBeNull()
    expect(parseBootstrapAppearance(JSON.stringify({ version: 9 }))).toBeNull()
    expect(
      parseBootstrapAppearance(
        JSON.stringify({ version: 1, palette: 'pink', mode: 'light', density: 'compact' }),
      ),
    ).toBeNull()
  })
})

describe('buildUserUiPreferenceKey', () => {
  it('按 tenantId + userId 编码隔离,标识只出现在键中', () => {
    expect(buildUserUiPreferenceKey({ tenantId: '租户/甲', userId: 'u 1' })).toContain(
      'industrial-platform.ui.preferences.v1:',
    )
    expect(buildUserUiPreferenceKey({ tenantId: 'a', userId: 'b' })).toBe(
      'industrial-platform.ui.preferences.v1:a:b',
    )
    expect(buildUserUiPreferenceKey({ tenantId: 'a', userId: 'b' })).not.toBe(
      buildUserUiPreferenceKey({ tenantId: 'b', userId: 'a' }),
    )
  })
})

describe('PC navigation mode preference', () => {
  it('按租户与用户隔离并安全读写三种模式', () => {
    const storage = storageMock()
    expect(buildPcNavigationModeKey(SCOPE)).toContain('industrial-platform.pc.navigation-mode.v2:')
    expect(writePcNavigationMode(storage, SCOPE, 'compact')).toBe(true)
    expect(readPcNavigationMode(storage, SCOPE)).toBe('compact')
    expect(readPcNavigationMode(storage, { tenantId: 't2', userId: 'u2' })).toBeNull()
    storage.backing[buildPcNavigationModeKey(SCOPE)] = 'invalid'
    expect(readPcNavigationMode(storage, SCOPE)).toBeNull()
  })
})

describe('mergeUiPreferences', () => {
  it('用户显式值优先于租户默认与产品默认', () => {
    const merged = mergeUiPreferences(
      { ...DEFAULT_UI_PREFERENCES },
      { palette: 'neutral-gray', mode: 'light', density: 'compact', pcFunctionTreeCollapsed: true },
      { ...VALID },
      () => 0,
    )
    expect(merged.palette).toBe('technology-blue')
    expect(merged.mode).toBe('dark')
    expect(merged.density).toBe('compact')
    expect(merged.pcFunctionTreeCollapsed).toBe(true)
  })

  it('无用户快照时使用租户默认值', () => {
    const merged = mergeUiPreferences(
      { ...DEFAULT_UI_PREFERENCES },
      { palette: 'neutral-gray', mode: 'light' },
      null,
      () => 0,
    )
    expect(merged.palette).toBe('neutral-gray')
    expect(merged.mode).toBe('light')
    expect(merged.density).toBe('comfortable')
  })

  it('无用户与租户默认时使用产品默认值', () => {
    const merged = mergeUiPreferences({ ...DEFAULT_UI_PREFERENCES }, {}, null, () => 0)
    expect(merged).toMatchObject({
      palette: 'industrial-cyan',
      mode: 'system',
      density: 'comfortable',
      pcFunctionTreeCollapsed: false,
    })
  })

  it('有用户快照时保留其 updatedAt', () => {
    const merged = mergeUiPreferences(
      { ...DEFAULT_UI_PREFERENCES },
      {},
      { ...VALID },
      () => 1_700_000_000_000,
    )
    expect(merged.updatedAt).toBe(VALID.updatedAt)
  })

  it('无用户快照时用 now() 生成新 updatedAt', () => {
    const merged = mergeUiPreferences({ ...DEFAULT_UI_PREFERENCES }, {}, null, () => 0)
    expect(merged.updatedAt).toBe('1970-01-01T00:00:00.000Z')
  })

  it('返回的 version 恒为 1', () => {
    const merged = mergeUiPreferences({ ...DEFAULT_UI_PREFERENCES }, {}, null, () => 0)
    expect(merged.version).toBe(1)
  })
})

describe('存储读写与安全回退', () => {
  it('write → read round-trip', () => {
    const storage = storageMock()
    expect(writeUiPreferences(storage, SCOPE, { ...VALID })).toBe(true)
    expect(readUiPreferences(storage, SCOPE)).toEqual(VALID)
  })

  it('不同作用域互不串用', () => {
    const storage = storageMock()
    writeUiPreferences(storage, SCOPE, { ...VALID })
    expect(readUiPreferences(storage, { tenantId: 't2', userId: 'u2' })).toBeNull()
  })

  it('存储 getItem 抛异常 → read 返回 null 不抛出', () => {
    const storage: UiPreferencesStorage = {
      getItem: () => {
        throw new Error('SecurityError')
      },
      setItem: () => {},
      removeItem: () => {},
    }
    expect(readUiPreferences(storage, SCOPE)).toBeNull()
  })

  it('存储 setItem 抛异常 → write 返回 false 不抛出', () => {
    const storage: UiPreferencesStorage = {
      getItem: () => null,
      setItem: () => {
        throw new Error('QuotaExceededError')
      },
      removeItem: () => {},
    }
    expect(writeUiPreferences(storage, SCOPE, { ...VALID })).toBe(false)
  })

  it('旧侧栏键:键缺失 → null;值为 1 → true;值为 0 → false', () => {
    const storage = storageMock()
    expect(readLegacyPcSidebarCollapsed(storage)).toBeNull()
    storage.backing['industrial-platform.pc.sidebar.collapsed.v1'] = '1'
    expect(readLegacyPcSidebarCollapsed(storage)).toBe(true)
    storage.backing['industrial-platform.pc.sidebar.collapsed.v1'] = '0'
    expect(readLegacyPcSidebarCollapsed(storage)).toBe(false)
  })

  it('removeLegacyPcSidebarCollapsed 删除旧键', () => {
    const storage = storageMock({ 'industrial-platform.pc.sidebar.collapsed.v1': '1' })
    removeLegacyPcSidebarCollapsed(storage)
    expect(storage.getItem('industrial-platform.pc.sidebar.collapsed.v1')).toBeNull()
  })
})
