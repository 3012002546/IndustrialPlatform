<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'

import type {
  ComplianceDisposition,
  ComplianceExport,
  ComplianceMessage,
  LegalHoldCase,
  RetentionPolicy,
} from '@/api/collaboration'
import { ApiError } from '@/api/errors'
import { getCollaborationApi } from '@/api/collaborationRegistry'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import CollaborationStepUpDrawer from '@/components/collaboration/CollaborationStepUpDrawer.vue'
import { downloadBlob } from '@/components/management/download'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import type { AppDataTableColumn } from '@/components/management/AppDataTable'
import { PERMISSIONS } from '@/permissions'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'

const api = getCollaborationApi()
const props = defineProps<{
  page: 'controlled-view' | 'legal-holds' | 'exports' | 'retention'
}>()
const auth = useAuthStore()
const isSystemAdministrator = computed(() => auth.user?.roles.includes('SYSTEM_ADMIN') === true)
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration)
const pageTitle = computed(() =>
  props.page === 'controlled-view'
    ? copy.value.controlledView
    : props.page === 'legal-holds'
      ? copy.value.legalHolds
      : props.page === 'exports'
        ? copy.value.exports
        : copy.value.retention,
)
const pageDescription = computed(() =>
  props.page === 'controlled-view'
    ? copy.value.controlledViewHint
    : copy.value.complianceDescription,
)

const searchRows = ref<ComplianceMessage[]>([])
const dispositions = ref<ComplianceDisposition[]>([])
const legalHolds = ref<LegalHoldCase[]>([])
const exports = ref<ComplianceExport[]>([])
const retention = ref<RetentionPolicy | null>(null)
const loading = ref(false)
const hasSearched = ref(false)
const retentionSaving = ref(false)
const query = reactive({
  conversationNId: '',
  keyword: '',
  from: '',
  until: '',
  readOriginal: false,
})
const dispositionForm = reactive({ subjectType: 'Message', subjectNId: '', reason: '' })
const holdForm = reactive({ conversationNId: '', reason: '' })
const exportForm = reactive({ conversationNId: '', reason: '' })
const retentionForm = reactive({
  messageRetentionDays: 365,
  attachmentRetentionDays: 365,
  auditRetentionDays: 365,
  enabled: true,
  expectedOptimisticVersion: 1,
})
const selectedExport = ref<ComplianceExport | null>(null)
const holdDrawerOpen = ref(false)
const exportDrawerOpen = ref(false)
const holdSaving = ref(false)
const exportSaving = ref(false)
const stepUpDrawerOpen = ref(false)
const stepUpBusy = ref(false)
const stepUpError = ref('')
const stepUpRequest = ref<{
  action: string
  scope: Record<string, unknown>
  scopeChecksum: string
  requestNId: string
  targetNId?: string
  reason?: string
  extra?: Record<string, unknown>
} | null>(null)
let resolveStepUp: ((proof: string | null) => void) | null = null
let pendingStepUp: Promise<string | null> | null = null

const stepUpActor = computed(() => {
  const user = auth.user
  if (!user) return copy.value.stepUpUnavailable
  return `${user.displayName || user.username} (${user.userId})`
})

const canRetention = computed(
  () =>
    auth.hasPermission(PERMISSIONS.collaborationComplianceRetentionUpdate) ||
    auth.hasPermission(PERMISSIONS.collaborationComplianceRetentionManage),
)
const canReviewHold = computed(() =>
  auth.hasPermission(PERMISSIONS.collaborationComplianceLegalHoldReview),
)
const canReleaseHold = computed(() =>
  auth.hasPermission(PERMISSIONS.collaborationComplianceLegalHoldReleaseApprove),
)
const canApproveExport = computed(() =>
  auth.hasPermission(PERMISSIONS.collaborationComplianceExportApprove),
)
const canReadOriginal = computed(() =>
  auth.hasPermission(PERMISSIONS.collaborationComplianceReadOriginal),
)

const searchColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'conversationNId', title: copy.value.conversation, minWidth: 180, filter: false },
  { field: 'messageNId', title: copy.value.message, minWidth: 180, filter: false },
  { field: 'sequence', title: copy.value.sequence, width: 90, sortable: true, filter: false },
  { field: 'senderUserNId', title: copy.value.sender, minWidth: 140, filter: false },
  { field: 'messageType', title: copy.value.type, width: 100, filter: false },
  {
    field: 'acceptedOn',
    title: copy.value.acceptedOn,
    minWidth: 170,
    sortable: true,
    filter: false,
  },
  { field: 'state', title: copy.value.state, width: 100, filter: false },
])

const legalHoldColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'holdCaseNId', title: copy.value.id, minWidth: 180, filter: false },
  { field: 'state', title: copy.value.state, width: 150, filter: false },
  { field: 'reason', title: copy.value.reason, minWidth: 220, filter: false },
  { field: 'createdOn', title: copy.value.createdOn, width: 170, filter: false },
])

const exportColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'exportNId', title: copy.value.id, minWidth: 180, filter: false },
  { field: 'state', title: copy.value.state, width: 150, filter: false },
  { field: 'scopeChecksum', title: copy.value.scopeChecksum, minWidth: 220, filter: false },
  { field: 'createdOn', title: copy.value.createdOn, width: 170, filter: false },
])

function requestId(): string {
  return crypto.randomUUID().replaceAll('-', '')
}

function scope(conversationNId: string): Record<string, unknown> {
  const value = conversationNId.trim()
  return value.length === 0
    ? { schemaVersion: 1, scopeType: 'TimeRange' }
    : { conversationNId: value, schemaVersion: 1, scopeType: 'Conversation' }
}

async function searchMessages(): Promise<void> {
  if (!validateSearchScope()) return
  loading.value = true
  try {
    const request: import('@/api/collaboration').ComplianceSearchRequest = {
      scope: scope(query.conversationNId),
      requestNId: requestId(),
      reason: copy.value.defaultReason,
      readOriginal: query.readOriginal && canReadOriginal.value,
    }
    const keyword = query.keyword.trim()
    if (keyword.length > 0) request.keyword = keyword
    if (query.from || query.until)
      request.scope = {
        ...scope(query.conversationNId),
        scopeType: 'TimeRange',
        ...(query.from ? { fromOn: query.from } : {}),
        ...(query.until ? { toOn: query.until } : {}),
      }
    const action = request.readOriginal ? 'compliance.read-original' : 'compliance.view'
    const proof = await stepUp(
      action,
      '',
      request.requestNId ?? requestId(),
      request.scope as Record<string, unknown>,
      undefined,
      request.reason,
      {
        ...(request.keyword ? { keyword: request.keyword } : {}),
        readOriginal: request.readOriginal,
      },
    )
    if (proof === null) return
    const result = await api.searchCompliance(request, proof)
    searchRows.value = result.items
    hasSearched.value = true
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.loadFailed)
  } finally {
    loading.value = false
  }
}

function resetSearch(): void {
  query.conversationNId = ''
  query.keyword = ''
  query.from = ''
  query.until = ''
  query.readOriginal = false
  searchRows.value = []
  hasSearched.value = false
}

function validateSearchScope(): boolean {
  if (query.from && query.until && new Date(query.from) > new Date(query.until)) {
    ElMessage.warning(copy.value.invalidTimeRange)
    return false
  }
  return true
}

async function loadDispositions(): Promise<void> {
  loading.value = true
  try {
    dispositions.value = await api.listDispositions()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.loadFailed)
  } finally {
    loading.value = false
  }
}

