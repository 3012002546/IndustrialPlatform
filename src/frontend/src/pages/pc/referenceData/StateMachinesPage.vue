<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown, Delete, Plus } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type { StateMachineApi } from '@/api/referenceData/stateMachines'
import type {
  AvailableStateMachine,
  CreateStateMachineRequest,
  RuntimeStateMachine,
  StateMachineDetail,
  StateMachinePublicationCheck,
  StateMachineSummary,
  StateNodeWrite,
  StateOutcome,
  StateTransitionWrite,
  TransitionEvaluation,
} from '@/api/referenceData/stateMachineTypes'
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

const api = getReferenceDataApi() as unknown as StateMachineApi | null
const localization = useLocalizationStore()
const copy = computed(() => localeMessages[localization.locale].referenceData)
const { has } = usePermission()

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
const graphErrors = ref<string[]>([])
const busy = ref(false)
const publicationOpen = ref(false)
const publication = ref<StateMachinePublicationCheck | null>(null)
const publicationRow = ref<StateMachineSummary | null>(null)
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
const outcomeOptions = computed(() => [
  { value: 'None', label: copy.value.stateMachineOutcomeNone },
  { value: 'Success', label: copy.value.stateMachineOutcomeSuccess },
  { value: 'Failure', label: copy.value.stateMachineOutcomeFailure },
  { value: 'Skipped', label: copy.value.stateMachineOutcomeSkipped },
])
const columns = computed<AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.name, minWidth: 120, sortable: true },
])
const nodeColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 125, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'isInitial', title: copy.value.stateMachineInitial, width: 82, filter: false },
  { field: 'isTerminal', title: copy.value.stateMachineTerminal, width: 82, filter: false },
  { field: 'outcome', title: copy.value.stateMachineOutcome, minWidth: 120, filter: false },
  { field: 'color', title: copy.value.stateMachineColor, width: 115, filter: false },
  { field: 'sort', title: copy.value.sort, width: 90, filter: false },
])
const transitionColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'fromStatusNId', title: copy.value.stateMachineFrom, minWidth: 140, filter: false },
  { field: 'actionNId', title: copy.value.stateMachineActionNId, minWidth: 130, filter: false },
  {
    field: 'actionName',
    title: copy.value.stateMachineActionName,
    minWidth: 150,
    filter: false,
  },
  { field: 'toStatusNId', title: copy.value.stateMachineTo, minWidth: 140, filter: false },
  { field: 'description', title: copy.value.description, minWidth: 180, filter: false },
])
const runtimeNodeColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 120, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 145, filter: false },
  { field: 'isInitial', title: copy.value.stateMachineInitial, width: 80, filter: false },
  { field: 'isTerminal', title: copy.value.stateMachineTerminal, width: 80, filter: false },
  { field: 'outcome', title: copy.value.stateMachineOutcome, width: 110, filter: false },
])

function statusLabel(status: PublicationStatus): string {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function outcomeLabel(outcome: StateOutcome): string {
  return outcomeOptions.value.find((item) => item.value === outcome)?.label ?? outcome
}
function sourceLabel(scope: ReferenceScope, tenantNId: string | null): string {
  return scope === 'Tenant'
    ? `${copy.value.tenant}${tenantNId ? ` · ${tenantNId}` : ''}`
    : copy.value.platform
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
  graphErrors.value = []
}
function report(caught: unknown): void {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  const details = caught instanceof ApiError ? caught.details : undefined
  conflict.value = details?.code === 'REF-CONCURRENCY-CONFLICT'
  const codes: Record<string, string> = {
    'REF-STATE-MACHINE-INVALID': copy.value.stateMachineInvalid,
    'REF-STATE-MACHINE-NOT-FOUND': copy.value.notFound,
    'REF-INVALID-STATE': copy.value.stateInvalid,
    'REF-VALIDATION-FAILED': copy.value.invalid,
  }
  error.value = conflict.value
    ? copy.value.conflict
    : details?.status === 403
      ? copy.value.forbidden
      : details?.status === 404
        ? copy.value.notFound
        : (codes[details?.code ?? ''] ??
          (details?.status === 400 || details?.status === 422
            ? copy.value.invalid
            : copy.value.unavailable))
  traceId.value = details?.traceId ?? ''
  const field = details?.parameters?.field
  if (typeof field === 'string') validation.value[field] = error.value
}
function version(row: Pick<StateMachineSummary, 'optimisticVersion' | 'concurrencyVersion'>) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
}
type StateMachineLifecycle = Pick<
  StateMachineSummary,
  | 'scopeType'
  | 'status'
  | 'isFrozen'
  | 'isLocked'
  | 'publishedOn'
  | 'optimisticVersion'
  | 'concurrencyVersion'
