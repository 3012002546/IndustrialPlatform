import { describe, expect, it } from 'vitest'

import {
  buildAppDataTablePreferenceKey,
  readAppDataTablePreferences,
  writeAppDataTablePreferences,
} from '@/components/management/appDataTable/preferences'
import {
  buildAppDataTableExportRequest,
  type AppDataTableExportContext,
} from '@/components/management/appDataTable/exporting'
import type { QueryDescriptor } from '@/querying'

describe('AppDataTable public contract', () => {
  const descriptor: QueryDescriptor = {
    filters: [{ field: 'userName', operator: 'contains', value: 'alice' }],
    orderBy: [{ field: 'userName', direction: 'asc' }],
    select: ['userName', 'displayName'],
    pageIndex: 2,
    pageSize: 25,
  }

  it('keeps preference keys stable and storage failures non-fatal', () => {
    const storage = new Map<string, string>()
    const key = buildAppDataTablePreferenceKey('operator', 'identity-users', 'users')
    const fallback = {
      visibleFields: ['userName'],
      order: ['userName'],
      fixed: {},
      density: 'comfortable' as const,
      border: true,
      showIndex: false,
      widths: {},
    }

    expect(key).toBe('industrial-platform.table-preferences.v1:operator:identity-users:users')
    expect(readAppDataTablePreferences(storage, key, fallback)).toEqual(fallback)
    expect(writeAppDataTablePreferences(storage, key, { ...fallback, density: 'compact' })).toBe(
      true,
    )
    expect(readAppDataTablePreferences(storage, key, fallback).density).toBe('compact')
  })

  it('adds only export concerns around the same descriptor', () => {
    const context: AppDataTableExportContext = {
      descriptor,
      filename: 'users',
      columns: ['userName', 'displayName'],
      quantity: 'all',
      culture: 'zh-CN',
      timeZone: 'Asia/Taipei',
    }
    expect(buildAppDataTableExportRequest(context)).toEqual(context)
  })
})