async function createDisposition(): Promise<void> {
  if (dispositionForm.subjectNId.trim().length === 0 || dispositionForm.reason.trim().length === 0)
    return
  const requestNId = requestId()
  const messageNId = dispositionForm.subjectNId.trim()
  const reason = dispositionForm.reason.trim()
  const proof = await stepUp(
    'compliance.dispose',
    '',
    requestNId,
    { schemaVersion: 1, scopeType: 'MessageSet', messageNIds: [messageNId] },
    messageNId,
    reason,
  )
  if (proof === null) return
  try {
    await api.createDisposition(
      { subjectType: dispositionForm.subjectType, subjectNId: messageNId, reason, requestNId },
      proof,
    )
    dispositionForm.subjectNId = ''
    dispositionForm.reason = ''
    await loadDispositions()
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

async function loadLegalHolds(): Promise<void> {
  loading.value = true
  try {
    legalHolds.value = (await api.listLegalHolds({ limit: 100 })).items
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.loadFailed)
  } finally {
    loading.value = false
  }
}

async function createLegalHold(): Promise<void> {
  if (holdForm.conversationNId.trim().length === 0 || holdForm.reason.trim().length === 0) return
  holdSaving.value = true
  const requestNId = requestId()
  const holdScope = scope(holdForm.conversationNId)
  const reason = holdForm.reason.trim()
  const proof = await stepUp('legal-hold.create', '', requestNId, holdScope, undefined, reason)
  if (proof === null) {
    holdSaving.value = false
    return
  }
  try {
    await api.createLegalHold({ scope: holdScope, reason, requestNId }, proof)
    holdForm.conversationNId = ''
    holdForm.reason = ''
    holdDrawerOpen.value = false
    await loadLegalHolds()
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  } finally {
    holdSaving.value = false
  }
}

async function stepUp(
  action: string,
  scopeChecksum: string,
  requestNId: string,
  stepUpScope?: Record<string, unknown>,
  targetNId?: string,
  reason?: string,
  extra?: Record<string, unknown>,
): Promise<string | null> {
  if (pendingStepUp) return pendingStepUp
  stepUpRequest.value = {
    action,
    scope: stepUpScope ?? {},
    scopeChecksum,
    requestNId,
    ...(targetNId ? { targetNId } : {}),
    ...(reason ? { reason } : {}),
    ...(extra ? { extra } : {}),
  }
  if (isSystemAdministrator.value) {
    pendingStepUp = requestStepUpProof(stepUpRequest.value)
      .catch((error: unknown) => {
        ElMessage.error(describeStepUpError(error))
        return null
      })
      .finally(closeStepUp)
    return pendingStepUp
  }
  stepUpError.value = ''
  stepUpBusy.value = false
  stepUpDrawerOpen.value = true
  pendingStepUp = new Promise((resolve) => {
    resolveStepUp = resolve
  })
  return pendingStepUp
}

async function requestStepUpProof(
  request: NonNullable<typeof stepUpRequest.value>,
  password = '',
): Promise<string> {
  const context = await api.createStepUpContext({
    action: request.action,
    requestNId: request.requestNId,
    ...(Object.keys(request.scope).length > 0 ? { scope: request.scope } : {}),
    ...(request.scopeChecksum ? { scopeChecksum: request.scopeChecksum } : {}),
    ...(request.targetNId ? { targetNId: request.targetNId } : {}),
    ...(request.reason ? { reason: request.reason } : {}),
    ...(request.extra ?? {}),
  })
  const proof = await api.createStepUp({
    binding: context.binding,
    currentPassword: password,
    password,
  })
  return proof.proof
}

async function confirmStepUp(password: string): Promise<void> {
  if (stepUpBusy.value || !stepUpRequest.value) return
  stepUpBusy.value = true
  try {
    const proof = await requestStepUpProof(stepUpRequest.value, password)
    const resolve = resolveStepUp
    closeStepUp()
    resolve?.(proof)
  } catch (error) {
    // Keep the business drawer/form intact, but never retain a failed password.
    stepUpError.value = describeStepUpError(error)
  } finally {
    stepUpBusy.value = false
  }
}

function cancelStepUp(): void {
  if (stepUpBusy.value) return
  const resolve = resolveStepUp
  closeStepUp()
  resolve?.(null)
}

function describeStepUpError(error: unknown): string {
  if (error instanceof ApiError) {
    const code = error.details.code
    if (code === 'COLLAB_STEP_UP_BINDING_UNAVAILABLE' || code === 'COLLAB_SERVICE_UNAVAILABLE')
      return copy.value.stepUpServiceUnavailable
    if (
      code === 'COLLAB_STEP_UP_INVALID' &&
      (error.message.includes('密码') || error.message.includes('password'))
    )
      return copy.value.stepUpCredentialFailed
    if (
      code === 'COLLAB_STEP_UP_INVALID' ||
      code === 'COLLAB_SCOPE_CHECKSUM_INVALID' ||
      code === 'COLLAB_SESSION_INVALID'
    )
      return copy.value.stepUpContextInvalid
    if (code === 'COLLAB_STEP_UP_REQUIRED' || code === 'COLLAB_STEP_UP_RATE_LIMITED')
      return copy.value.stepUpCredentialFailed
  }
  return error instanceof Error && error.message ? error.message : copy.value.stepUpUnavailable
}

function closeStepUp(): void {
  stepUpDrawerOpen.value = false
  stepUpRequest.value = null
  stepUpError.value = ''
  resolveStepUp = null
  pendingStepUp = null
}

function stepUpActionLabel(action: string): string {
  const labels: Record<string, string> = {
    'compliance.view': copy.value.controlledView,
    'compliance.read-original': copy.value.readOriginal,
    'compliance.dispose': copy.value.disposition,
    'legal-hold.create': copy.value.createLegalHold,
    'legal-hold.review': copy.value.review,
    'legal-hold.release-request': copy.value.release,
    'legal-hold.release-approve': copy.value.approve,
    'compliance.export.request': copy.value.prepareExport,
    'compliance.export.approve': copy.value.approve,
    'compliance.export.download': copy.value.download,
    'retention.update': copy.value.save,
  }
  return labels[action] ?? copy.value.stepUp
}

function stepUpScopeSummary(scope: Record<string, unknown>): string {
  const conversationNId = typeof scope.conversationNId === 'string' ? scope.conversationNId : ''
  if (conversationNId) return `${copy.value.conversation}: ${conversationNId}`
  const messageNId = Array.isArray(scope.messageNIds) ? String(scope.messageNIds[0] ?? '') : ''
  if (messageNId) return `${copy.value.message}: ${messageNId}`
  const from = typeof scope.fromOn === 'string' ? scope.fromOn : ''
  const until = typeof scope.toOn === 'string' ? scope.toOn : ''
  if (from || until)
    return `${copy.value.from}: ${from || copy.value.notLoaded}；${copy.value.until}: ${until || copy.value.notLoaded}`
  return copy.value.authorizedScope
}

function onStepUpDrawerModelValue(open: boolean): void {
  if (!open) cancelStepUp()
}

async function reviewHold(
  hold: LegalHoldCase,
  action: 'review' | 'release-request' | 'release-approve',
): Promise<void> {
  if (action === 'review' && !canReviewHold.value) return
  if ((action === 'release-request' || action === 'release-approve') && !canReleaseHold.value)
    return
  const requestNId = requestId()
  const resolvedAction =
    action === 'review'
      ? 'legal-hold.review'
      : action === 'release-request'
        ? 'legal-hold.release-request'
        : 'legal-hold.release-approve'
  const proof = await stepUp(
    resolvedAction,
    hold.scopeChecksum ?? '',
    requestNId,
    hold.scope as Record<string, unknown>,
    hold.holdCaseNId,
    hold.reason,
  )
  if (proof === null) return
  try {
    await api.updateLegalHold(hold.holdCaseNId, { action, reason: hold.reason }, proof, requestNId)
    await loadLegalHolds()
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

async function downloadExport(item: ComplianceExport): Promise<void> {
  if (
    item.state !== 'Succeeded' ||
    !auth.hasPermission(PERMISSIONS.collaborationComplianceExportDownload)
  )
    return
  const requestNId = requestId()
  const proof = await stepUp(
    'compliance.export.download',
    item.scopeChecksum,
    requestNId,
    item.scope as Record<string, unknown>,
    item.exportNId,
    copy.value.approvalReason,
  )
  if (proof === null) return
  try {
    await api.authorizeExportDownload(item.exportNId, { requestNId }, proof)
    const blob = await api.downloadExport(item.exportNId, requestNId, proof)
    downloadBlob(blob, `collaboration-export-${item.exportNId}.json`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

async function loadExports(): Promise<void> {
  // The list endpoint is intentionally tenant-scoped and returns metadata only.
  loading.value = true
  try {
    exports.value = (await api.listExports({ limit: 100 })).items
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.loadFailed)
  } finally {
    loading.value = false
  }
}

async function prepareExport(): Promise<void> {
  if (exportForm.reason.trim().length === 0) return
  exportSaving.value = true
  const requestNId = requestId()
  const exportScope = scope(exportForm.conversationNId)
  const reason = exportForm.reason.trim()
  const proof = await stepUp(
    'compliance.export.request',
    '',
    requestNId,
    exportScope,
    undefined,
    reason,
  )
  if (proof === null) {
    exportSaving.value = false
    return
  }
  try {
    const result = await api.prepareExport({ scope: exportScope, reason, requestNId }, proof)
    selectedExport.value = result
    exports.value = [result, ...exports.value]
    exportForm.conversationNId = ''
    exportForm.reason = ''
    exportDrawerOpen.value = false
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  } finally {
    exportSaving.value = false
  }
}

async function approveExport(item: ComplianceExport): Promise<void> {
  if (!canApproveExport.value) return
  const requestNId = requestId()
  const reason = copy.value.approvalReason
  const proof = await stepUp(
    'compliance.export.approve',
    item.scopeChecksum,
    requestNId,
    item.scope as Record<string, unknown>,
    item.exportNId,
    reason,
  )
  if (proof === null) return
  try {
    const updated = await api.approveExport(
      item.exportNId,
      { reason, requestNId },
      requestNId,
      proof,
    )
    selectedExport.value = updated
    exports.value = exports.value.map((candidate) =>
      candidate.exportNId === updated.exportNId ? updated : candidate,
    )
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

async function loadRetention(): Promise<void> {
  loading.value = true
  try {
    const result = await api.getRetention()
    retention.value = result
    retentionForm.messageRetentionDays = result.messageRetentionDays
    retentionForm.attachmentRetentionDays = result.attachmentRetentionDays
    retentionForm.auditRetentionDays = result.auditRetentionDays
    retentionForm.enabled = result.enabled
    retentionForm.expectedOptimisticVersion = result.optimisticVersion
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.loadFailed)
  } finally {
    loading.value = false
  }
}

async function saveRetention(): Promise<void> {
  if (!canRetention.value || retentionSaving.value) return
  retentionSaving.value = true
  try {
    const requestNId = requestId()
    const proof = await stepUp('retention.update', '', requestNId, {
      schemaVersion: 1,
      scopeType: 'TimeRange',
    })
    if (proof === null) return
    retention.value = await api.updateRetention({ ...retentionForm, requestNId }, proof)
    retentionForm.expectedOptimisticVersion = retention.value.optimisticVersion
    ElMessage.success(copy.value.saved)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  } finally {
    retentionSaving.value = false
  }
}

function formatTime(value: string | null | undefined): string {
  return value
    ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' }).format(
        new Date(value),
      )
    : ''
}

async function loadActiveTab(): Promise<void> {
  if (props.page === 'legal-holds') void loadLegalHolds()
  else if (props.page === 'exports') void loadExports()
  else if (props.page === 'retention') void loadRetention()
}

onMounted(() => {
  void loadActiveTab()
})
watch(
  () => props.page,
  () => {
    void loadActiveTab()
  },
)
</script>

<template>
  <AppPage :title="pageTitle" :description="pageDescription">
    <template v-if="props.page === 'legal-holds'" #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.collaborationComplianceLegalHoldCreate">
        <el-button type="primary" @click="holdDrawerOpen = true">{{ copy.create }}</el-button>
      </PermissionGate>
    </template>
    <template v-else-if="props.page === 'exports'" #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.collaborationComplianceExportRequest">
        <el-button type="primary" @click="exportDrawerOpen = true">{{ copy.prepare }}</el-button>
      </PermissionGate>
    </template>
    <section class="collaboration-compliance">
      <template v-if="props.page === 'controlled-view'">
        <AppQueryPanel
          :title="copy.query"
          show-actions
          grid
          @submit="searchMessages"
          @reset="resetSearch"
        >
          <label>
            <span>{{ copy.conversation }}</span>
            <el-input
              v-model="query.conversationNId"
              :placeholder="copy.conversationPlaceholder"
              clearable
            />
          </label>
          <label>
            <span>{{ copy.keyword }}</span>
            <el-input v-model="query.keyword" :placeholder="copy.keywordPlaceholder" clearable />
          </label>
          <label
            ><span>{{ copy.from }}</span
            ><el-date-picker
              v-model="query.from"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss[Z]"
              class="collaboration-compliance__control"
          /></label>
          <label
            ><span>{{ copy.until }}</span
            ><el-date-picker
              v-model="query.until"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss[Z]"
              class="collaboration-compliance__control"
          /></label>
          <label class="collaboration-compliance__checkbox"
            ><span>{{ copy.readOriginal }}</span
            ><el-checkbox v-model="query.readOriginal" :disabled="!canReadOriginal"
          /></label>
        </AppQueryPanel>
        <p v-if="!hasSearched" class="collaboration-compliance__hint">
          {{ copy.controlledViewHint }}
        </p>
        <AppDataTable
          v-else
          table-key="collaboration-compliance-search"
          row-key="messageNId"
          toolbar-profile="hidden"
          :columns="searchColumns"
          :rows="searchRows"
          :total="searchRows.length"
          :loading="loading"
        >
          <template #cell-acceptedOn="{ row }">{{ formatTime(row.acceptedOn) }}</template>
          <template #cell-textContent="{ row }">{{
            row.textContent ? copy.masked : copy.notLoaded
          }}</template>
        </AppDataTable>
        <section class="collaboration-compliance__card">
          <h2>{{ copy.disposition }}</h2>
          <div class="collaboration-compliance__form">
            <label
              ><span>{{ copy.subject }}</span
              ><el-input
                v-model="dispositionForm.subjectNId"
                :placeholder="copy.subjectPlaceholder"
            /></label>
            <label class="wide"
              ><span>{{ copy.reason }}</span
              ><el-input
                v-model="dispositionForm.reason"
                type="textarea"
                :rows="3"
                maxlength="500"
                show-word-limit
              />
            </label>
            <PermissionGate :permission-n-id="PERMISSIONS.collaborationComplianceDispose"
              ><el-button
                type="primary"
                :disabled="
                  dispositionForm.subjectNId.trim().length === 0 ||
                  dispositionForm.reason.trim().length === 0
                "
                @click="createDisposition"
              >
                {{ copy.create }}
              </el-button></PermissionGate
            >
          </div>
        </section>
      </template>

      <template v-else-if="props.page === 'legal-holds'">
        <section class="collaboration-compliance__card">
          <h2>{{ copy.legalHolds }}</h2>
          <AppDataTable
            table-key="collaboration-legal-holds"
            row-key="holdCaseNId"
            toolbar-profile="hidden"
            selection="none"
            :columns="legalHoldColumns"
            :rows="legalHolds"
            :total="legalHolds.length"
            :loading="loading"
          >
            <template #cell-createdOn="{ row }">{{ formatTime(row.createdOn) }}</template>
            <template #actions="{ row }">
              <el-dropdown trigger="click">
                <el-button link type="primary">{{ copy.more }}</el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item
                      v-if="row.state === 'ActivePendingReview' && canReviewHold"
                      @click="reviewHold(row, 'review')"
                      >{{ copy.review }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="
                        (row.state === 'ActivePendingReview' || row.state === 'ActiveReviewed') &&
                        canReleaseHold
                      "
                      @click="reviewHold(row, 'release-request')"
                      >{{ copy.release }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="row.state === 'ReleasePendingApproval' && canReleaseHold"
                      @click="reviewHold(row, 'release-approve')"
                      >{{ copy.approve }}</el-dropdown-item
                    >
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>
          </AppDataTable>
        </section>
        <AppFormDrawer v-model="holdDrawerOpen" :title="copy.createLegalHold" :busy="holdSaving">
          <div class="collaboration-compliance__drawer-form">
            <label>
              <span>{{ copy.conversation }}</span>
              <el-input
                v-model="holdForm.conversationNId"
                :placeholder="copy.conversationPlaceholder"
              />
            </label>
            <label>
              <span>{{ copy.reason }}</span>
              <el-input
                v-model="holdForm.reason"
                type="textarea"
                :rows="4"
                maxlength="500"
                show-word-limit
              />
            </label>
          </div>
          <template #footer>
            <el-button @click="holdDrawerOpen = false">
              {{ localeMessages[locale].common.action.cancel }}
            </el-button>
            <el-button type="primary" :loading="holdSaving" @click="createLegalHold">
              {{ holdSaving ? copy.saving : copy.create }}
            </el-button>
          </template>
        </AppFormDrawer>
      </template>

      <template v-else-if="props.page === 'exports'">
        <section class="collaboration-compliance__card">
          <h2>{{ copy.exports }}</h2>
          <AppDataTable
            table-key="collaboration-exports"
            row-key="exportNId"
            toolbar-profile="hidden"
            selection="none"
            :columns="exportColumns"
            :rows="exports"
            :total="exports.length"
            :loading="loading"
          >
            <template #cell-createdOn="{ row }">{{ formatTime(row.createdOn) }}</template>
            <template #actions="{ row }">
              <el-dropdown trigger="click">
                <el-button link type="primary">{{ copy.more }}</el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <PermissionGate
                      :permission-n-id="PERMISSIONS.collaborationComplianceExportApprove"
                    >
                      <el-dropdown-item
                        v-if="row.state === 'PendingApproval'"
                        @click="approveExport(row)"
                        >{{
                          isSystemAdministrator && row.createdByUserNId === auth.user?.userId
                            ? copy.generateExport
                            : copy.approve
                        }}</el-dropdown-item
                      >
                    </PermissionGate>
                    <PermissionGate
                      :permission-n-id="PERMISSIONS.collaborationComplianceExportDownload"
                    >
                      <el-dropdown-item
                        v-if="row.state === 'Succeeded'"
                        @click="downloadExport(row)"
                      >
                        {{ copy.download }}
                      </el-dropdown-item>
                    </PermissionGate>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>
          </AppDataTable>
        </section>
        <AppFormDrawer v-model="exportDrawerOpen" :title="copy.prepareExport" :busy="exportSaving">
          <div class="collaboration-compliance__drawer-form">
            <label>
              <span>{{ copy.conversation }}</span>
              <el-input
                v-model="exportForm.conversationNId"
                :placeholder="copy.conversationPlaceholder"
              />
            </label>
            <label>
              <span>{{ copy.reason }}</span>
              <el-input
                v-model="exportForm.reason"
                type="textarea"
                :rows="4"
                maxlength="500"
                show-word-limit
              />
            </label>
          </div>
          <template #footer>
            <el-button @click="exportDrawerOpen = false">
              {{ localeMessages[locale].common.action.cancel }}
            </el-button>
            <el-button type="primary" :loading="exportSaving" @click="prepareExport">
              {{ exportSaving ? copy.saving : copy.prepare }}
            </el-button>
          </template>
        </AppFormDrawer>
      </template>

      <template v-else>
        <section class="collaboration-compliance__card">
          <h2>{{ copy.retention }}</h2>
          <div v-if="retention" class="collaboration-compliance__form">
            <label
              ><span>{{ copy.messageRetentionDays }}</span
              ><el-input-number
                v-model.number="retentionForm.messageRetentionDays"
                min="1"
                max="3650" /></label
            ><label
              ><span>{{ copy.attachmentRetentionDays }}</span
              ><el-input-number
                v-model.number="retentionForm.attachmentRetentionDays"
                min="1"
                max="3650" /></label
            ><label
              ><span>{{ copy.auditRetentionDays }}</span
              ><el-input-number
                v-model.number="retentionForm.auditRetentionDays"
                min="1"
                max="3650" /></label
            ><label
              ><span>{{ copy.enabled }}</span
              ><el-switch v-model="retentionForm.enabled" /></label
            ><el-button
              type="primary"
              :disabled="!canRetention"
              :loading="retentionSaving"
              @click="saveRetention"
            >
              {{ retentionSaving ? copy.saving : copy.save }}
            </el-button>
          </div>
          <p v-else class="collaboration-compliance__hint">{{ copy.noData }}</p>
        </section>
      </template>
    </section>
  </AppPage>
  <CollaborationStepUpDrawer
    :model-value="stepUpDrawerOpen"
    :actor="stepUpActor"
    :action-label="stepUpActionLabel(stepUpRequest?.action ?? '')"
    :scope-summary="stepUpScopeSummary(stepUpRequest?.scope ?? {})"
    :busy="stepUpBusy"
    :error="stepUpError"
    @update:model-value="onStepUpDrawerModelValue"
    @confirm="confirmStepUp"
    @cancel="cancelStepUp"
  />
</template>

<style scoped>
.collaboration-compliance {
  display: grid;
  gap: var(--ip-space-4);
}
.collaboration-compliance :deep(.app-query-panel__body--grid) {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}
.collaboration-compliance label {
  display: grid;
  gap: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.collaboration-compliance__control,
.collaboration-compliance :deep(.el-input),
.collaboration-compliance :deep(.el-input-number),
.collaboration-compliance :deep(.el-date-editor) {
  width: 100%;
}
.collaboration-compliance__card {
  display: grid;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  background: var(--ip-color-bg-container);
  overflow: auto;
}
.collaboration-compliance__card h2 {
  margin: 0;
  font-size: var(--ip-font-size-md);
}
.collaboration-compliance__card-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}
.collaboration-compliance__form {
  display: grid;
  grid-template-columns: repeat(4, minmax(140px, 1fr));
  align-items: end;
  gap: var(--ip-space-3);
}
.collaboration-compliance__form .wide {
  grid-column: span 2;
}
.collaboration-compliance__drawer-form {
  display: grid;
  gap: var(--ip-space-4);
}
.collaboration-compliance__drawer-form label {
  display: grid;
  gap: var(--ip-space-1);
}
.collaboration-compliance table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--ip-font-size-sm);
}
.collaboration-compliance th,
.collaboration-compliance td {
  padding: var(--ip-space-2);
  border-bottom: 1px solid var(--ip-color-border);
  text-align: left;
  vertical-align: top;
}
.collaboration-compliance th {
  color: var(--ip-color-text-secondary);
  font-weight: 600;
  white-space: nowrap;
}
.collaboration-compliance td {
  overflow-wrap: anywhere;
}
.collaboration-compliance__hint {
  margin: 0;
  color: var(--ip-color-text-secondary);
}
.collaboration-compliance :deep(.vxe-table--render-default) {
  --vxe-ui-font-color: var(--ip-color-text-primary);
  --vxe-ui-font-lighten-color: var(--ip-color-text-secondary);
  --vxe-ui-font-darken-color: var(--ip-color-text-primary);
  --vxe-ui-font-disabled-color: var(--ip-color-text-disabled);
  --vxe-ui-input-placeholder-color: var(--ip-color-text-tertiary);
  --vxe-ui-layout-background-color: var(--ip-color-bg-container);
  --vxe-ui-table-header-background-color: var(--ip-color-bg-muted);
  --vxe-ui-table-column-to-row-background-color: var(--ip-color-bg-muted);
  --vxe-ui-table-footer-background-color: var(--ip-color-bg-container);
  --vxe-ui-table-border-color: var(--ip-color-border);
  --collaboration-compliance-table-hover-background: color-mix(
    in srgb,
    var(--ip-color-primary) 12%,
    var(--ip-color-bg-container)
  );
  --collaboration-compliance-table-selection-background: color-mix(
    in srgb,
    var(--ip-color-primary) 20%,
    var(--ip-color-bg-container)
  );
  --collaboration-compliance-table-selection-hover-background: color-mix(
    in srgb,
    var(--ip-color-primary) 28%,
    var(--ip-color-bg-container)
  );
  --vxe-ui-table-column-hover-background-color: var(
    --collaboration-compliance-table-hover-background
  );
  --vxe-ui-table-column-current-background-color: var(
    --collaboration-compliance-table-selection-background
  );
  --vxe-ui-table-column-hover-current-background-color: var(
    --collaboration-compliance-table-selection-hover-background
  );
  --vxe-ui-table-row-hover-background-color: var(--collaboration-compliance-table-hover-background);
  --vxe-ui-table-row-striped-background-color: color-mix(
    in srgb,
    var(--ip-color-bg-muted) 72%,
    var(--ip-color-bg-container)
  );
  --vxe-ui-table-row-hover-striped-background-color: var(
    --collaboration-compliance-table-hover-background
  );
  --vxe-ui-table-row-current-background-color: var(
    --collaboration-compliance-table-selection-background
  );
  --vxe-ui-table-row-hover-current-background-color: var(
    --collaboration-compliance-table-selection-hover-background
  );
  --vxe-ui-table-row-checkbox-checked-background-color: var(
    --collaboration-compliance-table-selection-background
  );
  --vxe-ui-table-row-hover-checkbox-checked-background-color: var(
    --collaboration-compliance-table-selection-hover-background
  );
  --vxe-ui-table-row-radio-checked-background-color: var(
    --collaboration-compliance-table-selection-background
  );
  --vxe-ui-table-row-hover-radio-checked-background-color: var(
    --collaboration-compliance-table-selection-hover-background
  );
  --vxe-ui-table-fixed-scrolling-box-shadow-color: color-mix(
    in srgb,
    var(--ip-color-text-primary) 22%,
    transparent
  );
}
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--header-wrapper),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--body-wrapper),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--footer-wrapper),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--fixed-left-wrapper),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--fixed-right-wrapper) {
  color: var(--ip-color-text-primary);
  background-color: var(--ip-color-bg-container);
}
.collaboration-compliance :deep(.vxe-table--render-default .vxe-header--column) {
  color: var(--ip-color-text-primary);
  background-color: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}