>
function scopeWritable(row: Pick<StateMachineLifecycle, 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: StateMachineLifecycle): boolean {
  return (
    row.status === 'Draft' && scopeWritable(row) && has(PERMISSIONS.referenceDataStateMachineUpdate)
  )
}
function canClone(row: StateMachineLifecycle | null): boolean {
  return (
    !!row &&
    row.publishedOn !== null &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataStateMachineCreate)
  )
}
function canPublish(row: StateMachineLifecycle | null): boolean {
  return (
    !!row &&
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataStateMachinePublish)
  )
}
function canDisable(row: StateMachineLifecycle | null): boolean {
  return (
    !!row &&
    (row.status === 'Draft' || row.status === 'Published') &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataStateMachineDisable)
  )
}
function canCheck(row: StateMachineLifecycle | null): boolean {
  return (
    !!row &&
    row.publishedOn !== null &&
    ['Published', 'Superseded', 'Disabled'].includes(row.status)
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
    const result = await api.listStateMachines(params, { signal: listRequest.signal })
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
  if (!formOpen.value && !publicationOpen.value && !runtimeOpen.value) clearError()
}
function reportList(caught: unknown): void {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  listError.value = copy.value.unavailable
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

interface FormNode extends StateNodeWrite {
  localKey: string
  id?: string
}
interface FormTransition extends StateTransitionWrite {
  localKey: string
  id?: string
}
type FormMode = 'create' | 'edit' | 'view'
const formOpen = ref(false)
const formMode = ref<FormMode>('view')
const selected = ref<StateMachineDetail | null>(null)
const activeId = ref<string | null>(null)
const selectedSummary = computed<StateMachineSummary | null>(() =>
  selected.value
    ? {
        ...selected.value,
        nodeCount: selected.value.nodes.length,
        transitionCount: selected.value.transitions.length,
      }
    : null,
)
const savedSnapshot = ref('')
const form = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
  nodes: [] as FormNode[],
  transitions: [] as FormTransition[],
})
const stateDetailTab = ref('nodes')
const dirty = computed(
  () => formOpen.value && formMode.value !== 'view' && JSON.stringify(form) !== savedSnapshot.value,
)
const canSubmit = computed(() => {
  if (formMode.value === 'create')
    return (
      has(PERMISSIONS.referenceDataStateMachineCreate) &&
      (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
    )
  return formMode.value === 'edit' && !!selected.value && canEdit(selected.value)
})
const isReadOnly = computed(() => !canSubmit.value)

function blankNode(initial = false): FormNode {
  return {
    localKey: crypto.randomUUID(),
    nId: '',
    name: '',
    description: null,
    isInitial: initial,
    isTerminal: false,
    outcome: 'None',
    color: null,
    sort: form.nodes.length,
  }
}
function blankTransition(): FormTransition {
  return {
    localKey: crypto.randomUUID(),
    fromStatusNId: '',
    actionNId: '',
    actionName: '',
    toStatusNId: '',
    description: null,
  }
}
function nodeWrite(node: FormNode): StateNodeWrite {
  return {
    nId: node.nId.trim(),
    name: node.name.trim(),
    description: node.description?.trim() || null,
    isInitial: node.isInitial,
    isTerminal: node.isTerminal,
    outcome: node.outcome,
    color: node.color?.trim().toUpperCase() || null,
    sort: node.sort,
  }
}
function transitionWrite(transition: FormTransition): StateTransitionWrite {
  return {
    fromStatusNId: transition.fromStatusNId.trim(),
    actionNId: transition.actionNId.trim(),
    actionName: transition.actionName.trim(),
    toStatusNId: transition.toStatusNId.trim(),
    description: transition.description?.trim() || null,
  }
}
function payload(): CreateStateMachineRequest {
  return {
    scopeType: form.scopeType,
    nId: form.nId.trim(),
    name: form.name.trim(),
    description: form.description.trim() || null,
    nodes: form.nodes.map(nodeWrite),
    transitions: form.transitions.map(transitionWrite),
  }
}
function fill(item: StateMachineDetail | null): void {
  selected.value = item
  Object.assign(form, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    description: item?.description ?? '',
    scopeType: item?.scopeType ?? 'Tenant',
    nodes: (item?.nodes ?? []).map((node) => ({
      ...node,
      localKey: crypto.randomUUID(),
    })),
    transitions: (item?.transitions ?? []).map((transition) => ({
      ...transition,
      localKey: crypto.randomUUID(),
    })),
  })
  savedSnapshot.value = JSON.stringify(form)
}
function addNode(): void {
  if (canSubmit.value && form.nodes.length < 200)
    form.nodes.push(blankNode(form.nodes.length === 0))
}
function removeNode(node: FormNode): void {
  if (!canSubmit.value) return
  form.nodes = form.nodes.filter((item) => item.localKey !== node.localKey)
  const removed = node.nId.trim().toUpperCase()
  form.transitions = form.transitions.filter(
    (item) =>
      item.fromStatusNId.trim().toUpperCase() !== removed &&
      item.toStatusNId.trim().toUpperCase() !== removed,
  )
  if (form.nodes.length > 0 && !form.nodes.some((item) => item.isInitial))
    form.nodes[0]!.isInitial = true
}
function addTransition(): void {
  if (canSubmit.value && form.transitions.length < 1000) form.transitions.push(blankTransition())
}
function removeTransition(transition: FormTransition): void {
  if (canSubmit.value)
    form.transitions = form.transitions.filter((item) => item.localKey !== transition.localKey)
}
function setInitial(node: FormNode, checked: boolean): void {
  if (!canSubmit.value) return
  if (checked) for (const item of form.nodes) item.isInitial = item.localKey === node.localKey
  else node.isInitial = false
}

