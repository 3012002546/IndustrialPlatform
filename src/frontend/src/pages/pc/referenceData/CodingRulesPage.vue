<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type { CodingRuleApi } from '@/api/referenceData/codingRules'
import type {
  CodePreview,
  CodingResetPolicy,
  CodingRuleDetail,
  CodingRuleSummary,
  CreateCodingRuleRequest,
  GenerateCodeRequest,
  GeneratedCode,
} from '@/api/referenceData/codingRuleTypes'
import type {
  PublicationStatus,
  ReferenceDataQuery,
  ReferenceScope,
} from '@/api/referenceData/types'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type {
  AppDataTableColumn,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import { localeMessages } from '@/localization/i18n'
import { PERMISSIONS, PermissionGate, usePermission } from '@/permissions'
import { useLocalizationStore } from '@/stores/localizationStore'

const api = getReferenceDataApi() as unknown as CodingRuleApi | null
const localization = useLocalizationStore()
const { has } = usePermission()
const codingCopy = {
  'zh-CN': {
    title: '编码规则',
    description: '维护版本化编码模板，并按显式来源、修订和幂等键预览或生成编码。',
    createRule: '新建编码规则',
    editRule: '编辑编码规则',
    ruleDetail: '编码规则详情',
    targetEntity: '目标实体标识',
    template: '编码模板',
    resetPolicy: '重置周期',
    resetNever: '不重置',
    resetYearly: '每年',
    resetMonthly: '每月',
    resetDaily: '每日',
    tokenTitle: '允许的 Token',
    tokenList: '{YYYY} {MM} {DD} {TENANT} {FACTORY} {SEQ:n}',
    tokenHint: 'n 必须为 1～12，且模板必须且只能包含一个 {SEQ:n}。',
    factory: '工厂上下文',
    factoryUnavailable: '本阶段尚未开放工厂上下文；{FACTORY} 暂不可使用。',
    templateInvalid: '模板 Token、序列宽度或重置周期不符合约束。',
    empty: '暂无编码规则，可新建第一条租户规则。',
    codeTools: '编码工具',
    previewDraft: '预览草稿',
    previewRuntime: '预览固定修订',
    generate: '生成编码',
    sourceScope: '规则来源',
    sourceTenant: '来源租户',
    ruleRevision: '规则修订',
    idempotencyKey: '幂等键',
    newKey: '新建幂等键',
    previewResult: '预览结果',
    generatedResult: '生成结果',
    noSequence: '预览不会占用序列号。',
    sequence: '序列号',
    period: '周期分区',
    publishHint: '发布会原子替代同一作用域的当前修订；旧修订仍可按固定来源读取。',
    disableHint: '停用后不再作为当前规则；曾发布修订仍可按固定来源读取。',
    idempotencyRequired: '生成编码前请填写幂等键；重试必须复用同一个键。',
    idempotencyConflict: '该幂等键已用于不同请求，请使用新的幂等键。',
  },
  'en-US': {
    title: 'Coding rules',
    description:
      'Maintain versioned code templates, then preview or generate with an explicit source, revision, and idempotency key.',
    createRule: 'New coding rule',
    editRule: 'Edit coding rule',
    ruleDetail: 'Coding rule details',
    targetEntity: 'Target entity ID',
    template: 'Template',
    resetPolicy: 'Reset policy',
    resetNever: 'Never',
    resetYearly: 'Yearly',
    resetMonthly: 'Monthly',
    resetDaily: 'Daily',
    tokenTitle: 'Allowed tokens',
    tokenList: '{YYYY} {MM} {DD} {TENANT} {FACTORY} {SEQ:n}',
    tokenHint: 'n must be from 1 to 12, and the template must contain exactly one {SEQ:n}.',
    factory: 'Factory context',
    factoryUnavailable: 'Factory context is unavailable in this release; {FACTORY} is disabled.',
    templateInvalid: 'The template tokens, sequence width, or reset policy are invalid.',
    empty: 'No coding rules yet. Create the first tenant rule.',
    codeTools: 'Code tools',
    previewDraft: 'Preview draft',
    previewRuntime: 'Preview fixed revision',
    generate: 'Generate',
    sourceScope: 'Rule source',
    sourceTenant: 'Source tenant',
    ruleRevision: 'Rule revision',
    idempotencyKey: 'Idempotency key',
    newKey: 'New idempotency key',
    previewResult: 'Preview result',
    generatedResult: 'Generated result',
    noSequence: 'Preview does not consume a sequence number.',
    sequence: 'Sequence',
    period: 'Period partition',
    publishHint:
      'Publishing atomically replaces the current revision in this scope. Fixed historical revisions remain readable.',
    disableHint:
      'The rule will no longer be current. Previously published revisions remain readable by explicit source.',
    idempotencyRequired:
      'Enter an idempotency key before generating. Retries must reuse the same key.',
    idempotencyConflict: 'This idempotency key belongs to another request. Use a new key.',
  },
} as const
const copy = computed(() => ({
  ...localeMessages[localization.locale].referenceData,
  ...codingCopy[localization.locale],
}))

const table = ref<{
  reload: () => Promise<void>
  setTopQuery: (query: Record<string, unknown>) => void
}>()
const mode = ref<AppDataTableQueryMode>('top')
const query = reactive({ keyword: '', scopeType: '', status: '' })
const total = ref(0)
const firstLoading = ref(true)
const listError = ref('')
const error = ref('')
const traceId = ref('')
const conflict = ref(false)
const validation = ref<Record<string, string>>({})
const busy = ref(false)
let listRequest: AbortController | undefined
let detailRequest: AbortController | undefined
let detailSequence = 0

const scopeOptions = computed(() => [
  { value: 'Tenant', label: copy.value.tenant },
  { value: 'Platform', label: copy.value.platform },
])
const statusOptions = computed(() => [
  { value: 'Draft', label: copy.value.draft },
  { value: 'Published', label: copy.value.published },
  { value: 'Superseded', label: copy.value.superseded },
  { value: 'Disabled', label: copy.value.disabled },
])
const resetOptions = computed(() => [
  { value: 'Never', label: copy.value.resetNever },
  { value: 'Yearly', label: copy.value.resetYearly },
  { value: 'Monthly', label: copy.value.resetMonthly },
  { value: 'Daily', label: copy.value.resetDaily },
])
const columns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 140, sortable: true, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 170, sortable: true },
  { field: 'targetEntityNId', title: copy.value.targetEntity, minWidth: 140, filter: false },
  {
    field: 'scopeType',
    title: copy.value.scope,
    width: 100,
    filter: { kind: 'select', options: scopeOptions.value },
  },
  { field: 'revision', title: copy.value.revision, width: 82, sortable: true, filter: false },
  {
    field: 'status',
    title: copy.value.status,
    width: 112,
    sortable: true,
    filter: { kind: 'select', options: statusOptions.value },
  },
  { field: 'resetPolicy', title: copy.value.resetPolicy, width: 115, filter: false },
  {
    field: 'lastUpdatedOn',
    title: copy.value.updatedOn,
    minWidth: 170,
    sortable: true,
    filter: false,
  },
])