.collaboration-compliance :deep(.vxe-table--render-default .vxe-body--column),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-footer--column) {
  color: var(--ip-color-text-primary);
  background-color: var(--ip-color-bg-container);
  border-color: var(--ip-color-border);
}
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--empty-block),
.collaboration-compliance :deep(.vxe-table--render-default .vxe-table--empty-placeholder) {
  color: var(--ip-color-text-tertiary);
  background-color: var(--ip-color-bg-container);
}
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--stripe > .vxe-body--column) {
  background-color: color-mix(in srgb, var(--ip-color-bg-muted) 72%, var(--ip-color-bg-container));
}
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--hover > .vxe-body--column) {
  background-color: var(--collaboration-compliance-table-hover-background);
}
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--hover.row--current > .vxe-body--column),
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--hover.row--checked > .vxe-body--column),
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--hover.row--radio > .vxe-body--column) {
  background-color: var(--collaboration-compliance-table-selection-hover-background);
}
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--current > .vxe-body--column),
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--checked > .vxe-body--column),
.collaboration-compliance
  :deep(.vxe-table--render-default .vxe-body--row.row--radio > .vxe-body--column) {
  background-color: var(--collaboration-compliance-table-selection-background);
}
@media (max-width: 900px) {
  .collaboration-compliance :deep(.app-query-panel__body--grid),
  .collaboration-compliance__form {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (max-width: 560px) {
  .collaboration-compliance :deep(.app-query-panel__body--grid),
  .collaboration-compliance__form {
    grid-template-columns: 1fr;
  }
  .collaboration-compliance__form .wide {
    grid-column: auto;
  }
  .collaboration-compliance th,
  .collaboration-compliance td {
    min-width: 120px;
  }
}
</style>
