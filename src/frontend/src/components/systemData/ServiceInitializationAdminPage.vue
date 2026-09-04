<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { ElIcon } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'

import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import type {
  InitializationOperationDto,
  InitializationPlanDto,
  InitializationRegistrationSummaryDto,
} from '@/api/systemData/managementTypes'
import { localeMessages } from '@/localization/i18n'
import { interpolate, systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'

type InitializationTab = 'registrations' | 'seedsets' | 'plans' | 'operations' | 'environment'

const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataServiceInitializationView,
  },
)

const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'initialization'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)

const tab = ref<InitializationTab>('registrations')
const selectedRegistration = ref<InitializationRegistrationSummaryDto | null>(null)
const selectedPlanNId = ref('')
const selectedOperationNId = ref('')
const approvalReason = ref('')
const backupReference = ref('')
const formError = ref('')
const polling = ref(false)
const registrationDrawerOpen = ref(false)
const planDrawerOpen = ref(false)

const registration = ref({
  serviceKey: '',
  moduleKey: '',
  requestedVersion: '',
  logicalDatabaseName: '',
  artifactChecksum: '',
})
const plan = ref({
  serviceKey: '',
  moduleKey: '',
  requestedVersion: '',
  desiredState: 'SourceOfTruth',
})

const selectedRegistrationDetail = computed(() => store.initializationRegistration)
const selectedPlan = computed<InitializationPlanDto | null>(
  () =>
    store.initializationPlans?.items.find((item) => item.planNId === selectedPlanNId.value) ?? null,
)
const selectedOperation = computed<InitializationOperationDto | null>(
  () =>
    store.initializationOperations?.items.find(
      (item) => item.operationNId === selectedOperationNId.value,
    ) ?? null,
)
const selectedSeeds = computed(() => selectedRegistrationDetail.value?.seedSets ?? [])
const registrationRows = computed(() =>
  (store.initializationRegistrations?.items ?? []).map((item) => ({
    ...item,
    registrationKey: `${item.serviceKey}/${item.moduleKey}`,
  })),
)
const activeOperations = computed(() =>
  (store.initializationOperations?.items ?? []).some((item) =>
    ['Queued', 'Running'].includes(item.status),
  ),
)
const tabs = computed(() => [
  { id: 'registrations' as const, label: copy.value.registrations },
  { id: 'seedsets' as const, label: copy.value.seedsets },
  { id: 'plans' as const, label: copy.value.plans },
  { id: 'operations' as const, label: copy.value.operations },
  { id: 'environment' as const, label: copy.value.environment },
])

const registrationColumns = computed(() => [
  { field: 'serviceKey', title: copy.value.service, minWidth: 160 },
  { field: 'moduleKey', title: copy.value.module, minWidth: 160 },
  { field: 'logicalDatabaseName', title: copy.value.logicalDatabase, minWidth: 160 },
  { field: 'provider', title: copy.value.provider, width: 120 },
  { field: 'migrationVersion', title: copy.value.migrationVersion, width: 140 },
  { field: 'status', title: copy.value.status, width: 120 },
])
const planColumns = computed(() => [
  { field: 'planNId', title: copy.value.plan, minWidth: 160 },
  { field: 'serviceKey', title: copy.value.service, minWidth: 140 },
  { field: 'moduleKey', title: copy.value.module, minWidth: 140 },
  { field: 'planChecksum', title: copy.value.artifactChecksum, minWidth: 200 },
  { field: 'riskLevel', title: copy.value.risk, width: 120 },
  { field: 'expiresOn', title: copy.value.expiry, minWidth: 180 },
])
const operationColumns = computed(() => [
  { field: 'operationNId', title: copy.value.operation, minWidth: 180 },
  { field: 'serviceKey', title: copy.value.service, minWidth: 140 },
  { field: 'moduleKey', title: copy.value.module, minWidth: 140 },
  { field: 'status', title: copy.value.status, width: 130 },
  { field: 'phase', title: copy.value.phase, width: 150 },
  { field: 'steps', title: copy.value.steps, minWidth: 260 },
])

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(localization.locale, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(date)
}

function seedValue(value: string[] | string | undefined): string {
  return Array.isArray(value) ? value.join(', ') : (value ?? '—')
}