function statusLabel(status: PublicationStatus): string {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function resetLabel(reset: CodingResetPolicy): string {
  return resetOptions.value.find((item) => item.value === reset)?.label ?? reset
}
function date(value: string): string {
  return new Intl.DateTimeFormat(localization.locale, {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: localization.preferences.timeZone,
  }).format(new Date(value))
}
function clearError(): void {
  error.value = ''
  traceId.value = ''
  conflict.value = false
  validation.value = {}
}
function report(caught: unknown): void {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  const details = caught instanceof ApiError ? caught.details : undefined
  conflict.value = details?.code === 'REF-CONCURRENCY-CONFLICT'
  const messages: Record<string, string> = {
    'REF-CODING-TEMPLATE-INVALID': copy.value.templateInvalid,
    'REF-CODING-RULE-NOT-FOUND': copy.value.notFound,
    'REF-IDEMPOTENCY-REQUIRED': copy.value.idempotencyRequired,
    'REF-IDEMPOTENCY-CONFLICT': copy.value.idempotencyConflict,
    'REF-SCOPE-FACTORY-NOT-READY': copy.value.factoryUnavailable,
    'REF-INVALID-STATE': copy.value.stateInvalid,
    'REF-VALIDATION-FAILED': copy.value.invalid,
  }
  error.value = conflict.value
    ? copy.value.conflict
    : details?.status === 403
      ? copy.value.forbidden
      : details?.status === 404
        ? copy.value.notFound
        : (messages[details?.code ?? ''] ??
          (details?.status === 400 || details?.status === 422
            ? copy.value.invalid
            : copy.value.unavailable))
  traceId.value = details?.traceId ?? ''
  const field = details?.parameters?.field
  if (typeof field === 'string') validation.value[field] = error.value
}
function version(row: Pick<CodingRuleSummary, 'optimisticVersion' | 'concurrencyVersion'>) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
}
function scopeWritable(row: Pick<CodingRuleSummary, 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: CodingRuleSummary): boolean {
  return (
    row.status === 'Draft' && scopeWritable(row) && has(PERMISSIONS.referenceDataCodingRuleUpdate)
  )
}
function canClone(row: CodingRuleSummary): boolean {
  return (
    row.publishedOn !== null && scopeWritable(row) && has(PERMISSIONS.referenceDataCodingRuleCreate)
  )
}
function canPublish(row: CodingRuleSummary): boolean {
  return (
    row.status === 'Draft' && scopeWritable(row) && has(PERMISSIONS.referenceDataCodingRulePublish)
  )
}
function canDisable(row: CodingRuleSummary): boolean {
  return (
    (row.status === 'Draft' || row.status === 'Published') &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataCodingRuleDisable)
  )
}
function canUseTools(row: CodingRuleSummary): boolean {
  return (
    has(PERMISSIONS.referenceDataCodingRulePreview) ||
    (row.publishedOn !== null && has(PERMISSIONS.referenceDataCodingRuleGenerate))
  )
}

