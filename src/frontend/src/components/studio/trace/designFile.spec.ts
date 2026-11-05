import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { initialTraceDocument, newTraceObject, newTraceRelation } from './model'
import { parseTraceDesign, serializeTraceDesign, MAX_TRACE_FILE_BYTES } from './designFile'
import { traceAdapter } from './adapter'
import { createHistory } from '../shared/model'

function fixture() {
  const doc = initialTraceDocument()
  doc.objects = [newTraceObject('a', '原料'), newTraceObject('b', '成品')]
  doc.relations = [newTraceRelation('a', 'b', 'consume')]
  doc.layout.edges = [
    { relationId: 'consume', sourcePort: 'out', targetPort: 'in', vertices: [{ x: 20, y: 40 }] },
  ]
  doc.start.objectTypeIds = ['a']
  return doc
}
describe('trace configuration files', () => {
  it('round-trips vertical layout and multiple configurable detail sections while rejecting malformed sections', () => {
    const doc = fixture()
    doc.presentation = { layoutDirection: 'TopToBottom' }
    doc.objects[0]!.detail.sections = [
      {
        id: 'checks',
        label: '检验结果',
        kind: 'Table',
        datasetRevisionNId: 'dataset-r1',
        parameterMap: [
          { target: 'lot', source: { from: 'Row', path: ['lot'], nullPolicy: 'Error' } },
        ],
        collectionPath: ['rows'],
        fields: [],
        columns: [{ fieldId: 'value', label: '结果', width: 160, visible: true }],
        rowKey: { from: 'Row', path: ['id'], nullPolicy: 'Error' },
        pageSize: 20,
      },
    ]
    expect(parseTraceDesign(serializeTraceDesign('template', doc)).document).toEqual(doc)
    doc.objects[0]!.detail.sections.push(doc.objects[0]!.detail.sections[0]!)
    expect(() => parseTraceDesign(JSON.stringify(doc))).toThrow('designFileInvalid')
    doc.objects[0]!.detail.sections.pop()
    doc.objects[0]!.detail.sections[0]!.columns = null as never
    expect(() => parseTraceDesign(JSON.stringify(doc))).toThrow('designFileInvalid')
  })
  it('imports and round-trips the reusable production-chain template', () => {
    const source = readFileSync('../../docs/examples/trace/production-chain.template.json', 'utf8')
    const { document } = parseTraceDesign(source)
    expect(document.objects).toHaveLength(8)
    expect(document.relations).toHaveLength(8)
    expect(parseTraceDesign(serializeTraceDesign('生产全链路', document)).document).toEqual(
      document,
    )
  })
  it('round-trips mappings and geometry and excludes unrelated resource/runtime data', () => {
    const doc = fixture()
    doc.nodeToken = 'not-design-data'
    const file = serializeTraceDesign('生产模板', doc)
    expect(file).not.toContain('not-design-data')
    const restored = parseTraceDesign(file)
    delete doc.nodeToken
    expect(restored).toEqual({ name: '生产模板', document: doc })
    expect(parseTraceDesign('\uFEFF' + JSON.stringify(doc)).document).toEqual(doc)
  })
  it('rejects nested malformed data, wrong versions and dangling graph references before editing', () => {
    for (const change of [
      (doc: ReturnType<typeof fixture>) => {
        doc.objects[0]!.summary = null as never
      },
      (doc: ReturnType<typeof fixture>) => {
        doc.relations[0]!.fromTypeId = 'missing'
      },
      (doc: ReturnType<typeof fixture>) => {
        doc.start.identityMap.factory.path = {} as never
      },
      (doc: ReturnType<typeof fixture>) => {
        doc.objects.push(doc.objects[0]!)
      },
    ]) {
      const doc = fixture()
      change(doc)
      expect(() => parseTraceDesign(JSON.stringify(doc))).toThrow('designFileInvalid')
    }
    expect(() => parseTraceDesign('{')).toThrow('designFileInvalid')
    expect(() => parseTraceDesign(JSON.stringify({ ...fixture(), schemaVersion: 2 }))).toThrow(
      'designFileVersion',
    )
    expect(() => parseTraceDesign(' '.repeat(MAX_TRACE_FILE_BYTES + 1))).toThrow('designFileSize')
  })
  it('creates each palette grain and restores node deletion with its relations and start references', () => {
    const doc = fixture(),
      history = createHistory(doc)
    const context = {
      scope: {
        contextKey: 'test',
        definition: { kind: 'draft' as const, resourceNId: 'model', documentStamp: '1' },
      },
      expectedDocumentStamp: '1',
    }
    for (const grain of ['Batch', 'Package', 'Serial', 'WorkOrder', 'Operation', 'Custom']) {
      const value = traceAdapter.edit(
        doc,
        { type: 'addNode', catalogKey: 'trace.object.' + grain, at: { x: 20, y: 30 } },
        context,
      )
      expect(value.ok).toBe(true)
      if (value.ok) expect(value.value.document.objects.at(-1)!.identity.grain).toBe(grain)
    }
    const removed = traceAdapter.edit(
      doc,
      { type: 'remove', targets: [{ kind: 'node', id: 'a' }] },
      context,
    )
    expect(removed.ok).toBe(true)
    if (!removed.ok) return
    expect(removed.value.document.relations).toHaveLength(0)
    expect(removed.value.document.start.objectTypeIds).toEqual([])
    history.commit(removed.value.document)
    expect(history.undo()).toEqual(doc)
  })
})