function operationSteps(operation: InitializationOperationDto): string {
  return operation.steps
    .map(
      (step) =>
        `${systemDataEnumLabel(localization.locale, step.phase)}:${systemDataEnumLabel(localization.locale, step.status)} (${step.phase}:${step.status})`,
    )
    .join(' / ')
}

function seedObservationSummary(observation: Record<string, unknown>): string {
  const seedKey = String(observation.seedKey ?? '')
  const status = systemDataEnumLabel(localization.locale, String(observation.status ?? ''))
  const version = String(observation.seedVersion ?? '')
  return [seedKey, version, status].filter(Boolean).join(' · ')
}

async function exportRegistrations(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportInitializationRegistrations({
    quantity: request.quantity,
    columns: request.columns,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

async function exportOperations(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportInitializationOperations({
    quantity: request.quantity,
    columns: request.columns,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

function selectTab(next: InitializationTab): void {
  tab.value = next
}

function onTabKeydown(event: KeyboardEvent, index: number): void {
  const last = tabs.value.length - 1
  let nextIndex: number | null = null
  if (event.key === 'ArrowRight' || event.key === 'ArrowDown')
    nextIndex = index === last ? 0 : index + 1
  if (event.key === 'ArrowLeft' || event.key === 'ArrowUp')
    nextIndex = index === 0 ? last : index - 1
  if (event.key === 'Home') nextIndex = 0
  if (event.key === 'End') nextIndex = last
  if (nextIndex !== null) {
    event.preventDefault()
    const next = tabs.value[nextIndex]
    if (next) {
      selectTab(next.id)
      document.getElementById(`systemdata-init-tab-${next.id}`)?.focus()
    }
  }
}

function selectRegistrationRows(rows: unknown): void {
  const next = (rows as InitializationRegistrationSummaryDto[])[0] ?? null
  selectedRegistration.value = next
  store.clearInitializationRegistrationSelection()
  selectedPlanNId.value = ''
  selectedOperationNId.value = ''
  store.clearInitializationPlanSelection()
  if (next === null) return
  registration.value = {
    serviceKey: next.serviceKey,
    moduleKey: next.moduleKey,
    requestedVersion: next.migrationVersion,
    logicalDatabaseName: next.logicalDatabaseName,
    artifactChecksum: '',
  }
  plan.value = {
    serviceKey: next.serviceKey,
    moduleKey: next.moduleKey,
    requestedVersion: next.migrationVersion,
    desiredState: next.desiredState,
  }
  void store.loadInitializationRegistration(next.serviceKey, next.moduleKey)
}

function selectPlanRows(rows: unknown): void {
  selectPlan((rows as InitializationPlanDto[])[0]?.planNId ?? '')
}

function selectPlan(planNId: string): void {
  selectedPlanNId.value = planNId
  if (planNId) void store.selectInitializationPlan(planNId)
  else store.clearInitializationPlanSelection()
}

function selectOperationRows(rows: unknown): void {
  selectedOperationNId.value = (rows as InitializationOperationDto[])[0]?.operationNId ?? ''
}

async function register(): Promise<void> {
  formError.value = ''
  if (Object.values(registration.value).some((value) => !value.trim())) {
    formError.value = copy.value.validationHint
    return
  }
  await store.registerInitialization({
    ...registration.value,
    manifestVersion: '1',
  })
  if (!store.error) registrationDrawerOpen.value = false
}

async function createPlan(): Promise<void> {
  formError.value = ''
  if (Object.values(plan.value).some((value) => !value.trim())) {
    formError.value = copy.value.validationHint
    return
  }
  await store.createInitializationPlan({ ...plan.value })
  if (!store.error) planDrawerOpen.value = false
}

async function createApproval(): Promise<void> {
  if (!selectedPlan.value || store.loading) return
  await store.createApproval(selectedPlan.value.planNId, approvalReason.value.trim())
}

async function captureBackup(): Promise<void> {
  if (!selectedPlan.value || !backupReference.value.trim() || store.loading) return
  await store.createBackupEvidence(selectedPlan.value.planNId, backupReference.value.trim())
}

async function verifyBackup(): Promise<void> {
  const evidence = store.initializationBackupEvidence
  if (!evidence || evidence.planNId !== selectedPlanNId.value || store.loading) return
  await store.verifyBackupEvidence(evidence.evidenceNId)
}

async function apply(): Promise<void> {
  const currentPlan = selectedPlan.value
  if (!currentPlan || store.loading) return
  try {
    await ElMessageBox.confirm(
      interpolate(copy.value.confirmApplyBody, {
        service: currentPlan.serviceKey,
        module: currentPlan.moduleKey,
        version: currentPlan.requestedMigrationVersion,
      }),
      copy.value.confirmApplyTitle,
      {
        type: 'warning',
        confirmButtonText: commonCopy.value.confirm,
        cancelButtonText: commonCopy.value.cancel,
      },
    )
  } catch {
    return
  }
  await store.applyInitialization({
    planNId: currentPlan.planNId,
    moduleKey: currentPlan.moduleKey,
    requestedVersion: currentPlan.requestedMigrationVersion,
  })
}

async function cancelOperation(operation: InitializationOperationDto): Promise<void> {
  if (store.loading || !['Queued', 'Running'].includes(operation.status)) return
  try {
    await ElMessageBox.confirm(
      `${operation.serviceKey} / ${operation.moduleKey} · ${copy.value.cancelOperation}`,
      copy.value.cancelOperation,
      {
        type: 'warning',
        confirmButtonText: commonCopy.value.confirm,
        cancelButtonText: commonCopy.value.cancel,
      },
    )
  } catch {
    return
  }
  await store.cancelInitialization(operation.operationNId)
}

let pollingTimer: number | undefined
const clearPollingTimer = (): void => {
  if (pollingTimer !== undefined) {
    window.clearInterval(pollingTimer)
    pollingTimer = undefined
  }
  polling.value = false
}

function startPolling(): void {
  if (pollingTimer !== undefined || !activeOperations.value) return
  polling.value = true
  pollingTimer = window.setInterval(() => {
    if (!activeOperations.value || store.loading) {
      if (!activeOperations.value) clearPollingTimer()
      return
    }
    void store.retry('service-initialization')
  }, 5000)
}

function togglePolling(): void {
  if (pollingTimer !== undefined) clearPollingTimer()
  else startPolling()
}

watch(activeOperations, (active) => {
  if (active) startPolling()
  else clearPollingTimer()
})

onMounted(() => {
  if (typeof window !== 'undefined') startPolling()
})
onBeforeUnmount(clearPollingTimer)
</script>

<template>
  <SystemDataAdminFrame
    kind="service-initialization"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
  >
    <div class="systemdata-init-tabs" role="tablist" :aria-label="pageTitle">
      <button
        v-for="(item, index) in tabs"
        :id="`systemdata-init-tab-${item.id}`"
        :key="item.id"
        type="button"
        role="tab"
        :aria-controls="`systemdata-init-panel-${item.id}`"
        :aria-selected="tab === item.id"
        :aria-pressed="tab === item.id"
        :tabindex="tab === item.id ? 0 : -1"
        @click="selectTab(item.id)"
        @keydown="onTabKeydown($event, index)"
      >
        {{ item.label }}
      </button>
    </div>

    <section
      v-if="tab === 'registrations'"
      id="systemdata-init-panel-registrations"
      role="tabpanel"
      aria-labelledby="systemdata-init-tab-registrations"
      class="systemdata-init-panel"
    >
      <h2>{{ copy.registerTitle }}</h2>
      <p class="systemdata-init-hint">{{ copy.validationHint }}</p>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationRegister">
        <el-button type="primary" :disabled="store.loading" @click="registrationDrawerOpen = true">
          <ElIcon class="systemdata-page-action-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.register }}
        </el-button>
      </PermissionGate>
      <AppDataTable
        v-if="registrationRows.length"
        table-key="systemdata-initialization-registrations"
        route-key="systemdata-service-initialization"
        row-key="registrationKey"
        selection="single"
        :rows="registrationRows"
        :total="store.initializationRegistrations?.total ?? 0"
        :columns="registrationColumns"
        :exporter="exportRegistrations"
        @selection-change="selectRegistrationRows"
      >
        <template #cell-status="{ row }">
          {{ systemDataEnumLabel(localization.locale, row.status) }} ·
          {{ systemDataEnumLabel(localization.locale, row.desiredState) }}
        </template>
      </AppDataTable>
      <AppEmptyState v-else :title="copy.noRegistration" />
    </section>

    <section
      v-else-if="tab === 'seedsets'"
      id="systemdata-init-panel-seedsets"
      role="tabpanel"
      aria-labelledby="systemdata-init-tab-seedsets"
      class="systemdata-init-panel"
    >
      <h2>{{ copy.seedsets }}</h2>
      <p class="systemdata-init-hint">{{ copy.seedsetsHint }}</p>
      <AppEmptyState v-if="!selectedRegistration" :title="commonCopy.selectFirst" />
      <AppEmptyState v-else-if="!selectedSeeds.length" :title="copy.noSeedsets" />
      <div v-else class="systemdata-init-seeds" role="list">
        <article
          v-for="seed in selectedSeeds"
          :key="seed.seedKey"
          class="systemdata-init-seed"
          role="listitem"
        >
          <strong>{{ seed.seedKey }}</strong>
          <dl>
            <div>
              <dt>{{ copy.migrationVersion }}</dt>
              <dd>{{ seed.seedVersion }}</dd>
            </div>
            <div>
              <dt>{{ copy.status }}</dt>
              <dd>{{ systemDataEnumLabel(localization.locale, seed.seedClass) }}</dd>
            </div>
            <div>
              <dt>{{ copy.topology }}</dt>
              <dd>{{ seed.scope }}</dd>
            </div>
            <div>
              <dt>{{ copy.requiredPolicies }}</dt>
              <dd>{{ seed.requiredForReadiness ? commonCopy.current : commonCopy.unknown }}</dd>
            </div>
            <div>
              <dt>{{ copy.environment }}</dt>
              <dd>{{ seedValue(seed.allowedEnvironments) }}</dd>
            </div>
            <div v-if="seed.dependsOnMigrationVersion">
              <dt>{{ copy.migrationVersion }}</dt>
              <dd>{{ seed.dependsOnMigrationVersion }}</dd>
            </div>
          </dl>
        </article>
      </div>
    </section>

    <section
      v-else-if="tab === 'plans'"
      id="systemdata-init-panel-plans"
      role="tabpanel"
      aria-labelledby="systemdata-init-tab-plans"
      class="systemdata-init-panel"
    >
      <h2>{{ copy.planTitle }}</h2>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationPlan">
        <el-button type="primary" :disabled="store.loading" @click="planDrawerOpen = true">
          <ElIcon class="systemdata-page-action-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.createPlan }}
        </el-button>
      </PermissionGate>
      <div
        v-if="store.initializationPlans?.items.length"
        class="systemdata-init-plan-index"
        role="list"
      >
        <button
          v-for="item in store.initializationPlans.items"
          :key="item.planNId"
          type="button"
          role="listitem"
          :aria-pressed="selectedPlanNId === item.planNId"
          @click="selectPlan(item.planNId)"
        >
          {{ item.planNId }} · {{ item.planChecksum }}
        </button>
      </div>
      <AppDataTable
        v-if="store.initializationPlans?.items.length"
        table-key="systemdata-initialization-plans"
        route-key="systemdata-service-initialization"
        row-key="planNId"
        selection="single"
        :rows="store.initializationPlans.items"
        :total="store.initializationPlans.total"
        :columns="planColumns"
        @selection-change="selectPlanRows"
      >
        <template #cell-riskLevel="{ row }">{{
          systemDataEnumLabel(localization.locale, row.riskLevel)
        }}</template>
        <template #cell-expiresOn="{ row }">{{ formatDate(row.expiresOn) }}</template>
      </AppDataTable>
      <AppEmptyState v-else-if="!selectedPlan" :title="copy.selectPlan" />
      <article v-if="selectedPlan" class="systemdata-init-plan-detail">
        <h3>{{ selectedPlan.planNId }}</h3>
        <dl class="systemdata-init-facts">
          <div>
            <dt>{{ copy.service }}</dt>
            <dd>{{ selectedPlan.serviceKey }}</dd>
          </div>
          <div>
            <dt>{{ copy.module }}</dt>
            <dd>{{ selectedPlan.moduleKey }}</dd>
          </div>
          <div>
            <dt>{{ copy.migrationVersion }}</dt>
            <dd>
              {{ selectedPlan.currentMigrationVersion }} →
              {{ selectedPlan.requestedMigrationVersion }}
            </dd>
          </div>
          <div>
            <dt>{{ copy.risk }}</dt>
            <dd>{{ systemDataEnumLabel(localization.locale, selectedPlan.riskLevel) }}</dd>
          </div>
          <div>
            <dt>{{ copy.expiry }}</dt>
            <dd>{{ formatDate(selectedPlan.expiresOn) }}</dd>
          </div>
          <div>
            <dt>{{ copy.requiredPolicies }}</dt>
            <dd>{{ selectedPlan.requiredPolicies }}</dd>
          </div>
        </dl>
        <ol class="systemdata-init-steps">
          <li v-for="step in selectedPlan.steps" :key="step.sequence">
            <strong
              >{{ step.sequence }}.
              {{ systemDataEnumLabel(localization.locale, step.stepKind) }}</strong
            >
            · {{ systemDataEnumLabel(localization.locale, step.riskLevel) }}
            <span v-if="step.preconditionSummary"> · {{ step.preconditionSummary }}</span>
          </li>
        </ol>
        <div class="systemdata-init-gates">
          <label>{{ copy.approvalReason }}<el-input v-model="approvalReason" /></label>
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationApprove">
            <el-button type="primary" :disabled="store.loading" @click="createApproval">
              {{ copy.createApproval }}
            </el-button>
          </PermissionGate>
          <label>{{ copy.backupReference }}<el-input v-model="backupReference" /></label>
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationBackup">
            <el-button
              type="default"
              :disabled="store.loading || !backupReference.trim()"
              @click="captureBackup"
            >
              {{ copy.captureBackup }}
            </el-button>
          </PermissionGate>
          <div
            v-if="store.initializationBackupEvidence"
            class="systemdata-init-evidence"
            role="status"
          >
            <span
              >{{ copy.backupCaptured }} ·
              {{
                systemDataEnumLabel(localization.locale, store.initializationBackupEvidence.status)
              }}</span
            >
            <span v-if="store.initializationBackupEvidence.verifiedOn">
              · {{ copy.backupVerified }}</span
            >
            <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationBackup">
              <el-button
                v-if="store.initializationBackupEvidence.status !== 'Verified'"
                link
                type="success"
                :disabled="store.loading"
                @click="verifyBackup"
              >
                {{ copy.verifyBackup }}
              </el-button>
            </PermissionGate>
          </div>
          <p class="systemdata-init-impact">{{ copy.applyImpact }}</p>
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationApply">
            <el-button type="primary" :disabled="store.loading" @click="apply">{{
              copy.apply
            }}</el-button>
          </PermissionGate>
        </div>
      </article>
    </section>

    <section
      v-else-if="tab === 'operations'"
      id="systemdata-init-panel-operations"
      role="tabpanel"
      aria-labelledby="systemdata-init-tab-operations"
      class="systemdata-init-panel"
    >
      <div class="systemdata-init-panel-heading">
        <h2>{{ copy.operation }}</h2>
        <el-button v-if="activeOperations" type="default" @click="togglePolling">
          {{ polling ? copy.stopPolling : copy.poll }}
        </el-button>
      </div>
      <AppDataTable
        v-if="store.initializationOperations?.items.length"
        table-key="systemdata-initialization-operations"
        route-key="systemdata-service-initialization"
        row-key="operationNId"
        selection="single"
        :rows="store.initializationOperations.items"
        :total="store.initializationOperations.total"
        :columns="operationColumns"
        :exporter="exportOperations"
        @selection-change="selectOperationRows"
      >
        <template #cell-status="{ row }">{{
          systemDataEnumLabel(localization.locale, row.status)
        }}</template>
        <template #cell-phase="{ row }">{{
          systemDataEnumLabel(localization.locale, row.phase)
        }}</template>
        <template #cell-steps="{ row }">{{ operationSteps(row) }}</template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationCancel">
            <el-button
              v-if="['Queued', 'Running'].includes(row.status)"
              link
              type="danger"
              :disabled="store.loading"
              @click="cancelOperation(row)"
            >
              {{ copy.cancelOperation }}
            </el-button>
          </PermissionGate>
        </template>
      </AppDataTable>
      <AppEmptyState v-else :title="copy.noOperations" />
      <article v-if="selectedOperation" class="systemdata-init-operation-detail">
        <h3>{{ copy.operationDetail }} · {{ selectedOperation.operationNId }}</h3>
        <dl class="systemdata-init-facts">
          <div>
            <dt>{{ copy.status }}</dt>
            <dd>{{ systemDataEnumLabel(localization.locale, selectedOperation.status) }}</dd>
          </div>
          <div>
            <dt>{{ copy.phase }}</dt>
            <dd>{{ systemDataEnumLabel(localization.locale, selectedOperation.phase) }}</dd>
          </div>
          <div>
            <dt>{{ copy.trace }}</dt>
            <dd>{{ selectedOperation.traceId }}</dd>
          </div>
          <div>
            <dt>{{ copy.expiry }}</dt>
            <dd>{{ formatDate(selectedOperation.timeoutOn) }}</dd>
          </div>
        </dl>
        <ol class="systemdata-init-steps">
          <li v-for="step in selectedOperation.steps" :key="`${step.sequence}-${step.phase}`">
            {{ step.sequence }}. {{ systemDataEnumLabel(localization.locale, step.phase) }} ·
            {{ systemDataEnumLabel(localization.locale, step.status) }}
          </li>
        </ol>
        <ul v-if="selectedOperation.seedObservations?.length" class="systemdata-init-observations">
          <li v-for="(observation, index) in selectedOperation.seedObservations" :key="index">
            {{ seedObservationSummary(observation) }}
          </li>
        </ul>
        <p
          v-if="selectedOperation.sanitizedErrorCode || selectedOperation.sanitizedErrorSummary"
          role="alert"
        >
          {{ copy.sanitizedError }}: {{ selectedOperation.sanitizedErrorCode }} ·
          {{ selectedOperation.sanitizedErrorSummary }}
        </p>
      </article>
      <p class="systemdata-init-hint">{{ copy.applyImpact }}</p>
    </section>

    <section
      v-else
      id="systemdata-init-panel-environment"
      role="tabpanel"
      aria-labelledby="systemdata-init-tab-environment"
      class="systemdata-init-panel"
    >
      <h2>{{ copy.environmentTitle }}</h2>
      <AppEmptyState v-if="!store.initializationPolicy" :title="copy.noPolicy" />
      <dl v-else class="systemdata-init-facts">
        <div>
          <dt>{{ copy.environment }}</dt>
          <dd>
            {{
              systemDataEnumLabel(localization.locale, store.initializationPolicy.environmentKind)
            }}
            ({{ store.initializationPolicy.environmentNId }})
          </dd>
        </div>
        <div>
          <dt>{{ copy.policy }}</dt>
          <dd>
            {{
              systemDataEnumLabel(
                localization.locale,
                store.initializationPolicy.initializationPolicy,
              )
            }}
          </dd>
        </div>
        <div>
          <dt>{{ copy.approvalRequired }}</dt>
          <dd>{{ store.initializationPolicy.approvalRequired ? copy.yes : copy.no }}</dd>
        </div>
        <div>
          <dt>{{ copy.backupRequired }}</dt>
          <dd>{{ store.initializationPolicy.backupRequired ? copy.yes : copy.no }}</dd>
        </div>
        <div>
          <dt>{{ copy.policyRevision }}</dt>
          <dd>
            {{ store.initializationPolicy.policyRevision }} ·
            {{ store.initializationPolicy.isExplicit ? copy.status : copy.topology }}
          </dd>
        </div>
        <div>
          <dt>{{ copy.expiry }}</dt>
          <dd>
            {{ store.initializationPolicy.planTtlSeconds }}s /
            {{ store.initializationPolicy.planTimeoutSeconds }}s /
            {{ store.initializationPolicy.applyTimeoutSeconds }}s
          </dd>
        </div>
      </dl>
    </section>

    <AppFormDrawer
      v-model="registrationDrawerOpen"
      :busy="store.loading"
      :title="copy.registerTitle"
      size="medium"
      @submit="register"
    >
      <el-form label-width="150px">
        <el-form-item :label="copy.service">
          <el-input v-model="registration.serviceKey" :placeholder="copy.servicePlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.module">
          <el-input v-model="registration.moduleKey" :placeholder="copy.modulePlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.requestedVersion">
          <el-input v-model="registration.requestedVersion" />
        </el-form-item>
        <el-form-item :label="copy.databaseName">
          <el-input v-model="registration.logicalDatabaseName" />
        </el-form-item>
        <el-form-item :label="copy.artifactChecksum">
          <el-input v-model="registration.artifactChecksum" />
        </el-form-item>
      </el-form>
      <p v-if="formError" role="alert">{{ formError }}</p>
    </AppFormDrawer>

    <AppFormDrawer
      v-model="planDrawerOpen"
      :busy="store.loading"
      :title="copy.planTitle"
      size="medium"
      @submit="createPlan"
    >
      <el-form label-width="150px">
        <el-form-item :label="copy.service"><el-input v-model="plan.serviceKey" /></el-form-item>
        <el-form-item :label="copy.module"><el-input v-model="plan.moduleKey" /></el-form-item>
        <el-form-item :label="copy.requestedVersion"
          ><el-input v-model="plan.requestedVersion"
        /></el-form-item>
      </el-form>
      <p v-if="formError" role="alert">{{ formError }}</p>
    </AppFormDrawer>
  </SystemDataAdminFrame>
