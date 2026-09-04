import type { QueryDescriptor } from '@/querying'

export type AppDataTableQueryMode = 'top' | 'header'
export type AppDataTableMode = 'list' | 'detail' | 'tree'
export type AppDataTableDensity = 'comfortable' | 'medium' | 'compact'
export type AppDataTableSortOrder = 'asc' | 'desc'
export type AppDataTableQuickExportFormat = 'csv' | 'html' | 'xml' | 'txt'
export type AppDataTableQuickExportMode = 'current' | 'selected'

export interface AppDataTableFilterOption {
  label: string
  value: string | number | boolean
}

export interface AppDataTableFilter {
  kind: 'text' | 'select' | 'date-range'
  options?: readonly AppDataTableFilterOption[]
}

export interface AppDataTableColumn {
  field: string
  title: string
  width?: number | string
  minWidth?: number | string
  sortable?: boolean
  visible?: boolean
  fixed?: 'left' | 'right'
  groupable?: boolean
  /** undefined = ordinary data column defaults to fuzzy text filter; false disables it. */
  filter?: AppDataTableFilter | false
}

export interface AppDataTableSort {
  field: string
  order: AppDataTableSortOrder
}

export interface AppDataTableRequest {
  pageIndex: number
  pageSize: number
  queryMode: AppDataTableQueryMode
  filters: Record<string, unknown>
  sort?: AppDataTableSort
  columns: string[]
  /** Shared platform query descriptor; legacy fields remain for compatibility. */
  descriptor?: QueryDescriptor
}

export interface AppDataTablePage<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface AppDataTableExportRequest {
  filename: string
  queryMode: AppDataTableQueryMode
  filters: Record<string, unknown>
  sort?: AppDataTableSort
  columns: string[]
  quantity: number | 'all'
  rows?: undefined
  descriptor?: QueryDescriptor
  culture?: string
  timeZone?: string
}

export interface AppDataTableTreeOptions<T extends object> {
  childrenField?: string
  hasChildrenField?: string
  checkStrictly?: boolean
  loadChildren?: (row: T) => Promise<readonly T[]>
}

/** Keep matching descendants visible while retaining their ancestor rows. */
export function filterAppDataTableTreeRows<T extends object>(
  rows: readonly T[],
  childrenField: string,
  matches: (row: T) => boolean,
): T[] {
  return rows.flatMap((row) => {
    const children = (row as Record<string, unknown>)[childrenField]
    const filteredChildren = Array.isArray(children)
      ? filterAppDataTableTreeRows(children as T[], childrenField, matches)
      : []
    if (!matches(row) && filteredChildren.length === 0) return []
    const childrenUnchanged =
      Array.isArray(children) &&
      filteredChildren.length === children.length &&
      filteredChildren.every((child, index) => child === children[index])
    if (!Array.isArray(children) || childrenUnchanged) return [row]
    return [{ ...row, [childrenField]: filteredChildren } as T]
  })
}

export type AppDataTableLoader<T extends object> = (
  request: AppDataTableRequest,
) => Promise<AppDataTablePage<T>>

export type AppDataTableExporter = (request: AppDataTableExportRequest) => Promise<void> | void
