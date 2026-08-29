import type { AppDataTableColumn, AppDataTableDensity } from '../AppDataTable'

export interface AppDataTablePreferences {
  visibleFields: string[]
  order: string[]
  fixed: Record<string, 'left' | 'right' | undefined>
  density: AppDataTableDensity
  border: boolean
  showIndex: boolean
  widths: Record<string, number | string | undefined>
}

export interface AppDataTableStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

export function buildAppDataTablePreferenceKey(
  userKey: string,
  routeKey: string,
  tableKey: string,
): string {
  return `industrial-platform.table-preferences.v1:${userKey}:${routeKey}:${tableKey}`
}

export function createDefaultAppDataTablePreferences(
  columns: readonly AppDataTableColumn[],
): AppDataTablePreferences {
  return {
    visibleFields: columns
      .filter((column) => column.visible !== false)
      .map((column) => column.field),
    order: columns.map((column) => column.field),
    fixed: Object.fromEntries(columns.map((column) => [column.field, column.fixed])) as Record<
      string,
      'left' | 'right' | undefined
    >,
    density: 'comfortable',
    border: true,
    showIndex: false,
    widths: {},
  }
}

function storageGet(storage: AppDataTableStorage | Map<string, string>, key: string): string | null {
  return storage instanceof Map ? (storage.get(key) ?? null) : storage.getItem(key)
}

function storageSet(
  storage: AppDataTableStorage | Map<string, string>,
  key: string,
  value: string,
): void {
  if (storage instanceof Map) storage.set(key, value)
  else storage.setItem(key, value)
}

export function readAppDataTablePreferences(
  storage: AppDataTableStorage | Map<string, string>,
  key: string,
  fallback: AppDataTablePreferences,
): AppDataTablePreferences {
  try {
    const parsed = JSON.parse(storageGet(storage, key) ?? 'null') as
      | Partial<AppDataTablePreferences>
      | null
    if (parsed === null) return fallback
    return {
      ...fallback,
      ...parsed,
      visibleFields:
        parsed.visibleFields?.filter((field) => fallback.order.includes(field)) ??
        fallback.visibleFields,
      order: [...fallback.order].sort(
        (a, b) => (parsed.order?.indexOf(a) ?? -1) - (parsed.order?.indexOf(b) ?? -1),
      ),
      fixed: { ...fallback.fixed, ...(parsed.fixed ?? {}) },
      widths: { ...fallback.widths, ...(parsed.widths ?? {}) },
    }
  } catch {
    return fallback
  }
}

export function writeAppDataTablePreferences(
  storage: AppDataTableStorage | Map<string, string>,
  key: string,
  value: AppDataTablePreferences,
): boolean {
  try {
    storageSet(storage, key, JSON.stringify(value))
    return true
  } catch {
    return false
  }
}