</template>

<style scoped>
.systemdata-init-panel {
  display: grid;
  min-width: 0;
  gap: var(--ip-space-4);
}

.systemdata-init-panel > .el-button {
  justify-self: start;
}

.systemdata-init-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  min-width: 0;
  border-bottom: 1px solid var(--ip-color-border);
}

.systemdata-init-tabs button {
  min-height: 38px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  font: inherit;
  font-size: var(--ip-font-size-sm);
}

.systemdata-init-tabs button:hover,
.systemdata-init-tabs button[aria-selected='true'] {
  color: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.systemdata-init-tabs button:focus-visible,
.systemdata-init-plan-index button:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: -2px;
}

.systemdata-init-panel-heading,
.systemdata-init-evidence {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
}

.systemdata-init-panel h2,
.systemdata-init-panel h3 {
  margin: 0;
}

.systemdata-init-panel h2 {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-lg);
  line-height: var(--ip-line-height-tight);
}

.systemdata-init-hint,
.systemdata-init-impact {
  margin: 0;
  color: var(--ip-color-text-secondary);
}

.systemdata-init-seeds {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: var(--ip-space-3);
}

.systemdata-init-seed,
.systemdata-init-plan-detail,
.systemdata-init-operation-detail {
  display: grid;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.systemdata-init-facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--ip-space-3);
  margin: 0;
}