async function load(request: AppDataTableRequest) {
  listRequest?.abort()
  listRequest = new AbortController()
  const params: ReferenceDataQuery = {
    pageIndex: request.pageIndex,
    pageSize: Math.min(request.pageSize, 100),
    keyword: String(
      request.queryMode === 'top' ? (request.filters.keyword ?? '') : (request.filters.name ?? ''),
    ),
    scopeType: String(request.filters.scopeType ?? ''),
    status: String(request.filters.status ?? ''),
    ...(request.sort
      ? { sortField: request.sort.field, descending: request.sort.order === 'desc' }
      : {}),
  }
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const result = await api.listCodingRules(params, { signal: listRequest.signal })
    if (activeId.value && !result.items.some((item) => item.id === activeId.value))
      activeId.value = null
    return result
  } finally {
    firstLoading.value = false
  }
}
function onLoaded(page: { total: number }): void {
  total.value = page.total
  listError.value = ''
}
function reportList(caught: unknown): void {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  report(caught)
  listError.value = error.value
}
function search(): void {
  table.value?.setTopQuery({ ...query })
}
function reset(): void {
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
  search()
}
function switchMode(value: AppDataTableQueryMode): void {
  mode.value = value
  if (value === 'top') table.value?.setTopQuery({ ...query })
}

function setActive(row: CodingRuleSummary): void {
  activeId.value = row.id
}

type FormMode = 'create' | 'edit' | 'view'
const formOpen = ref(false)
const formMode = ref<FormMode>('view')
const selected = ref<CodingRuleDetail | null>(null)
const activeId = ref<string | null>(null)
const savedSnapshot = ref('')
const form = reactive({
  nId: '',
  name: '',
  scopeType: 'Tenant' as ReferenceScope,
  targetEntityNId: '',
  template: '{SEQ:4}',
  resetPolicy: 'Never' as CodingResetPolicy,
})
const dirty = computed(
  () => formOpen.value && formMode.value !== 'view' && JSON.stringify(form) !== savedSnapshot.value,
)
const isReadOnly = computed(() => formMode.value === 'view')
const canSubmit = computed(
  () =>
    formMode.value !== 'view' &&
    !busy.value &&
    (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage)),
)

