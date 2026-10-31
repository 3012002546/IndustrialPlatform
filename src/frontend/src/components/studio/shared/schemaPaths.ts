import type { Json } from '@/api/studio'

/** Paths are relative to the result value; schema node names are not JSON wrappers. */
export function schemaPaths(schema: Json, row = false, collectionPath: string[] = []): string[] {
  if (row) {
    for (const segment of collectionPath) {
      schema =
        schema?.type === 'Array'
          ? schema.item
          : schema?.children?.find((child: Json) => child.name === segment)
      if (!schema) return []
    }
  }
  const paths = new Set<string>()
  const visit = (node: Json, path: string[], depth: number) => {
    if (depth > 16 || !node || typeof node !== 'object') return
    if (path.length) paths.add(path.join('.'))
    if (node.type === 'Array') {
      visit(node.item, [...path, '0'], depth + 1)
    } else if (node.type === 'Object') {
      for (const child of node.children ?? []) {
        if (child.name) visit(child, [...path, String(child.name)], depth + 1)
      }
    }
  }
  visit(row && schema?.type === 'Array' ? schema.item : schema, [], 0)
  return [...paths]
}
