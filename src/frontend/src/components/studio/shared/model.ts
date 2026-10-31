import type { Json } from '@/api/studio'
export interface LayoutNode {
  id: string
  type: string
  registryVersion: string
  props: Json
  children: LayoutNode[]
  grid: { span: number; order: number; minWidth: number; [key: string]: number }
}
export interface Binding {
  from: string
  path?: (string | number)[]
  instanceId?: string
  contextBandId?: string
  value?: unknown
  nullPolicy?: string
  defaultValue?: unknown
}
export interface LedgerField {
  id: string
  name: string
  label: string
  type: string
  required: boolean
  unique: boolean
  queryable: boolean
  sortable: boolean
  retired: boolean
  [key: string]: unknown
}
export const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T
export function newNode(type = 'Text'): LayoutNode {
  return {
    id: crypto.randomUUID(),
    type,
    registryVersion: '1',
    props:
      type === 'Text' ? { text: 'Text' } : type === 'Container' ? { columns: 12, gapPx: 12 } : {},
    children: [],
    grid: { span: 12, order: 0, minWidth: 80 },
  }
}
export function initialDocument(kind: string): Json {
  if (kind === 'LedgerModel')
    return { schemaVersion: 1, fields: [], numbering: { mode: 'Manual' }, displayFieldId: '' }
  if (kind === 'Dataset')
    return {
      schemaVersion: 1,
      sourceNId: '',
      sourceConfigurationVersion: '',
      query: { kind: 'Api', operationKey: '', method: 'GET', parameterMap: [] },
      parameters: [],
      result: {
        id: 'root',
        name: 'root',
        label: 'root',
        type: 'Array',
        nullable: false,
        item: {
          id: 'row',
          name: 'row',
          label: 'row',
          type: 'Object',
          nullable: false,
          children: [],
        },
      },
      rowPolicyKey: 'public',
      readableFieldIds: [],
      timeoutSeconds: 30,
      maxRows: 10000,
      maxBytes: '10485760',
      cacheSeconds: 0,
      freshness: { mode: 'AcquisitionOnly' },
    }
  return {
    schemaVersion: 1,
    mode: 'Crud',
    parameters: [],
    instances: [],
    layout: newNode('Container'),
    fields: [],
    actions: [],
  }
}
export function nodes(root: LayoutNode): LayoutNode[] {
  return [root, ...root.children.flatMap(nodes)]
}
/** LowCode keeps a responsive 24-column flow; canvas gestures update its real order/span. */
export function syncGridPlacement(root: LayoutNode, id: string, canvasWidth: number) {
  const parent = nodes(root).find((node) => node.children.some((child) => child.id === id))
  const node = parent?.children.find((child) => child.id === id)
  if (!parent || !node) return
  const available = Math.max(1, parent === root ? canvasWidth : (parent.grid.width ?? canvasWidth))
  if (node.grid.width !== undefined)
    node.grid.span = Math.max(1, Math.min(24, Math.round((node.grid.width / available) * 24)))
  parent.children.sort((a, b) => {
    const ay = a.grid.y ?? 24 + Math.floor(a.grid.order / 2) * 150
    const by = b.grid.y ?? 24 + Math.floor(b.grid.order / 2) * 150
    return (
      ay - by ||
      (a.grid.x ?? 24 + (a.grid.order % 2) * 430) - (b.grid.x ?? 24 + (b.grid.order % 2) * 430)
    )
  })
  parent.children.forEach((child, index) => (child.grid.order = index))
}
export function resolveBinding(binding: Binding, context: Record<string, unknown>): unknown {
  if (binding.from === 'Constant') return binding.value
  let current: unknown =
    binding.from === 'Instance'
      ? (context.Instance as Json | undefined)?.[binding.instanceId ?? '']
      : context[binding.from]
  for (const part of binding.path ?? []) {
    if (['__proto__', 'constructor', 'prototype'].includes(String(part)))
      throw new Error('PATH_INVALID')
    current =
      current !== null && typeof current === 'object'
        ? (current as Record<string, unknown>)[String(part)]
        : undefined
  }
  if (current == null) {
    if (binding.nullPolicy === 'Default') return binding.defaultValue
    if (binding.nullPolicy === 'Null') return null
    throw new Error('PATH_INVALID: ' + (binding.path ?? []).join('.'))
  }
  return current
}
/** Read bindings supply untouched controls; explicit edits, including null, retain precedence. */
export function mappedFormValues(
  bindings: Json[],
  form: Json,
  context: Record<string, unknown>,
  strict = true,
): Json {
  const values = { ...form }
  for (const binding of bindings) {
    const target = binding.writeTarget
    if (typeof target !== 'string' || Object.hasOwn(values, target) || !binding.source) continue
    try {
      const value = resolveBinding(binding.source as Binding, { ...context, Form: values })
      if (value !== undefined) values[target] = value
    } catch (error) {
      if (strict) throw error
    }
  }
  return values
}
export function evaluateCondition(
  condition: Json | undefined,
  context: Record<string, unknown>,
  depth = 0,
): boolean {
  if (!condition) return true
  if (depth > 32) throw new Error('STUDIO_LIMIT: condition depth')
  if (condition.op === 'And')
    return (condition.items as Json[]).every((item) => evaluateCondition(item, context, depth + 1))
  if (condition.op === 'Or')
    return (condition.items as Json[]).some((item) => evaluateCondition(item, context, depth + 1))
  const left = resolveBinding(condition.left as Binding, context),
    right = condition.right ? resolveBinding(condition.right as Binding, context) : undefined
  switch (condition.op) {
    case 'Eq':
      return JSON.stringify(left) === JSON.stringify(right)
    case 'Ne':
      return JSON.stringify(left) !== JSON.stringify(right)
    case 'IsNull':
      return left == null
    case 'HasRows':
      return Array.isArray(left) && left.length > 0
    case 'In':
      return (
        Array.isArray(right) && right.some((item) => JSON.stringify(item) === JSON.stringify(left))
      )
    case 'Gt':
    case 'Ge':
    case 'Lt':
    case 'Le': {
      if (left == null || right == null) return false
      const a = String(left),
        b = String(right),
        numeric = /^-?\d+(\.\d+)?$/
      let result: number
      if (numeric.test(a) && numeric.test(b)) {
        const scale = Math.max(a.split('.')[1]?.length ?? 0, b.split('.')[1]?.length ?? 0)
        const exact = (value: string) =>
          BigInt(value.replace('.', '') + '0'.repeat(scale - (value.split('.')[1]?.length ?? 0)))
        const x = exact(a),
          y = exact(b)
        result = x > y ? 1 : x < y ? -1 : 0
      } else result = a > b ? 1 : a < b ? -1 : 0
      return condition.op === 'Gt'
        ? result > 0
        : condition.op === 'Ge'
          ? result >= 0
          : condition.op === 'Lt'
            ? result < 0
            : result <= 0
    }
    default:
      throw new Error('STUDIO_VALIDATION: condition.op')
  }
}
export function validateFieldBindings(
  fields: Json[],
  form: Json,
  context: Record<string, unknown>,
): void {
  for (const field of fields) {
    const target = String(field.writeTarget ?? '')
    if (!target) continue
    const value = form[target],
      empty = value == null || (typeof value === 'string' && !value.trim())
    if (!evaluateCondition(field.visibleWhen, context) || field.readOnly) continue
    if (field.requiredWhen && evaluateCondition(field.requiredWhen, context) && empty)
      throw new Error(target + ': Required')
    for (const validator of field.validators ?? []) {
      const options = validator.options ?? {}
      if (validator.kind === 'Required' && empty) throw new Error(target + ': Required')
      if (empty) continue
      if (
        validator.kind === 'Length' &&
        (String(value).length < Number(options.min ?? 0) ||
          String(value).length > Number(options.max ?? 65536))
      )
        throw new Error(target + ': Length')
      if (validator.kind === 'Pattern') {
        const pattern = String(options.pattern ?? '')
        if (pattern.length > 256 || String(value).length > 65536)
          throw new Error(target + ': Pattern limit')
        if (!new RegExp(pattern, 'u').test(String(value))) throw new Error(target + ': Pattern')
      }
      if (validator.kind === 'Range') {
        const left = { from: 'Constant', value } as Binding
        for (const [key, op] of [
          ['min', 'Ge'],
          ['max', 'Le'],
        ])
          if (
            options[key!] !== undefined &&
            !evaluateCondition(
              { op, left, right: { from: 'Constant', value: options[key!] } },
              context,
            )
          )
            throw new Error(target + ': Range')
      }
    }
  }
}
export function createHistory<T>(initial: T, limit = 100) {
  let past: T[] = [],
    future: T[] = [],
    current = clone(initial)
  return {
    get canUndo() {
      return past.length > 0
    },
    get canRedo() {
      return future.length > 0
    },
    reset(value: T) {
      current = clone(value)
      past = []
      future = []
    },
    commit(value: T) {
      if (JSON.stringify(value) === JSON.stringify(current)) return
      past.push(current)
      if (past.length > limit) past.shift()
      current = clone(value)
      future = []
    },
    undo() {
      const value = past.pop()
      if (!value) return clone(current)
      future.push(current)
      current = value
      return clone(current)
    },
    redo() {
      const value = future.pop()
      if (!value) return clone(current)
      past.push(current)
      current = value
      return clone(current)
    },
  }
}
export function logicalDelta(delta: number, zoom: number): number {
  return delta / zoom
}
export function safeTotal(value: string): number {
  const number = Number(value)
  if (!Number.isSafeInteger(number) || number < 0)
    throw new Error('STUDIO_LIMIT: table total exceeds safe display range')
  return number
}
