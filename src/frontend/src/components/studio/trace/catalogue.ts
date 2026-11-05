import type { TraceObject } from './model'
export const traceObjectKinds: { grain: TraceObject['identity']['grain']; name: string }[] = [
  { grain: 'Batch', name: '批次' },
  { grain: 'Package', name: '包装' },
  { grain: 'Serial', name: '单件' },
  { grain: 'WorkOrder', name: '工单' },
  { grain: 'Operation', name: '工序' },
  { grain: 'Custom', name: '自定义' },
]
