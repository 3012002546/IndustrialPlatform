import type {
  QueryDescriptor,
  QueryFilter,
  QueryOperator,
  QueryResourceSchema,
  QuerySort,
} from './types'

const OPERATORS: readonly QueryOperator[] = [
  'eq',
  'ne',
  'contains',
  'startsWith',
  'gt',
  'ge',
  'lt',
  'le',
  'between',
  'in',
]

export class QueryDescriptorError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'QueryDescriptorError'
  }
}

function isOperator(value: unknown): value is QueryOperator {
  return typeof value === 'string' && OPERATORS.includes(value as QueryOperator)
}

function assertField(field: string, allowed: readonly string[], kind: string): void {
  if (!allowed.includes(field)) throw new QueryDescriptorError(`${kind} field is not allowed: ${field}`)
}

function assertFilterValue(filter: QueryFilter): void {
  if (filter.value === null || filter.value === undefined) {
    throw new QueryDescriptorError(`Empty value is not allowed: ${filter.field}`)
  }
  if (filter.operator === 'between') {
    if (!Array.isArray(filter.value) || filter.value.length !== 2 || filter.value.some((value) => value === null || value === undefined)) {
      throw new QueryDescriptorError(`between requires two values: ${filter.field}`)
    }
  }
  if (filter.operator === 'in') {
    if (!Array.isArray(filter.value) || filter.value.length === 0 || filter.value.length > 100) {
      throw new QueryDescriptorError(`in requires 1 to 100 values: ${filter.field}`)
    }
    if (filter.value.some((value) => value === null || value === undefined)) {
      throw new QueryDescriptorError(`in does not accept empty values: ${filter.field}`)
    }
  }
}

function normalizeFilter(
  value: QueryFilter,
  schema: QueryResourceSchema,
): QueryFilter {
  if (typeof value.field !== 'string' || value.field.length === 0) {
    throw new QueryDescriptorError('Filter field is required')
  }
  if (!isOperator(value.operator)) throw new QueryDescriptorError(`Invalid query operator: ${String(value.operator)}`)
  assertField(value.field, schema.filterable, 'Filter')
  assertFilterValue(value)
  return { field: value.field, operator: value.operator, value: value.value }
}

function normalizeSort(value: QuerySort, schema: QueryResourceSchema): QuerySort {
  if (typeof value.field !== 'string' || value.field.length === 0) {
    throw new QueryDescriptorError('Sort field is required')
  }
  if (value.direction !== 'asc' && value.direction !== 'desc') {
    throw new QueryDescriptorError(`Invalid sort direction: ${String(value.direction)}`)
  }
  assertField(value.field, schema.sortable, 'Sort')
  return { field: value.field, direction: value.direction }
}

export function normalizeQueryDescriptor(
  input: QueryDescriptor,
  schema: QueryResourceSchema,
): QueryDescriptor {
  if (!Number.isFinite(input.pageIndex) || !Number.isFinite(input.pageSize)) {
    throw new QueryDescriptorError('Paging values must be finite numbers')
  }
  const filters = input.filters.map((filter) => normalizeFilter(filter, schema))
  if (filters.length > 20) throw new QueryDescriptorError('At most 20 filters are allowed')
  const duplicateKeys = new Set<string>()
  for (const filter of filters) {
    const key = `${filter.field}\u0000${filter.operator}`
    if (duplicateKeys.has(key)) throw new QueryDescriptorError(`Duplicate filter: ${filter.field}`)
    duplicateKeys.add(key)
  }

  if (input.orderBy.length > 3) throw new QueryDescriptorError('At most 3 sort fields are allowed')
  const orderBy = input.orderBy.map((sort) => normalizeSort(sort, schema))
  const sortFields = new Set<string>()
  for (const sort of orderBy) {
    if (sortFields.has(sort.field)) throw new QueryDescriptorError(`Duplicate sort field: ${sort.field}`)
    sortFields.add(sort.field)
  }
  if (schema.tieBreaker !== undefined && !sortFields.has(schema.tieBreaker) && orderBy.length < 3) {
    assertField(schema.tieBreaker, schema.sortable, 'Sort')
    orderBy.push({ field: schema.tieBreaker, direction: 'asc' })
  }

  const select = [...new Set(input.select)].sort()
  select.forEach((field) => assertField(field, schema.selectable, 'Select'))
  const search = input.search
  if (search !== undefined && typeof search !== 'string') {
    throw new QueryDescriptorError('Search must be a string')
  }
  return {
    filters,
    orderBy,
    select,
    pageIndex: Math.max(1, Math.trunc(input.pageIndex)),
    pageSize: Math.min(100, Math.max(1, Math.trunc(input.pageSize))),
    ...(search === undefined ? {} : { search }),
  }
}
