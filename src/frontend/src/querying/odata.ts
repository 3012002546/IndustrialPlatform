import { normalizeQueryDescriptor, QueryDescriptorError } from './normalize'
import type { QueryDescriptor, QueryFilter, QueryResourceSchema, QueryValueType } from './types'

export type ODataQuery = Readonly<Record<string, string>>

function quoteString(value: string): string {
  return `'${value.replaceAll("'", "''")}'`
}

function literal(value: unknown, type: QueryValueType | undefined): string {
  if (value === null || value === undefined) throw new QueryDescriptorError('OData values cannot be empty')
  if (type === 'date' || value instanceof Date) {
    const date = value instanceof Date ? value : new Date(String(value))
    if (Number.isNaN(date.getTime())) throw new QueryDescriptorError('Invalid date value')
    return date.toISOString()
  }
  if (type === 'boolean' || typeof value === 'boolean') {
    if (typeof value !== 'boolean') throw new QueryDescriptorError('Boolean value expected')
    return value ? 'true' : 'false'
  }
  if (type === 'number' || typeof value === 'number') {
    if (typeof value !== 'number' || !Number.isFinite(value)) throw new QueryDescriptorError('Finite number expected')
    return String(value)
  }
  if (typeof value === 'string') return quoteString(value)
  throw new QueryDescriptorError('Only scalar OData values are supported')
}

function filterExpression(filter: QueryFilter, schema: QueryResourceSchema): string {
  const type = schema.fieldTypes?.[filter.field]
  const value = filter.value
  switch (filter.operator) {
    case 'contains':
      if (typeof value !== 'string') throw new QueryDescriptorError('contains requires a string')
      return `contains(${filter.field},${quoteString(value)})`
    case 'startsWith':
      if (typeof value !== 'string') throw new QueryDescriptorError('startsWith requires a string')
      return `startsWith(${filter.field},${quoteString(value)})`
    case 'between': {
      if (!Array.isArray(value) || value.length !== 2) throw new QueryDescriptorError('between requires two values')
      return `(${filter.field} ge ${literal(value[0], type)} and ${filter.field} le ${literal(value[1], type)})`
    }
    case 'in': {
      if (!Array.isArray(value) || value.length === 0) throw new QueryDescriptorError('in requires values')
      return `(${value.map((entry) => `${filter.field} eq ${literal(entry, type)}`).join(' or ')})`
    }
    default:
      return `${filter.field} ${filter.operator} ${literal(value, type)}`
  }
}

export function toODataQuery(
  descriptor: QueryDescriptor,
  schema: QueryResourceSchema,
): ODataQuery {
  const raw = descriptor as QueryDescriptor & Record<string, unknown>
  for (const disabled of ['$expand', '$apply', '$compute', '$search', '$batch', 'expand', 'apply', 'compute', 'batch']) {
    if (raw[disabled] !== undefined) throw new QueryDescriptorError(`OData option is not allowed: ${disabled}`)
  }
  if (descriptor.search !== undefined && descriptor.search.trim() !== '') {
    throw new QueryDescriptorError('OData search is not allowed')
  }
  const normalized = normalizeQueryDescriptor(descriptor, schema)
  const query: Record<string, string> = {
    $top: String(normalized.pageSize),
    $skip: String((normalized.pageIndex - 1) * normalized.pageSize),
    $count: 'true',
  }
  if (normalized.filters.length > 0) {
    query.$filter = normalized.filters.map((filter) => filterExpression(filter, schema)).join(' and ')
  }
  if (normalized.select.length > 0) query.$select = normalized.select.join(',')
  if (normalized.orderBy.length > 0) {
    query.$orderby = normalized.orderBy.map((sort) => `${sort.field} ${sort.direction}`).join(',')
  }
  return query
}

export function serializeODataQuery(query: ODataQuery): string {
  return new URLSearchParams(Object.entries(query)).toString()
}
