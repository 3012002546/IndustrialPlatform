export function findAppDataTableRowByKey<T extends object>(
  rows: readonly T[],
  rowKey: string,
  selectedRowKey: string,
  childrenField = 'children',
): T | undefined {
  for (const row of rows) {
    if (String((row as Record<string, unknown>)[rowKey]) === String(selectedRowKey)) {
      return row
    }

    const children = (row as Record<string, unknown>)[childrenField]
    if (Array.isArray(children)) {
      const nested = findAppDataTableRowByKey(
        children as T[],
        rowKey,
        selectedRowKey,
        childrenField,
      )
      if (nested !== undefined) return nested
    }
  }

  return undefined
}
