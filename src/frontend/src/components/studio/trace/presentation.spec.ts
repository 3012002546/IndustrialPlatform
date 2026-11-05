import { describe, expect, it } from 'vitest'
import { traceDiagnosticText } from './presentation'
import { initialTraceDocument, newTraceObject } from './model'

describe('trace diagnostic presentation', () => {
  it('resolves both frontend index paths and backend stable-ID paths to names', () => {
    const doc = initialTraceDocument()
    doc.objects.push(newTraceObject('stable-id', '原料批次'))
    for (const path of [
      'objects[0].detail.datasetRevisionNId',
      'objects.stable-id.detail.datasetRevisionNId',
    ]) {
      const diagnostic = {
        id: path,
        path,
        code: 'TRACE_MAPPING_INVALID',
        severity: 'error' as const,
        message: 'Select a published detail Dataset',
      }
      expect(traceDiagnosticText(diagnostic, doc)).toEqual({
        location: '原料批次 / 明细 / 已发布数据集',
        message: '请选择已发布的明细数据集。',
      })
      expect(traceDiagnosticText(diagnostic, doc, true).message).toBe(diagnostic.message)
    }
  })
  it('retains server validation detail and does not mutate the diagnostic contract', () => {
    const doc = initialTraceDocument()
    const d = {
      id: 'd',
      path: 'start.parameterMap',
      code: 'TRACE_MAPPING_INVALID',
      severity: 'error' as const,
      message: '缺少Dataset必需参数映射：batchNo',
    }
    expect(traceDiagnosticText(d, doc)).toEqual({
      location: '起点配置 / 参数映射',
      message: '缺少数据集必需参数映射：batchNo',
    })
    expect(d.message).toContain('Dataset')
  })
})
