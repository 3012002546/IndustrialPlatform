import type { IWorkbookData } from '@univerjs/core'

export interface QueryColumn {
  field: string
  title: string
  width?: number
  numberFormat?: string
}
export type QueryRow = Record<string, unknown>
export interface QueryPage {
  /** Called only after the result replaces the worksheet successfully. */
  accepted?: () => void
  items: QueryRow[]
  total: number
  pageIndex: number
  pageSize: number
}
export interface QueryRequest {
  pageIndex: number
  pageSize: number
  signal: AbortSignal
}
export interface QuerySpreadsheetProps {
  columns: QueryColumn[]
  loader: (request: QueryRequest) => Promise<QueryPage>
  title?: string
  canExport?: boolean
  authorizeOutput?: (signal: AbortSignal) => Promise<void>
}
export interface FilterColumn {
  colId: number
  filters?: { filters?: string[]; blank?: boolean }
  customFilters?: {
    and?: number | boolean
    customFilters: { operator?: string; val: string | number }[]
  }
  colorFilters?: unknown
}
export interface SheetCapture {
  snapshot: IWorkbookData
  columns: QueryColumn[]
  rowCount: number
  hidden: number[]
  filters: FilterColumn[]
}