function payload(): CreateCodingRuleRequest {
  return {
    scopeType: form.scopeType,
    nId: form.nId.trim(),
    name: form.name.trim(),
    targetEntityNId: form.targetEntityNId.trim(),
    template: form.template,
    resetPolicy: form.resetPolicy,
  }
}
function fill(item: CodingRuleDetail | null): void {
  selected.value = item
  Object.assign(form, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    scopeType: item?.scopeType ?? 'Tenant',
    targetEntityNId: item?.targetEntityNId ?? '',
    template: item?.template ?? '{SEQ:4}',
    resetPolicy: item?.resetPolicy ?? 'Never',
  })
  savedSnapshot.value = JSON.stringify(form)
}
async function create(): Promise<void> {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  clearError()
  fill(null)
  formMode.value = 'create'
  formOpen.value = true
}
async function open(row: CodingRuleSummary, edit = false): Promise<void> {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  clearError()
  busy.value = true
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const item = await api.getCodingRule(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = edit && canEdit(item) ? 'edit' : 'view'
    formOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function allowDiscard(): Promise<boolean> {
  if (busy.value && formOpen.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.title, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
function closeFormNow(): void {
  detailSequence++
  detailRequest?.abort()
  formOpen.value = false
  selected.value = null
  busy.value = false
}
async function closeForm(value = false): Promise<void> {
  if (!value && (await allowDiscard())) {
    closeFormNow()
    clearError()
  }
}
function validNId(value: string): boolean {
  return /^[A-Za-z][A-Za-z0-9_.-]{1,63}$/.test(value.trim())
}
function validTemplate(): boolean {
  const template = form.template
  if (!template.trim() || template.length > 1024 || /[\u0000-\u001f\u007f]/.test(template))
    return false
  const tokens: string[] = template.match(/\{[^{}]*\}/g) ?? []
  if (template.replace(/\{[^{}]*\}/g, '').match(/[{}]/)) return false
  const sequences = tokens.filter((token) => /^\{SEQ:(?:[1-9]|1[0-2])\}$/.test(token))
  if (sequences.length !== 1) return false
  if (
    tokens.some(
      (token) =>
        !['{YYYY}', '{MM}', '{DD}', '{TENANT}'].includes(token) &&
        !/^\{SEQ:(?:[1-9]|1[0-2])\}$/.test(token),
    )
  )
    return false
  if (form.resetPolicy === 'Yearly' && !tokens.includes('{YYYY}')) return false
  if (form.resetPolicy === 'Monthly' && (!tokens.includes('{YYYY}') || !tokens.includes('{MM}')))
    return false
  if (
    form.resetPolicy === 'Daily' &&
    (!tokens.includes('{YYYY}') || !tokens.includes('{MM}') || !tokens.includes('{DD}'))
  )
    return false
  return true
}
function validate(): boolean {
  validation.value = {}
  if (!validNId(form.nId)) validation.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) validation.value.name = copy.value.required
  if (!validNId(form.targetEntityNId)) validation.value.targetEntityNId = copy.value.invalidNId
  if (!validTemplate()) validation.value.template = copy.value.templateInvalid
  return Object.keys(validation.value).length === 0
}
async function save(): Promise<void> {
  if (!api || !canSubmit.value || !validate()) return
  busy.value = true
  clearError()
  try {
    const request = payload()
    const result =
      formMode.value === 'create'
        ? await api.createCodingRule(request)
        : await api.updateCodingRule(selected.value!.id, {
            name: request.name,
            targetEntityNId: request.targetEntityNId,
            template: request.template,
            resetPolicy: request.resetPolicy,
            ...version(selected.value!),
          })
    closeFormNow()
    ElMessage.success(`${copy.value.saved}: ${result.nId}`)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function reloadForm(): Promise<void> {
  if (!api || !selected.value) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  busy.value = true
  try {
    const item = await api.getCodingRule(selected.value.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = canEdit(item) ? 'edit' : 'view'
    clearError()
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function copyUnsaved(): Promise<void> {
  try {
    await navigator.clipboard.writeText(JSON.stringify(payload(), null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.warning(copy.value.copyFailed)
  }
}
async function clone(row: CodingRuleSummary): Promise<void> {
  if (!api || !canClone(row) || busy.value || !(await allowDiscard())) return
  busy.value = true
  clearError()
  try {
    const item = await api.cloneCodingRule(row.id, version(row))
    fill(item)
    formMode.value = 'edit'
    formOpen.value = true
    ElMessage.success(copy.value.cloned)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function publish(row: CodingRuleSummary): Promise<void> {
  if (!api || !canPublish(row) || busy.value) return
  try {
    await ElMessageBox.confirm(copy.value.publishHint, copy.value.publish, {
      confirmButtonText: copy.value.publish,
      cancelButtonText: copy.value.cancel,
      type: 'warning',
    })
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    await api.publishCodingRule(row.id, version(row))
    ElMessage.success(copy.value.publishedSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disable(row: CodingRuleSummary): Promise<void> {
  if (!api || !canDisable(row) || busy.value) return
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(copy.value.disableHint, copy.value.reason, {
      confirmButtonText: copy.value.disable,
      cancelButtonText: copy.value.cancel,
      inputValidator: (value) =>
        (Boolean(value?.trim()) && value!.length <= 1000) || copy.value.required,
    })
    reason = result.value
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    await api.disableCodingRule(row.id, { ...version(row), changeReason: reason })
    ElMessage.success(copy.value.disabledSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

const toolsOpen = ref(false)
const toolsBusy = ref(false)
const toolsRow = ref<CodingRuleSummary | null>(null)
const toolsForm = reactive({
  sourceScope: 'Tenant' as ReferenceScope,
  sourceTenantNId: '',
  ruleRevision: 1,
  idempotencyKey: '',
})
const previewResult = ref<CodePreview | null>(null)
const generatedResult = ref<GeneratedCode | null>(null)
let toolsRequest: AbortController | undefined
let toolsSequence = 0
const runtimeAvailable = computed(() => toolsRow.value?.publishedOn !== null)

function createIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  const bytes = new Uint8Array(16)
  globalThis.crypto?.getRandomValues(bytes)
  return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
}
async function openTools(row: CodingRuleSummary): Promise<void> {
  if (!canUseTools(row) || !(await allowDiscard())) return
  closeFormNow()
  toolsSequence++
  toolsRequest?.abort()
  clearError()
  toolsRow.value = row
  Object.assign(toolsForm, {
    sourceScope: row.scopeType,
    sourceTenantNId: row.tenantNId ?? '',
    ruleRevision: row.revision,
    idempotencyKey: createIdempotencyKey(),
  })
  previewResult.value = null
  generatedResult.value = null
  toolsOpen.value = true
}
function sourceChanged(): void {
  if (toolsForm.sourceScope === 'Platform') toolsForm.sourceTenantNId = ''
}
function runtimeRequest(): GenerateCodeRequest | null {
  if (!toolsRow.value || !Number.isInteger(toolsForm.ruleRevision) || toolsForm.ruleRevision < 1) {
    validation.value.ruleRevision = copy.value.required
    return null
  }
  if (toolsForm.sourceScope === 'Tenant' && !toolsForm.sourceTenantNId.trim()) {
    validation.value.sourceTenantNId = copy.value.required
    return null
  }
  return {
    sourceScope: toolsForm.sourceScope,
    sourceTenantNId: toolsForm.sourceScope === 'Tenant' ? toolsForm.sourceTenantNId.trim() : null,
    ruleRevision: toolsForm.ruleRevision,
    factoryId: null,
  }
}
function beginToolsRequest(): { controller: AbortController; sequence: number } {
  toolsSequence++
  toolsRequest?.abort()
  toolsRequest = new AbortController()
  toolsBusy.value = true
  clearError()
  return { controller: toolsRequest, sequence: toolsSequence }
}
async function previewDraft(): Promise<void> {
  if (!api || !toolsRow.value || toolsBusy.value) return
  const current = beginToolsRequest()
  try {
    const result = await api.previewCodingRule(
      toolsRow.value.id,
      { factoryId: null },
      { signal: current.controller.signal },
    )
    if (current.sequence === toolsSequence) previewResult.value = result
  } catch (caught) {
    if (current.sequence === toolsSequence) report(caught)
  } finally {
    if (current.sequence === toolsSequence) toolsBusy.value = false
  }
}
async function previewRuntime(): Promise<void> {
  if (!api || !toolsRow.value || toolsBusy.value) return
  clearError()
  const request = runtimeRequest()
  if (!request) return
  const current = beginToolsRequest()
  try {
    const result = await api.previewCode(toolsRow.value.nId, request, {
      signal: current.controller.signal,
    })
    if (current.sequence === toolsSequence) previewResult.value = result
  } catch (caught) {
    if (current.sequence === toolsSequence) report(caught)
  } finally {
    if (current.sequence === toolsSequence) toolsBusy.value = false
  }
}
async function generate(): Promise<void> {
  if (!api || !toolsRow.value || toolsBusy.value) return
  clearError()
  const request = runtimeRequest()
  if (!request) return
  const key = toolsForm.idempotencyKey.trim()
  if (!key || key.length > 200) {
    validation.value.idempotencyKey = copy.value.idempotencyRequired
    return
  }
  const current = beginToolsRequest()
  try {
    const result = await api.generateCode(toolsRow.value.nId, request, key, {
      signal: current.controller.signal,
    })
    if (current.sequence === toolsSequence) generatedResult.value = result
  } catch (caught) {
    if (current.sequence === toolsSequence) report(caught)
  } finally {
    if (current.sequence === toolsSequence) toolsBusy.value = false
  }
}
function renewKey(): void {
  toolsForm.idempotencyKey = createIdempotencyKey()
  generatedResult.value = null
  validation.value.idempotencyKey = ''
}
function closeTools(value = false): void {
  if (value || toolsBusy.value) return
  toolsSequence++
  toolsRequest?.abort()
  toolsOpen.value = false
  toolsRow.value = null
  previewResult.value = null
  generatedResult.value = null
  clearError()
}

function beforeUnload(event: BeforeUnloadEvent): void {
  if (dirty.value) event.preventDefault()
}
onBeforeRouteLeave(allowDiscard)
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  await nextTick()
  await table.value?.reload()
})
onBeforeUnmount(() => {
  listRequest?.abort()
  detailRequest?.abort()
  toolsRequest?.abort()
  detailSequence++
  toolsSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    class="coding-rules-page"
    :title="copy.title"
    :description="copy.description"
    data-testid="coding-rules-page"
  >
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.referenceDataCodingRuleCreate">
        <el-button type="primary" :icon="Plus" data-testid="coding-rule-create" @click="create">
          {{ copy.createRule }}
        </el-button>
      </PermissionGate>
    </template>

    <el-alert v-if="listError" :title="listError" type="error" :closable="false" show-icon />
    <el-alert
      v-if="error && !formOpen && !toolsOpen"
      :title="error"
      type="error"
      :closable="false"
      show-icon
    >
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
    </el-alert>

    <AppQueryPanel show-actions grid @submit="search" @reset="reset">
      <label class="coding-query-field">
        <span>{{ copy.keyword }}</span>
        <el-input v-model="query.keyword" :aria-label="copy.keyword" @keyup.enter="search" />
      </label>
      <label class="coding-query-field">
        <span>{{ copy.scope }}</span>
        <el-select v-model="query.scopeType" :aria-label="copy.scope">
          <el-option value="" :label="copy.all" />
          <el-option v-for="option in scopeOptions" :key="option.value" v-bind="option" />
        </el-select>
      </label>
      <label class="coding-query-field">
        <span>{{ copy.status }}</span>
        <el-select v-model="query.status" :aria-label="copy.status">
          <el-option value="" :label="copy.all" />
          <el-option v-for="option in statusOptions" :key="option.value" v-bind="option" />
        </el-select>
      </label>
    </AppQueryPanel>

    <AppDataTable
      ref="table"
      table-key="reference-data-coding-rules"
      :columns="columns"
      :loader="load"
      :query-mode="mode"
      toolbar-profile="full"
      selection="none"
      :active-row-key="activeId"
      @row-click="setActive"
      @query-mode-change="switchMode"
      @loaded="onLoaded"
      @load-error="reportList"
    >
      <template #cell-scopeType="{ row }">{{
        row.scopeType === 'Tenant' ? copy.tenant : copy.platform
      }}</template>
      <template #cell-status="{ row }">
        <el-tag
          :type="
            row.status === 'Published' ? 'success' : row.status === 'Draft' ? 'info' : 'warning'
          "
        >
          {{ statusLabel(row.status) }}
        </el-tag>
      </template>
      <template #cell-resetPolicy="{ row }">{{ resetLabel(row.resetPolicy) }}</template>
      <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
      <template #actions="{ row, availableWidth }">
        <div class="coding-actions">
          <el-button link type="primary" @click="open(row, false)">{{ copy.detail }}</el-button>
          <el-button
            v-if="canEdit(row) && availableWidth >= 190"
            link
            type="primary"
            @click="open(row, true)"
          >
            {{ copy.edit }}
          </el-button>
          <el-dropdown
            v-if="
              canEdit(row) ||
              canClone(row) ||
              canPublish(row) ||
              canDisable(row) ||
              canUseTools(row)
            "
            trigger="click"
          >
            <el-button link type="primary" :disabled="busy" data-testid="coding-rule-more">
              {{ copy.more }}
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item
                  v-if="canEdit(row) && availableWidth < 190"
                  @click="open(row, true)"
                >
                  {{ copy.edit }}
                </el-dropdown-item>
                <el-dropdown-item v-if="canClone(row)" @click="clone(row)">{{
                  copy.clone
                }}</el-dropdown-item>
                <el-dropdown-item
                  v-if="canPublish(row)"
                  data-testid="coding-rule-publish"
                  @click="publish(row)"
                >
                  {{ copy.publish }}
                </el-dropdown-item>
                <el-dropdown-item v-if="canDisable(row)" @click="disable(row)">{{
                  copy.disable
                }}</el-dropdown-item>
                <el-dropdown-item
                  v-if="canUseTools(row)"
                  data-testid="coding-rule-tools"
                  @click="openTools(row)"
                >
                  {{ copy.codeTools }}
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </template>
    </AppDataTable>
    <p v-if="!firstLoading && total === 0 && !listError">{{ copy.empty }}</p>

    <AppFormDrawer
      :model-value="formOpen"
      :title="
        formMode === 'create'
          ? copy.createRule
          : formMode === 'edit'
            ? copy.editRule
            : copy.ruleDetail
      "
      size="wide"
      :busy="busy"
      @update:model-value="closeForm"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="coding-actions">
          <el-button @click="reloadForm">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div>
      </el-alert>
      <el-alert v-if="isReadOnly" :title="copy.readOnly" type="info" :closable="false" />
      <el-form label-position="top" :disabled="isReadOnly || busy">
        <div class="coding-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input
              v-model="form.nId"
              data-testid="coding-rule-nid"
              :disabled="formMode !== 'create'"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.name" :error="validation.name" required>
            <el-input v-model="form.name" data-testid="coding-rule-name" maxlength="200" />
          </el-form-item>
          <el-form-item :label="copy.scope">
            <el-select
              v-model="form.scopeType"
              data-testid="coding-rule-scope"
              :disabled="formMode !== 'create'"
            >
              <el-option value="Tenant" :label="copy.tenant" />
              <el-option
                v-if="has(PERMISSIONS.referenceDataPlatformManage)"
                value="Platform"
                :label="copy.platform"
              />
              <el-option value="Factory" :label="copy.factory" disabled />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.targetEntity" :error="validation.targetEntityNId" required>
            <el-input
              v-model="form.targetEntityNId"
              data-testid="coding-rule-target"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.resetPolicy">
            <el-select v-model="form.resetPolicy" data-testid="coding-rule-reset-policy">
              <el-option v-for="option in resetOptions" :key="option.value" v-bind="option" />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.factory">
            <el-input
              data-testid="coding-rule-factory"
              disabled
              :placeholder="copy.factoryUnavailable"
            />
          </el-form-item>
        </div>
        <el-form-item :label="copy.template" :error="validation.template" required>
          <el-input
            v-model="form.template"
            type="textarea"
            :rows="4"
            maxlength="1024"
            data-testid="coding-rule-template"
          />
        </el-form-item>
        <aside class="coding-token-help" aria-live="polite">
          <strong>{{ copy.tokenTitle }}</strong>
          <code>{{ copy.tokenList }}</code>
          <span>{{ copy.tokenHint }}</span>
          <span>{{ copy.factoryUnavailable }}</span>
        </aside>
        <el-alert
          v-if="validation.template"
          :title="validation.template"
          type="error"
          :closable="false"
          show-icon
        />
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="closeForm()">{{ copy.cancel }}</el-button>
        <el-button
          v-if="formMode !== 'view'"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="coding-rule-save"
          @click="save"
        >
          {{ copy.save }}
        </el-button>
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="toolsOpen"
      :title="copy.codeTools"
      size="medium"
      :busy="toolsBusy"
      @update:model-value="closeTools"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
      </el-alert>
      <el-form v-if="toolsRow" label-position="top" :disabled="toolsBusy">
        <el-descriptions :column="2" border>
          <el-descriptions-item :label="copy.nId">{{ toolsRow.nId }}</el-descriptions-item>
          <el-descriptions-item :label="copy.status">{{
            statusLabel(toolsRow.status)
          }}</el-descriptions-item>
        </el-descriptions>

        <section v-if="toolsRow.status === 'Draft'" class="coding-tool-section">
          <h3>{{ copy.previewDraft }}</h3>
          <p>{{ copy.noSequence }}</p>
          <el-button
            v-if="has(PERMISSIONS.referenceDataCodingRulePreview)"
            type="primary"
            :loading="toolsBusy"
            data-testid="coding-rule-preview-draft"
            @click="previewDraft"
          >
            {{ copy.previewDraft }}
          </el-button>
        </section>

        <section v-if="runtimeAvailable" class="coding-tool-section">
          <h3>{{ copy.previewRuntime }}</h3>
          <div class="coding-form-grid">
            <el-form-item :label="copy.sourceScope">
              <el-select
                v-model="toolsForm.sourceScope"
                data-testid="coding-rule-source-scope"
                @change="sourceChanged"
              >
                <el-option value="Tenant" :label="copy.tenant" />
                <el-option value="Platform" :label="copy.platform" />
                <el-option value="Factory" :label="copy.factory" disabled />
              </el-select>
            </el-form-item>
            <el-form-item
              :label="copy.sourceTenant"
              :error="validation.sourceTenantNId"
              :required="toolsForm.sourceScope === 'Tenant'"
            >
              <el-input
                v-model="toolsForm.sourceTenantNId"
                data-testid="coding-rule-source-tenant"
                :disabled="toolsForm.sourceScope === 'Platform'"
              />
            </el-form-item>
            <el-form-item :label="copy.ruleRevision" :error="validation.ruleRevision" required>
              <el-input-number
                v-model="toolsForm.ruleRevision"
                :min="1"
                :precision="0"
                data-testid="coding-rule-revision"
              />
            </el-form-item>
            <el-form-item :label="copy.factory">
              <el-input disabled :placeholder="copy.factoryUnavailable" />
            </el-form-item>
          </div>
          <p>{{ copy.noSequence }}</p>
          <el-button
            v-if="has(PERMISSIONS.referenceDataCodingRulePreview)"
            :loading="toolsBusy"
            data-testid="coding-rule-preview-runtime"
            @click="previewRuntime"
          >
            {{ copy.previewRuntime }}
          </el-button>
          <div v-if="has(PERMISSIONS.referenceDataCodingRuleGenerate)" class="coding-generate">
            <el-form-item :label="copy.idempotencyKey" :error="validation.idempotencyKey" required>
              <el-input
                v-model="toolsForm.idempotencyKey"
                data-testid="coding-rule-idempotency-key"
                maxlength="200"
              />
            </el-form-item>
            <el-button :icon="Refresh" :disabled="toolsBusy" @click="renewKey">
              {{ copy.newKey }}
            </el-button>
            <el-button
              type="primary"
              :loading="toolsBusy"
              data-testid="coding-rule-generate"
              @click="generate"
            >
              {{ copy.generate }}
            </el-button>
          </div>
        </section>

        <el-card
          v-if="previewResult"
          shadow="never"
          class="coding-result"
          data-testid="coding-rule-preview-result"
        >
          <strong>{{ copy.previewResult }}</strong>
          <code>{{ previewResult.code }}</code>
          <span>{{ copy.sequence }}: {{ previewResult.sampleSequence }}</span>
          <span>{{ copy.period }}: {{ previewResult.periodKey }}</span>
          <span>{{ copy.noSequence }}</span>
        </el-card>
        <el-card
          v-if="generatedResult"
          shadow="never"
          class="coding-result"
          data-testid="coding-rule-generated-result"
        >
          <strong>{{ copy.generatedResult }}</strong>
          <code>{{ generatedResult.code }}</code>
          <span>{{ copy.sequence }}: {{ generatedResult.sequence }}</span>
          <span>{{ copy.period }}: {{ generatedResult.periodKey }}</span>
        </el-card>
      </el-form>
      <template #footer>
        <el-button :disabled="toolsBusy" @click="closeTools()">{{ copy.close }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.coding-query-field {
  display: flex;
  flex: 1 1 220px;
  flex-direction: column;
  gap: var(--ip-space-1);
  min-width: 180px;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.coding-rules-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.coding-rules-page :deep(.app-page__body) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.coding-rules-page :deep(.app-query-panel) {
  flex: 0 0 auto;
}
.coding-rules-page :deep(.app-data-table) {
  flex: 1 1 0;
  min-height: 0;
}
.coding-rules-page :deep(.app-data-table__card) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}

.coding-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-1);
}

.coding-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 var(--ip-space-4);
}

.coding-token-help {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.coding-token-help code,
.coding-result code {
  overflow-wrap: anywhere;
  color: var(--ip-color-text-primary);
  font-family: var(--ip-font-family-mono);
}

.coding-tool-section {
  margin-top: var(--ip-space-4);
  padding-top: var(--ip-space-3);
  border-top: 1px solid var(--ip-color-border);
}

.coding-tool-section h3 {
  margin: 0 0 var(--ip-space-2);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.coding-generate {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: end;
  gap: var(--ip-space-2);
  margin-top: var(--ip-space-3);
}

.coding-generate :deep(.el-form-item) {
  margin-bottom: 0;
}

.coding-result {
  margin-top: var(--ip-space-4);
}

.coding-result :deep(.el-card__body) {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-2);
}

@media (max-width: 720px) {
  .coding-form-grid,
  .coding-generate {
    grid-template-columns: 1fr;
  }
}
</style>
