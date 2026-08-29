export type QueryOperator =
  | 'eq'
  | 'ne'
  | 'contains'
  | 'startsWith'
  | 'gt'
  | 'ge'
  | 'lt'
  | 'le'
  | 'between'
  | 'in'

export interface QueryFilter {
  field: string
  operator: QueryOperator
  value: unknown
}

export interface QuerySort {
  field: string
  direction: 'asc' | 'desc'
}

export interface QueryDescriptor {
  readonly filters: readonly QueryFilter[]
  readonly orderBy: readonly QuerySort[]
  readonly select: readonly string[]
  readonly pageIndex: number
  readonly pageSize: number
  readonly search?: string
}

export type QueryValueType = 'string' | 'number' | 'boolean' | 'date'

export interface QueryResourceSchema {
  readonly selectable: readonly string[]
  readonly filterable: readonly string[]
  readonly sortable: readonly string[]
  readonly fieldTypes?: Readonly<Record<string, QueryValueType>>
  readonly tieBreaker?: string
}

export type QuerySchema = QueryResourceSchema