.systemdata-init-facts div {
  min-width: 0;
}

.systemdata-init-facts dt {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.systemdata-init-facts dd {
  margin: var(--ip-space-1) 0 0;
  overflow-wrap: anywhere;
  color: var(--ip-color-text-primary);
}

.systemdata-init-steps,
.systemdata-init-observations {
  display: grid;
  gap: var(--ip-space-2);
  margin: 0;
  padding-left: var(--ip-space-5);
}

.systemdata-init-gates {
  display: grid;
  gap: var(--ip-space-3);
  max-width: 720px;
}

.systemdata-init-plan-index {
  display: grid;
  gap: var(--ip-space-1);
  max-width: 720px;
  padding: var(--ip-space-2);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-init-plan-index button {
  min-width: 0;
  padding: var(--ip-space-2) var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  text-align: left;
  background: transparent;
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font: inherit;
  overflow-wrap: anywhere;
}

.systemdata-init-plan-index button:hover,
.systemdata-init-plan-index button[aria-pressed='true'] {
  color: var(--ip-color-primary);
  background: var(--ip-color-bg-container);
  border-color: var(--ip-color-border);
}

.systemdata-init-gates label {
  display: grid;
  gap: var(--ip-space-1);
}

@media (max-width: 720px) {
  .systemdata-init-tabs {
    overflow-x: auto;
    flex-wrap: nowrap;
  }

  .systemdata-init-tabs button {
    flex: 0 0 auto;
  }
}
</style>
