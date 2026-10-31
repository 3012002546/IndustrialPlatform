import { describe, expect, it, vi } from 'vitest'
import { initialReportDocument, reportBodyWidth } from '@/components/studio/report/model'
import { createStudioApi } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'

describe('report paper and API contracts', () => {
  it('keeps physical body width independent of nesting and display zoom', () => {
    const report = initialReportDocument()
    expect(reportBodyWidth(report)).toBe(180)
    report.paper.orientation = 'Landscape'
    expect(reportBodyWidth(report)).toBe(267)
    report.paper.size = 'Custom'
    report.paper.widthMm = 300
    report.paper.heightMm = 400
    report.paper.margin.left = 10
    report.paper.margin.right = 20
    expect(reportBodyWidth(report)).toBe(370)
  })
  it('keeps independent paper combinations and continuous assemblies distinct', () => {
    const simple = initialReportDocument('ReportBundle', 'Simple')
    const electronic = initialReportDocument('ReportBundle', 'ElectronicRecord')
    expect(simple.items).toEqual([])
    expect(simple).not.toHaveProperty('assembly')
    expect(electronic).not.toHaveProperty('items')
    expect(electronic.assembly.operation.collection.mode).toBe('ParentPath')
    electronic.paper.margin.left = 30
    expect(initialReportDocument().paper.margin.left).toBe(15)
  })
  it('keeps published and draft query entry points and revision strings separate', async () => {
    const http = {
      get: vi.fn(),
      post: vi.fn().mockResolvedValue({}),
      put: vi.fn(),
      delete: vi.fn(),
    } as unknown as HttpClient
    const api = createStudioApi(http)
    await api.reportQuery('report/a', {
      revisionNId: '9007199254740993',
      parameters: {},
      page: 3,
      pageSize: 20,
    })
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/reports/report%2Fa/query',
      expect.objectContaining({ revisionNId: '9007199254740993', page: 3 }),
      expect.anything(),
    )
    await api.reportQuery(null, { document: initialReportDocument(), parameters: {} })
    expect(http.post).toHaveBeenLastCalledWith(
      '/api/v1/studio/reports/query-preview',
      expect.objectContaining({ document: expect.anything() }),
      expect.anything(),
    )
  })
})
