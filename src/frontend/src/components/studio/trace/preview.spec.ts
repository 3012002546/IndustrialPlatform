import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createStudioApi, registerStudioApi } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import type { TraceRelationPreview, TraceStartRow } from '@/api/studioTrace'
import TracePreviewPanel from './TracePreviewPanel.vue'
import { initialTraceDocument, newTraceObject } from './model'
import { tracePreviewResult } from './preview'

vi.mock('../graph/GraphWorkbench.vue', () => ({ default: { template: '<div />' } }))

const sample: TraceStartRow = {
  startKey: 'preview-credential',
  objectTypeId: 'mix',
  identity: { sourceSystem: 'CMP', factory: 'F1', businessKey: 'CMP-001' },
  values: { code: 'CMP-001', quantity: '100.000' },
}
describe('trace draft preview presentation', () => {
  it('supplies flat display cells while retaining the selected sample identity', async () => {
    const doc = initialTraceDocument()
    doc.start.columns = [{ fieldId: 'code', label: '批号', visible: true, width: 180 }]
    registerStudioApi(
      createStudioApi({
        post: vi.fn().mockResolvedValue({ items: [sample], diagnostics: [] }),
      } as unknown as HttpClient),
    )
    const wrapper = mount(TracePreviewPanel, {
      props: { document: doc, resourceNId: 'draft' },
      global: {
        plugins: [createPinia()],
        stubs: {
          AppQueryPanel: { template: '<button @click="$emit(\'submit\')">查询</button>' },
          AppDataTable: { name: 'AppDataTable', props: ['rows', 'columns'], template: '<div />' },
          TraceSearchForm: true,
          GraphWorkbench: true,
          ElAlert: true,
          ElButton: true,
          ElSelect: true,
          ElOption: true,
        },
      },
    })
    try {
      await wrapper.get('button').trigger('click')
      await flushPromises()
      const table = wrapper.findComponent({ name: 'AppDataTable' })
      expect(table.props('columns')[0].field).toBe('traceColumn0')
      expect(table.props('rows')[0]).toMatchObject({ ...sample, traceColumn0: 'CMP-001' })
    } finally {
      wrapper.unmount()
    }
  })
  for (const direction of ['Upstream', 'Downstream'] as const) {
    it(`renders the sample and distinct parallel facts for ${direction}`, () => {
      const doc = initialTraceDocument()
      doc.objects.push(newTraceObject('mix', '混配批次'))
      doc.relations.push({ id: 'r', kind: 'Genealogy' } as (typeof doc.relations)[number])
      const neighbour = {
        key: 'n',
        objectTypeId: 'raw',
        identity: { ...sample.identity, businessKey: 'RAW-001' },
        summary: [],
        grain: 'Batch',
      }
      const edge = {
        relationId: 'r',
        from: direction === 'Upstream' ? 'n' : 'anchor',
        to: direction === 'Upstream' ? 'anchor' : 'n',
        evidence: { recordKey: 'one', occurredOn: null },
      }
      const response: TraceRelationPreview = {
        resolvedInput: {},
        sourceRows: [],
        nodes: [neighbour, neighbour],
        edges: [edge, { ...edge, evidence: { ...edge.evidence, recordKey: 'two' } }],
        diagnostics: [],
      }
      const result = tracePreviewResult(response, sample, doc, 'r', direction)
      expect(result.nodes).toHaveLength(2)
      expect(result.nodes.find((n) => n.key === 'anchor')?.identity.businessKey).toBe('CMP-001')
      expect(new Set(result.edges.map((e) => e.key)).size).toBe(2)
      expect(
        result.edges.every(
          (e) =>
            result.nodes.some((n) => n.key === e.from) && result.nodes.some((n) => n.key === e.to),
        ),
      ).toBe(true)
      expect(result.nodes.every((n) => n.nodeToken === '')).toBe(true)
      expect(response.nodes).toHaveLength(2)
    })
  }
})
