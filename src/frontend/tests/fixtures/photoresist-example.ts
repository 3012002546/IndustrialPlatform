// Operator-invoked example installer for the local development system.
// No route, automatic startup, business data mutation or archival action.
import { getStudioApi, type Json } from '@/api/studio'
import example from '@/components/studio/report/samples/photoresist-example.json'

const api = getStudioApi()
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value))
function canonical(value: unknown): string {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']'
  if (value && typeof value === 'object')
    return (
      '{' +
      Object.entries(value)
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([key, item]) => JSON.stringify(key) + ':' + canonical(item))
        .join(',') +
      '}'
    )
  return JSON.stringify(value)
}
const summary = (resource: Json) => ({
  nId: resource.nId,
  name: resource.name,
  revisionNId: resource.publishedRevisionNId,
})
async function saveExample(spec: Json) {
  const existing = (await api.list(spec.kind, { pageSize: 200, search: spec.name })).items.find(
    (r) => r.nId === spec.nId,
  )
  if (existing && (existing.name !== spec.name || existing.folder !== spec.folder))
    throw new Error('示例标识已被其他资源使用')
  const resource =
    existing ??
    (await api.create({
      nId: spec.nId,
      kind: spec.kind,
      name: spec.name,
      description: spec.description,
      folder: spec.folder,
      document: spec.document,
    }))
  const draft = await api.draft(resource.nId)
  const saved = await api.save(draft.resource, clone(spec.document))
  const reread = await api.draft(resource.nId)
  if (JSON.stringify(reread.draftDocument) !== JSON.stringify(saved.draftDocument))
    throw new Error('保存后读取不一致')
  const validation = await api.validate(resource.nId, reread.draftDocument)
  if (!validation.valid) throw new Error(JSON.stringify(validation.diagnostics))
  return { ...reread, validation }
}
export async function createSingles() {
  const result = []
  for (const spec of example.resources) {
    const saved = await saveExample(spec)
    result.push({ ...summary(saved.resource), valid: saved.validation.valid })
  }
  return result
}
export async function preview(key = 'bundle') {
  const spec = key === 'bundle' ? example.bundle : example.resources.find((r) => r.key === key)
  if (!spec) throw new Error('Unknown example')
  const draft = await api.draft(spec.nId)
  return api.reportPreview({
    kind: draft.resource.kind,
    resourceNId: spec.nId,
    document: draft.draftDocument,
    parameters: {},
  })
}
export async function previewState(nId: string) {
  return api.reportPreviewState(nId)
}
export async function createBundle() {
  const spec: Json = clone(example.bundle)
  for (const template of spec.document.assembly.templates) {
    const draft = await api.draft(template.reportNId)
    const validation = await api.validate(template.reportNId, draft.draftDocument)
    if (!validation.valid) throw new Error(JSON.stringify(validation.diagnostics))
    // Freeze example template revisions so the saved bundle can reopen independently.
    const revision = await api.publish(
      draft.resource,
      '光刻胶合成示例模板；供完整工单快照组合使用',
      validation.dependencyPlanHash,
    )
    template.revisionNId = revision.nId
  }
  const saved = await saveExample(spec)
  return {
    ...summary(saved.resource),
    valid: saved.validation.valid,
    templates: saved.draftDocument.assembly.templates.length,
  }
}
export async function verifyBundlePlan() {
  const draft = await api.draft(example.bundle.nId)
  const plan = await api.reportPlan({
    kind: 'ReportBundle',
    resourceNId: example.bundle.nId,
    document: draft.draftDocument,
    parameters: {},
  })
  const included = plan.instances.filter((i: Json) => i.decision === 'Include')
  const actual = included.map((i: Json) => ({
    key: i.ruleId,
    operation: i.operationKey?.split(':').slice(1).join(':') ?? null,
    rows: i.rowCount,
  }))
  const expected = example.expectedSections.map(({ key, operation, rows }) => ({
    key,
    operation,
    rows,
  }))
  if (canonical(actual) !== canonical(expected))
    throw new Error('组合顺序或记录数与预期不一致：' + JSON.stringify(actual))
  if (plan.instances.filter((i: Json) => i.decision === 'Skip').some((i: Json) => i.rowCount !== 0))
    throw new Error('空模板未正确跳过')
  return {
    decisions: plan.instances.length,
    included: included.length,
    skipped: plan.instances.length - included.length,
    rows: included.reduce((sum: number, i: Json) => sum + i.rowCount, 0),
    sourceManifest: plan.sourceManifest,
    orderedSections: actual,
  }
}
export async function verifyModeSave() {
  const original = await api.draft(example.bundle.nId)
  const normal = clone(original.draftDocument)
  normal.assembly.stitchMode = 'Normal'
  normal.assembly.rules = normal.assembly.templates
    .filter((t: Json) => t.scope === 'Operation')
    .map((t: Json) => ({
      id: t.id,
      name: example.resources.find((r) => r.key === t.id)?.name,
      priority: t.priority,
      reportNId: t.reportNId,
      revisionNId: t.revisionNId,
      parameterMap: t.parameterMap,
      presence: { mode: 'ParentPath', path: [t.id] },
      content: { mode: 'WholeReport' },
      required: t.required,
      emptyBehavior: 'Skip',
      headerMode: 'Inherit',
      breakBefore: 'Auto',
    }))
  try {
    await api.save(original.resource, normal)
    const normalRead = await api.draft(original.resource.nId)
    if (canonical(normalRead.draftDocument.assembly) !== canonical(normal.assembly))
      throw new Error('正常模式保存内容不一致')
    await api.save(normalRead.resource, original.draftDocument)
    const drivenRead = await api.draft(original.resource.nId)
    if (canonical(drivenRead.draftDocument) !== canonical(original.draftDocument))
      throw new Error('数据驱动模式恢复内容不一致')
    return {
      normalSavedAndRead: true,
      dataDrivenSavedAndRead: true,
      finalMode: drivenRead.draftDocument.assembly.stitchMode,
    }
  } catch (error) {
    const latest = await api.draft(original.resource.nId)
    await api.save(latest.resource, original.draftDocument)
    throw error
  }
}
