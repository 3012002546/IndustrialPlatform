import type { QueryDescriptor } from '@/querying'

export interface AppDataTableExportContext {
  descriptor: QueryDescriptor
  filename: string
  columns: string[]
  quantity: number | 'all'
  culture: string
  timeZone: string
}

export function buildAppDataTableExportRequest(
  context: AppDataTableExportContext,
): AppDataTableExportContext {
  return {
    ...context,
    columns: [...context.columns],
  }
}