async function create(): Promise<void> {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  clearError()
  fill(null)
  form.nodes.push(blankNode(true))
  savedSnapshot.value = JSON.stringify(form)
  formMode.value = 'create'
  formOpen.value = true
}
async function open(
  row: StateMachineSummary,
  edit = false,
  showForm = true,
  markActive = false,
): Promise<void> {
  if (!(await allowDiscard())) return
  if (markActive) activeId.value = row.id
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  clearError()
  busy.value = true
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const item = await api.getStateMachine(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = edit && canEdit(item) ? 'edit' : 'view'
    formOpen.value = showForm
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function select(row: StateMachineSummary): Promise<void> {
  await open(row, false, false, true)
}
async function allowDiscard(): Promise<boolean> {
  if (busy.value && formOpen.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.stateMachineTitle, {
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
  busy.value = false
}
async function closeForm(value = false): Promise<void> {
  if (!value && (await allowDiscard())) {
    closeFormNow()
    clearError()
  }
}
function validNId(value: string, root = false): boolean {
  const pattern = root ? /^[A-Za-z][A-Za-z0-9_.-]{1,63}$/ : /^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/
  return pattern.test(value.trim())
}
function addGraphError(message: string): void {
  if (!graphErrors.value.includes(message)) graphErrors.value.push(message)
}
function validate(): boolean {
  validation.value = {}
  graphErrors.value = []
  if (!validNId(form.nId, true)) validation.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) validation.value.name = copy.value.required
  if (form.nodes.length < 1 || form.nodes.length > 200)
    addGraphError(copy.value.stateMachineNodeLimit)
  if (form.transitions.length > 1000) addGraphError(copy.value.stateMachineTransitionLimit)

  const nodeNIds = new Set<string>()
  for (const node of form.nodes) {
    const nId = node.nId.trim().toUpperCase()
    if (!validNId(node.nId) || !node.name.trim() || !Number.isInteger(node.sort) || node.sort < 0)
      addGraphError(copy.value.stateMachineInvalid)
    if (nodeNIds.has(nId)) addGraphError(copy.value.stateMachineDuplicateNode)
    nodeNIds.add(nId)
    if (node.color && !/^#[0-9A-Fa-f]{6}$/.test(node.color.trim()))
      addGraphError(copy.value.stateMachineColorInvalid)
  }

  const initialNodes = form.nodes.filter((node) => node.isInitial)
  if (initialNodes.length !== 1) addGraphError(copy.value.stateMachineInitialInvalid)
  const terminalNIds = new Set(
    form.nodes.filter((node) => node.isTerminal).map((node) => node.nId.trim().toUpperCase()),
  )
  const pairs = new Set<string>()
  const actionNames = new Map<string, string>()
  for (const transition of form.transitions) {
    const from = transition.fromStatusNId.trim().toUpperCase()
    const to = transition.toStatusNId.trim().toUpperCase()
    const action = transition.actionNId.trim().toUpperCase()
    if (!validNId(transition.actionNId) || !transition.actionName.trim())
      addGraphError(copy.value.stateMachineInvalid)
    if (!nodeNIds.has(from) || !nodeNIds.has(to))
      addGraphError(copy.value.stateMachineEndpointInvalid)
    const pair = `${from}\u0000${action}`
    if (pairs.has(pair)) addGraphError(copy.value.stateMachineDuplicateTransition)
    pairs.add(pair)
    if (terminalNIds.has(from)) addGraphError(copy.value.stateMachineTerminalOutgoing)
    const previousName = actionNames.get(action)
    if (previousName !== undefined && previousName !== transition.actionName.trim())
      addGraphError(copy.value.stateMachineActionNameMismatch)
    else actionNames.set(action, transition.actionName.trim())
  }

  if (initialNodes.length === 1 && form.nodes.length > 0) {
    const reached = new Set<string>([initialNodes[0]!.nId.trim().toUpperCase()])
    const queue = [...reached]
    while (queue.length > 0) {
      const current = queue.shift()!
      for (const transition of form.transitions) {
        if (transition.fromStatusNId.trim().toUpperCase() !== current) continue
        const target = transition.toStatusNId.trim().toUpperCase()
        if (nodeNIds.has(target) && !reached.has(target)) {
          reached.add(target)
          queue.push(target)
        }
      }
    }
    if (reached.size !== nodeNIds.size) addGraphError(copy.value.stateMachineUnreachable)
  }
  return Object.keys(validation.value).length === 0 && graphErrors.value.length === 0
}
async function save(): Promise<void> {
  if (!api || !canSubmit.value || !validate() || busy.value) return
  busy.value = true
  clearError()
  try {
    const request = payload()
    const result =
      formMode.value === 'create'
        ? await api.createStateMachine(request)
        : await api.updateStateMachine(selected.value!.id, {
            name: request.name,
            description: request.description,
            nodes: request.nodes,
            transitions: request.transitions,
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
    const item = await api.getStateMachine(selected.value.id, { signal: detailRequest.signal })
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
async function clone(row: StateMachineSummary | null): Promise<void> {
  if (!api || !row || !canClone(row) || busy.value || !(await allowDiscard())) return
  busy.value = true
  clearError()
  try {
    const item = await api.cloneStateMachine(row.id, version(row))
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
async function preparePublication(row: StateMachineSummary | null): Promise<void> {
  if (!api || !row || !canPublish(row) || busy.value) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  busy.value = true
  clearError()
  try {
    const result = await api.checkStateMachinePublication(row.id, {
      signal: detailRequest.signal,
    })
    if (sequence !== detailSequence) return
    publication.value = result
    publicationRow.value = row
    publicationOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
function closePublication(value = false): void {
  if (value || busy.value) return
  detailSequence++
  detailRequest?.abort()
  publicationOpen.value = false
  publication.value = null
  publicationRow.value = null
  clearError()
}
async function publish(): Promise<void> {
  if (!api || !publicationRow.value || !publication.value || publication.value.errors.length > 0)
    return
  busy.value = true
  clearError()
  try {
    const result = await api.publishStateMachine(
      publicationRow.value.id,
      version(publicationRow.value),
    )
    if (result) fill(result)
    publicationOpen.value = false
    publication.value = null
    publicationRow.value = null
    ElMessage.success(copy.value.publishedSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disable(row: StateMachineSummary | null): Promise<void> {
  if (!api || !row || !canDisable(row) || busy.value) return
  let reason = ''
  try {
    const response = await ElMessageBox.prompt(
      copy.value.stateMachineDisableHint,
      copy.value.disable,
      {
        confirmButtonText: copy.value.disable,
        cancelButtonText: copy.value.cancel,
        inputPlaceholder: copy.value.reason,
        inputValidator: (value) =>
          (Boolean(value?.trim()) && value!.length <= 1000) || copy.value.required,
      },
    )
    reason = response.value.trim()
  } catch {
    return
  }
  if (!reason) return
  busy.value = true
  clearError()
  try {
    const result = await api.disableStateMachine(row.id, {
      ...version(row),
      changeReason: reason,
    })
    if (result) fill(result)
    ElMessage.success(copy.value.disabledSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

type RuntimeReadMode = 'Fixed' | 'Current'
const runtimeOpen = ref(false)
const runtimeLoading = ref(false)
const available = ref<AvailableStateMachine[]>([])
const availableKey = ref('')
const readMode = ref<RuntimeReadMode>('Fixed')
const runtimeDefinition = ref<RuntimeStateMachine | null>(null)
const evaluation = reactive({ fromStatusNId: '', actionNId: '' })
const evaluationResult = ref<TransitionEvaluation | null>(null)
let runtimeRequest: AbortController | undefined
let runtimeSequence = 0
let evaluationRequest: AbortController | undefined
let evaluationSequence = 0

function availabilityKey(item: AvailableStateMachine): string {
  return [item.nId, item.sourceScope, item.sourceTenantNId ?? '', String(item.revision)].join('|')
}
const selectedAvailability = computed(() =>
  available.value.find((item) => availabilityKey(item) === availableKey.value),
)
function availabilityLabel(item: AvailableStateMachine): string {
  return `${item.name} (${item.nId}) · ${sourceLabel(item.sourceScope, item.sourceTenantNId)} · ${copy.value.revision} ${item.revision}`
}
async function openRuntime(row: StateMachineSummary | null): Promise<void> {
  if (!api || !row || !canCheck(row) || !(await allowDiscard())) return
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeRequest = new AbortController()
  const sequence = runtimeSequence
  clearError()
  runtimeOpen.value = true
  runtimeLoading.value = true
  runtimeDefinition.value = null
  evaluationResult.value = null
  readMode.value = 'Fixed'
  try {
    const page = await api.listAvailableStateMachines(
      { pageIndex: 1, pageSize: 100, keyword: row.nId },
      { signal: runtimeRequest.signal },
    )
    if (sequence !== runtimeSequence) return
    const rowChoice: AvailableStateMachine = {
      nId: row.nId,
      name: row.name,
      sourceScope: row.scopeType,
      sourceTenantNId: row.tenantNId,
      revision: row.revision,
      publishedOn: row.publishedOn!,
      nodeCount: row.nodeCount,
      transitionCount: row.transitionCount,
    }
    available.value = [...page.items]
    const exact = available.value.find(
      (item) => availabilityKey(item) === availabilityKey(rowChoice),
    )
    if (!exact) available.value.unshift(rowChoice)
    availableKey.value = availabilityKey(exact ?? rowChoice)
  } catch (caught) {
    if (sequence === runtimeSequence) report(caught)
    return
  } finally {
    if (sequence === runtimeSequence) runtimeLoading.value = false
  }
  if (sequence === runtimeSequence) await loadRuntimeDefinition()
}
async function changeAvailability(): Promise<void> {
  readMode.value = 'Fixed'
  await loadRuntimeDefinition()
}
async function loadRuntimeDefinition(): Promise<void> {
  const choice = selectedAvailability.value
  if (!api || !choice) return
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeRequest = new AbortController()
  const sequence = runtimeSequence
  evaluationSequence++
  evaluationRequest?.abort()
  runtimeLoading.value = true
  runtimeDefinition.value = null
  evaluationResult.value = null
  clearError()
  try {
    const item =
      readMode.value === 'Fixed'
        ? await api.getStateMachineRevision(
            choice.nId,
            choice.revision,
            choice.sourceScope,
            choice.sourceTenantNId,
            { signal: runtimeRequest.signal },
          )
        : await api.getCurrentStateMachine(choice.nId, choice.sourceScope, choice.sourceTenantNId, {
            signal: runtimeRequest.signal,
          })
    if (sequence !== runtimeSequence) return
    runtimeDefinition.value = item
    evaluation.fromStatusNId =
      item.nodes.find((node) => node.isInitial)?.nId ?? item.nodes[0]?.nId ?? ''
    evaluation.actionNId = ''
  } catch (caught) {
    if (sequence === runtimeSequence) report(caught)
  } finally {
    if (sequence === runtimeSequence) runtimeLoading.value = false
  }
}
async function evaluate(): Promise<void> {
  const definition = runtimeDefinition.value
  if (!api || !definition || busy.value) return
  if (!validNId(evaluation.fromStatusNId) || !validNId(evaluation.actionNId)) {
    validation.value.runtime = copy.value.invalidNId
    return
  }
  evaluationSequence++
  evaluationRequest?.abort()
  evaluationRequest = new AbortController()
  const sequence = evaluationSequence
  busy.value = true
  evaluationResult.value = null
  clearError()
  try {
    const result = await api.evaluateStateTransition(
      definition.nId,
      {
        sourceScope: definition.sourceScope,
        sourceTenantNId: definition.sourceTenantNId,
        revision: definition.revision,
        fromStatusNId: evaluation.fromStatusNId.trim(),
        actionNId: evaluation.actionNId.trim(),
      },
      { signal: evaluationRequest.signal },
    )
    if (sequence === evaluationSequence) evaluationResult.value = result
  } catch (caught) {
    if (sequence === evaluationSequence) report(caught)
  } finally {
    if (sequence === evaluationSequence) busy.value = false
  }
}
function closeRuntime(value = false): void {
  if (value) return
  runtimeSequence++
  evaluationSequence++
  runtimeRequest?.abort()
  evaluationRequest?.abort()
  runtimeOpen.value = false
  runtimeLoading.value = false
  runtimeDefinition.value = null
  evaluationResult.value = null
  clearError()
}

function beforeUnload(event: BeforeUnloadEvent): void {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}
onBeforeRouteLeave(() => allowDiscard())
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  await nextTick()
  await table.value?.reload()
})
onBeforeUnmount(() => {
  listRequest?.abort()
  detailRequest?.abort()
  runtimeRequest?.abort()
  evaluationRequest?.abort()
  detailSequence++
  runtimeSequence++
  evaluationSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    class="state-machine-page"
    :title="copy.stateMachineTitle"
    :description="copy.stateMachineDescription"
    data-testid="reference-data-state-machines"
  >
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.referenceDataStateMachineCreate">
        <el-button type="primary" :icon="Plus" data-testid="state-machine-create" @click="create">
          {{ copy.stateMachineCreate }}
        </el-button>
      </PermissionGate>
    </template>

    <el-alert v-if="listError" :title="listError" type="error" :closable="false" show-icon />
    <el-alert
      v-if="error && !formOpen && !publicationOpen && !runtimeOpen"
      :title="error"
      type="error"
      :closable="false"
      show-icon
    >
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
    </el-alert>
    <div class="state-master-detail">
      <section class="state-master" :aria-label="copy.stateMachineTitle">
        <AppQueryPanel show-actions grid @submit="search" @reset="reset">
          <label class="state-machine-query-field">
            <span>{{ copy.keyword }}</span>
            <el-input v-model="query.keyword" :aria-label="copy.keyword" @keyup.enter="search" />
          </label>
          <label class="state-machine-query-field">
            <span>{{ copy.scope }}</span>
            <el-select v-model="query.scopeType" :aria-label="copy.scope">
              <el-option value="" :label="copy.all" />
              <el-option v-for="option in scopeOptions" :key="option.value" v-bind="option" />
            </el-select>
          </label>
          <label class="state-machine-query-field">
            <span>{{ copy.status }}</span>
            <el-select v-model="query.status" :aria-label="copy.status">
              <el-option value="" :label="copy.all" />
              <el-option v-for="option in statusOptions" :key="option.value" v-bind="option" />
            </el-select>
          </label>
        </AppQueryPanel>
        <AppDataTable
          ref="table"
          table-key="reference-data-state-machines"
          :columns="columns"
          :loader="load"
          :query-mode="mode"
          toolbar-profile="compact"
          :quick-search-enabled="false"
          selection="none"
          :active-row-key="activeId"
          @row-click="select"
          @query-mode-change="switchMode"
          @loaded="onLoaded"
          @load-error="reportList"
        >
          <template #cell-name="{ row }"
            ><div class="state-directory-name">
              <strong>{{ row.name }}</strong
              ><small :title="`${row.nId} · ${statusLabel(row.status)}`"
                ><span class="directory-status">{{ statusLabel(row.status) }}</span> ·
                {{ row.nId }}</small
              >
            </div></template
          >
          <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
        </AppDataTable>
        <p v-if="!firstLoading && total === 0 && !listError">{{ copy.stateMachineEmpty }}</p>
      </section>
      <section class="state-detail-panel" :aria-label="copy.stateMachineTitle">
        <el-empty v-if="!selected" :description="copy.stateMachineTitle" />
        <template v-else>
          <header class="state-detail-context">
            <div>
              <h2>{{ selected.name }}</h2>
              <p>
                {{ selected.nId }} ·
                {{ selected.scopeType === 'Tenant' ? copy.tenant : copy.platform }} ·
                {{ copy.revision }} {{ selected.revision }} · {{ statusLabel(selected.status) }} ·
                {{ copy.stateMachineNodeCount }} {{ selected.nodes.length }} ·
                {{ copy.stateMachineTransitionCount }}
                {{ selected.transitions.length }}
              </p>
            </div>
            <div class="state-machine-actions">
              <el-button
                v-if="canEdit(selected)"
                @click="((formOpen = true), (formMode = 'edit'))"
                >{{ copy.edit }}</el-button
              >
              <el-dropdown
                v-if="
                  canClone(selectedSummary) ||
                  canPublish(selectedSummary) ||
                  canDisable(selectedSummary) ||
                  canCheck(selectedSummary)
                "
                trigger="click"
              >
                <el-button :disabled="busy" data-testid="state-machine-more">
                  {{ copy.more }}<el-icon><ArrowDown /></el-icon>
                </el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item
                      v-if="canClone(selectedSummary)"
                      @click="clone(selectedSummary)"
                      >{{ copy.clone }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="canPublish(selectedSummary)"
                      data-testid="state-machine-publish"
                      @click="preparePublication(selectedSummary)"
                    >
                      {{ copy.stateMachinePublicationCheck }}
                    </el-dropdown-item>
                    <el-dropdown-item
                      v-if="canDisable(selectedSummary)"
                      @click="disable(selectedSummary)"
                      >{{ copy.disable }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="canCheck(selectedSummary)"
                      data-testid="state-machine-check-open"
                      @click="openRuntime(selectedSummary)"
                    >
                      {{ copy.stateMachineCheck }}
                    </el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </div>
          </header>
          <el-tabs v-model="stateDetailTab">
            <el-tab-pane :label="copy.stateMachineNodes" name="nodes">
              <AppDataTable
                table-key="reference-data-state-machine-inline-nodes"
                :rows="form.nodes"
                :total="form.nodes.length"
                :columns="nodeColumns"
                row-key="localKey"
                toolbar-profile="compact"
                selection="none"
              >
                <template #cell-isInitial="{ row }">{{
                  row.isInitial ? copy.trueValue : copy.falseValue
                }}</template>
                <template #cell-isTerminal="{ row }">{{
                  row.isTerminal ? copy.trueValue : copy.falseValue
                }}</template>
                <template #cell-outcome="{ row }">{{ outcomeLabel(row.outcome) }}</template>
              </AppDataTable>
            </el-tab-pane>
            <el-tab-pane :label="copy.stateMachineTransitions" name="transitions">
              <AppDataTable
                table-key="reference-data-state-machine-inline-transitions"
                :rows="form.transitions"
                :total="form.transitions.length"
                :columns="transitionColumns"
                row-key="localKey"
                toolbar-profile="compact"
                selection="none"
              />
            </el-tab-pane>
            <el-tab-pane :label="copy.stateMachinePublicationCheck" name="check">
              <el-alert :title="copy.stateMachinePublishHint" type="info" :closable="false" />
              <p>{{ copy.stateMachineGraphHint }}</p>
              <el-button
                v-if="canPublish(selectedSummary)"
                type="primary"
                :loading="busy"
                data-testid="state-machine-detail-publication-open"
                @click="preparePublication(selectedSummary)"
                >{{ copy.stateMachinePublicationCheck }}</el-button
              >
            </el-tab-pane>
            <el-tab-pane :label="copy.stateMachineCheck" name="action">
              <el-alert :title="copy.stateMachineCheckHint" type="info" :closable="false" />
              <el-button
                v-if="canCheck(selectedSummary)"
                type="primary"
                :loading="runtimeLoading"
                data-testid="state-machine-detail-check-open"
                @click="openRuntime(selectedSummary)"
                >{{ copy.stateMachineCheck }}</el-button
              >
            </el-tab-pane>
          </el-tabs>
        </template>
      </section>
    </div>

    <AppFormDrawer
      :model-value="formOpen"
      :title="
        formMode === 'create'
          ? copy.stateMachineCreate
          : formMode === 'edit'
            ? copy.stateMachineEdit
            : copy.stateMachineDetail
      "
      size="wide"
      :busy="busy"
      @update:model-value="closeForm"
    >
      <div data-testid="state-machine-detail-marker" class="state-machine-reference">
        <strong>{{ form.name || copy.stateMachineTitle }}</strong>
        <span v-if="selected">{{ sourceLabel(selected.scopeType, selected.tenantNId) }}</span>
        <span v-if="selected">{{ copy.revision }} {{ selected.revision }}</span>
      </div>
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="state-machine-actions">
          <el-button @click="reloadForm">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div>
      </el-alert>
      <el-alert v-if="isReadOnly" :title="copy.readOnly" type="info" :closable="false" />
      <el-alert
        v-if="graphErrors.length"
        :title="copy.stateMachineInvalid"
        type="error"
        :closable="false"
        show-icon
      >
        <ul class="state-machine-error-list">
          <li v-for="message in graphErrors" :key="message">{{ message }}</li>
        </ul>
      </el-alert>
      <el-form label-position="top" :disabled="isReadOnly || busy">
        <div class="state-machine-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input
              v-model="form.nId"
              data-testid="state-machine-nid"
              :disabled="formMode !== 'create'"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.name" :error="validation.name" required>
            <el-input v-model="form.name" data-testid="state-machine-name" maxlength="200" />
          </el-form-item>
          <el-form-item :label="copy.scope">
            <el-select v-model="form.scopeType" :disabled="formMode !== 'create'">
              <el-option value="Tenant" :label="copy.tenant" />
              <el-option
                v-if="has(PERMISSIONS.referenceDataPlatformManage)"
                value="Platform"
                :label="copy.platform"
              />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item :label="copy.description">
          <el-input v-model="form.description" type="textarea" :rows="2" maxlength="2000" />
        </el-form-item>

        <section class="state-machine-section">
          <div class="state-machine-section-heading">
            <div>
              <h3>{{ copy.stateMachineNodes }} ({{ form.nodes.length }}/200)</h3>
              <p>{{ copy.stateMachineGraphHint }}</p>
            </div>
            <el-button
              v-if="canSubmit"
              :icon="Plus"
              :disabled="form.nodes.length >= 200"
              data-testid="state-node-add"
              @click="addNode"
            >
              {{ copy.stateMachineAddNode }}
            </el-button>
          </div>
          <AppDataTable
            table-key="reference-data-state-machine-nodes"
            :rows="form.nodes"
            :total="form.nodes.length"
            :columns="nodeColumns"
            toolbar-profile="compact"
            selection="none"
          >
            <template #cell-nId="{ row }">
              <el-input
                v-model="row.nId"
                :aria-label="copy.nId"
                :data-testid="`state-node-nid-${form.nodes.indexOf(row)}`"
              />
            </template>
            <template #cell-name="{ row }">
              <el-input
                v-model="row.name"
                :aria-label="copy.name"
                :data-testid="`state-node-name-${form.nodes.indexOf(row)}`"
              />
            </template>
            <template #cell-isInitial="{ row }">
              <el-checkbox
                :model-value="row.isInitial"
                :aria-label="copy.stateMachineInitial"
                :data-testid="`state-node-initial-${form.nodes.indexOf(row)}`"
                @change="setInitial(row, Boolean($event))"
              />
            </template>
            <template #cell-isTerminal="{ row }">
              <el-checkbox
                v-model="row.isTerminal"
                :aria-label="copy.stateMachineTerminal"
                :data-testid="`state-node-terminal-${form.nodes.indexOf(row)}`"
              />
            </template>
            <template #cell-outcome="{ row }">
              <el-select
                v-model="row.outcome"
                :aria-label="copy.stateMachineOutcome"
                :data-testid="`state-node-outcome-${form.nodes.indexOf(row)}`"
              >
                <el-option v-for="option in outcomeOptions" :key="option.value" v-bind="option" />
              </el-select>
            </template>
            <template #cell-color="{ row }">
              <el-input
                v-model="row.color"
                :aria-label="copy.stateMachineColor"
                :data-testid="`state-node-color-${form.nodes.indexOf(row)}`"
                placeholder="#RRGGBB"
                maxlength="7"
              />
            </template>
            <template #cell-sort="{ row }">
              <el-input-number v-model="row.sort" :aria-label="copy.sort" :min="0" :precision="0" />
            </template>
            <template #actions="{ row }">
              <el-button
                v-if="canSubmit"
                link
                type="danger"
                :icon="Delete"
                @click="removeNode(row)"
              >
                {{ copy.remove }}
              </el-button>
            </template>
          </AppDataTable>
        </section>

        <section class="state-machine-section">
          <div class="state-machine-section-heading">
            <div>
              <h3>{{ copy.stateMachineTransitions }} ({{ form.transitions.length }}/1000)</h3>
            </div>
            <el-button
              v-if="canSubmit"
              :icon="Plus"
              :disabled="form.transitions.length >= 1000 || form.nodes.length === 0"
              data-testid="state-transition-add"
              @click="addTransition"
            >
              {{ copy.stateMachineAddTransition }}
            </el-button>
          </div>
          <AppDataTable
            table-key="reference-data-state-machine-transitions"
            :rows="form.transitions"
            :total="form.transitions.length"
            :columns="transitionColumns"
            toolbar-profile="compact"
            selection="none"
          >
            <template #cell-fromStatusNId="{ row }">
              <el-select
                v-model="row.fromStatusNId"
                :aria-label="copy.stateMachineFrom"
                :data-testid="`state-transition-from-${form.transitions.indexOf(row)}`"
              >
                <el-option
                  v-for="node in form.nodes.filter((item) => item.nId.trim())"
                  :key="node.localKey"
                  :value="node.nId.trim()"
                  :label="node.name || node.nId"
                />
              </el-select>
            </template>
            <template #cell-actionNId="{ row }">
              <el-input
                v-model="row.actionNId"
                :aria-label="copy.stateMachineActionNId"
                :data-testid="`state-transition-action-${form.transitions.indexOf(row)}`"
              />
            </template>
            <template #cell-actionName="{ row }">
              <el-input
                v-model="row.actionName"
                :aria-label="copy.stateMachineActionName"
                :data-testid="`state-transition-action-name-${form.transitions.indexOf(row)}`"
              />
            </template>
            <template #cell-toStatusNId="{ row }">
              <el-select
                v-model="row.toStatusNId"
                :aria-label="copy.stateMachineTo"
                :data-testid="`state-transition-to-${form.transitions.indexOf(row)}`"
              >
                <el-option
                  v-for="node in form.nodes.filter((item) => item.nId.trim())"
                  :key="node.localKey"
                  :value="node.nId.trim()"
                  :label="node.name || node.nId"
                />
              </el-select>
            </template>
            <template #cell-description="{ row }">
              <el-input v-model="row.description" :aria-label="copy.description" />
            </template>
            <template #actions="{ row }">
              <el-button
                v-if="canSubmit"
                link
                type="danger"
                :icon="Delete"
                @click="removeTransition(row)"
              >
                {{ copy.remove }}
              </el-button>
            </template>
          </AppDataTable>
        </section>
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="closeForm()">{{ copy.cancel }}</el-button>
        <el-button
          v-if="formMode !== 'view'"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="state-machine-save"
          @click="save"
        >
          {{ copy.save }}
        </el-button>
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="publicationOpen"
      :title="copy.publicationTitle"
      size="medium"
      :busy="busy"
      @update:model-value="closePublication"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <div
        v-if="publication"
        data-testid="state-machine-publish-confirm"
        class="state-machine-publication"
      >
        <p>
          <strong>{{ copy.previousRevision }}:</strong>
          {{ publication.previousRevision ?? copy.firstPublication }}
        </p>
        <section>
          <strong>{{ copy.stateMachineAddedNodes }}</strong>
          <p>{{ publication.addedNodeNIds.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachineRemovedNodes }}</strong>
          <p>{{ publication.removedNodeNIds.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachineChangedNodes }}</strong>
          <p>{{ publication.changedNodeNIds.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachineAddedTransitions }}</strong>
          <p>{{ publication.addedTransitionKeys.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachineRemovedTransitions }}</strong>
          <p>{{ publication.removedTransitionKeys.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachineChangedTransitions }}</strong>
          <p>{{ publication.changedTransitionKeys.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.stateMachinePublicationErrors }}</strong>
          <p>
            {{
              publication.errors.map((item) => `${item.code} (${item.field})`).join(', ') ||
              copy.validationPassed
            }}
          </p>
        </section>
        <el-alert :title="copy.stateMachinePublishHint" type="warning" :closable="false" />
      </div>
      <template #footer>
        <el-button :disabled="busy" @click="closePublication()">{{ copy.cancel }}</el-button>
        <el-button
          type="primary"
          :disabled="!publication || publication.errors.length > 0"
          :loading="busy"
          data-testid="state-machine-publish-submit"
          @click="publish"
        >
          {{ copy.publish }}
        </el-button>
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="runtimeOpen"
      :title="copy.stateMachineCheckTitle"
      size="wide"
      :busy="runtimeLoading || busy"
      @update:model-value="closeRuntime"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
      </el-alert>
      <el-alert :title="copy.stateMachineCheckHint" type="info" :closable="false" show-icon />
      <el-form label-position="top" :disabled="runtimeLoading || busy">
        <div class="state-machine-form-grid">
          <el-form-item :label="copy.stateMachineAvailable" required>
            <el-select
              v-model="availableKey"
              data-testid="state-machine-available"
              @change="changeAvailability"
            >
              <el-option
                v-for="item in available"
                :key="availabilityKey(item)"
                :value="availabilityKey(item)"
                :label="availabilityLabel(item)"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.stateMachineReadMode">
            <el-select
              v-model="readMode"
              data-testid="state-machine-read-mode"
              @change="loadRuntimeDefinition"
            >
              <el-option value="Fixed" :label="copy.stateMachineFixed" />
              <el-option value="Current" :label="copy.stateMachineCurrent" />
            </el-select>
          </el-form-item>
        </div>
        <div v-if="runtimeDefinition" class="state-machine-reference">
          <strong>{{ runtimeDefinition.name }} ({{ runtimeDefinition.nId }})</strong>
          <span data-testid="state-machine-runtime-source">
            {{ sourceLabel(runtimeDefinition.sourceScope, runtimeDefinition.sourceTenantNId) }}
          </span>
          <span data-testid="state-machine-runtime-revision">
            {{ copy.revision }} {{ runtimeDefinition.revision }}
          </span>
        </div>
        <AppDataTable
          v-if="runtimeDefinition"
          table-key="reference-data-state-machine-runtime-nodes"
          :rows="runtimeDefinition.nodes"
          :total="runtimeDefinition.nodes.length"
          :columns="runtimeNodeColumns"
          toolbar-profile="compact"
          selection="none"
        >
          <template #cell-isInitial="{ row }">
            {{ row.isInitial ? copy.trueValue : copy.falseValue }}
          </template>
          <template #cell-isTerminal="{ row }">
            {{ row.isTerminal ? copy.trueValue : copy.falseValue }}
          </template>
          <template #cell-outcome="{ row }">{{ outcomeLabel(row.outcome) }}</template>
        </AppDataTable>
        <div v-if="runtimeDefinition" class="state-machine-form-grid state-machine-evaluation">
          <el-form-item :label="copy.stateMachineCheckFrom" required>
            <el-select v-model="evaluation.fromStatusNId" data-testid="state-machine-from-status">
              <el-option
                v-for="node in runtimeDefinition.nodes"
                :key="node.nId"
                :value="node.nId"
                :label="`${node.name} (${node.nId})`"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.stateMachineCheckAction" :error="validation.runtime" required>
            <el-input v-model="evaluation.actionNId" data-testid="state-machine-action-nid" />
          </el-form-item>
        </div>
        <el-card
          v-if="evaluationResult !== null"
          data-testid="state-machine-evaluation-result"
          shadow="never"
          class="state-machine-result"
        >
          <el-tag :type="evaluationResult.allowedByDefinition ? 'success' : 'warning'">
            {{
              evaluationResult.allowedByDefinition
                ? copy.stateMachineAllowed
                : copy.stateMachineDenied
            }}
          </el-tag>
          <p v-if="evaluationResult.toStatusNId">
            {{ copy.stateMachineTarget }}: <strong>{{ evaluationResult.toStatusNId }}</strong>
          </p>
          <p v-if="evaluationResult.reasonCode">
            {{ copy.stateMachineReasonCode }}: <code>{{ evaluationResult.reasonCode }}</code>
          </p>
        </el-card>
      </el-form>
      <el-skeleton v-if="runtimeLoading && !runtimeDefinition" :rows="5" animated />
      <template #footer>
        <el-button :disabled="runtimeLoading || busy" @click="closeRuntime()">
          {{ copy.close }}
        </el-button>
        <el-button
          type="primary"
          :disabled="!runtimeDefinition"
          :loading="busy"
          data-testid="state-machine-evaluate"
          @click="evaluate"
        >
          {{ copy.stateMachineCheck }}
        </el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.state-machine-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
  overflow: hidden;
}
.state-machine-page :deep(.app-page__body) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.state-machine-page :deep(.app-query-panel) {
  flex: 0 0 auto;
}
.state-machine-query-field {
  display: grid;
  flex: 0 0 180px;
  max-width: 100%;
  min-width: 0;
  gap: var(--ip-space-2);
}
.state-master-detail {
  display: grid;
  flex: 1 1 0;
  grid-template-columns: minmax(250px, 280px) minmax(0, 1fr);
  gap: var(--ip-space-4);
  min-height: 0;
  align-items: stretch;
}
.state-master,
.state-detail-panel {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.state-master {
  gap: var(--ip-space-3);
  overflow: hidden;
}
.state-master :deep(.app-data-table),
.state-detail-panel :deep(.app-data-table) {
  flex: 1 1 0;
  min-height: 0;
}
.state-master :deep(.app-data-table__card),
.state-detail-panel :deep(.app-data-table__card) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}
.state-directory-name {
  display: grid;
  min-width: 0;
  gap: 2px;
}
.state-directory-name strong,
.state-directory-name small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.state-directory-name small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.state-detail-panel {
  min-height: 0;
  padding: var(--ip-space-4);
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--ip-radius-md);
  background: var(--el-bg-color);
}
.state-detail-panel > :deep(.el-empty) {
  flex: 1 1 auto;
  min-height: 0;
}
.state-detail-panel :deep(.el-tabs) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}
.state-detail-panel :deep(.el-tabs__content) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.state-detail-panel :deep(.el-tab-pane) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.state-detail-context {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  margin-bottom: var(--ip-space-3);
}
.state-detail-context h2 {
  margin: 0;
}
.state-detail-context p {
  margin: var(--ip-space-1) 0 0;
  color: var(--el-text-color-secondary);
}
.state-machine-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-1);
}
.state-machine-actions > .el-button {
  margin-left: 0;
}
.state-machine-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 0 var(--ip-space-4);
}
.state-machine-section {
  min-width: 0;
  margin-top: var(--ip-space-5);
}
.state-machine-section-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--ip-space-3);
  margin-bottom: var(--ip-space-3);
}
.state-machine-section-heading h3,
.state-machine-section-heading p {
  margin: 0;
}
.state-machine-section-heading p {
  margin-top: var(--ip-space-1);
  color: var(--el-text-color-secondary);
}
.state-machine-reference {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
  margin-bottom: var(--ip-space-4);
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-muted);
  border-radius: var(--ip-radius-sm);
}
.state-machine-reference strong {
  margin-right: auto;
}
.state-machine-error-list {
  margin: var(--ip-space-2) 0 0;
  padding-left: var(--ip-space-5);
}
.state-machine-evaluation {
  margin-top: var(--ip-space-4);
}
.state-machine-result {
  margin-top: var(--ip-space-2);
}
.state-machine-result p:last-child {
  margin-bottom: 0;
}
@media (max-width: 768px) {
  .state-master-detail {
    grid-template-columns: 1fr;
  }
  .state-machine-section-heading {
    flex-direction: column;
  }
}
</style>
