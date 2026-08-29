<script setup lang="ts">
import { reactive, ref, computed } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '服务初始化编排',
    description:
      '按注册信息、SeedSets、计划、审批/备份和 Operation 结果管理服务初始化；ServiceKey、ModuleKey、Checksum 来自服务发布物。',
    permission: PERMISSIONS.systemDataServiceInitializationView,
  },
)
const store = useSystemDataManagementStore()
const tab = ref('registrations')
const selectedPlanNId = ref('')
const reason = ref('')
const backup = ref('')
const registration = reactive({
  serviceKey: '',
  moduleKey: '',
  requestedVersion: '',
  logicalDatabaseName: '',
  artifactChecksum: '',
})
const plan = reactive({
  serviceKey: '',
  moduleKey: '',
  requestedVersion: '',
  desiredState: 'SourceOfTruth',
})
const selectedPlan = computed(
  () =>
    store.initializationPlans?.items.find((item) => item.planNId === selectedPlanNId.value) ?? null,
)
const REGISTRATION_COLUMNS = [
  { field: 'serviceKey', title: 'ServiceKey', minWidth: 160 },
  { field: 'moduleKey', title: 'ModuleKey', minWidth: 160 },
  { field: 'status', title: '状态/期望状态', minWidth: 180, filter: { kind: 'text' as const } },
  { field: 'migrationVersion', title: '迁移版本', width: 120, filter: { kind: 'text' as const } },
]
const OPERATION_COLUMNS = [
  { field: 'operationNId', title: 'OperationNId', minWidth: 180 },
  { field: 'status', title: '状态/阶段', width: 150, filter: { kind: 'text' as const } },
  { field: 'steps', title: '步骤', minWidth: 260, filter: { kind: 'text' as const } },
]
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
async function register(): Promise<void> {
  if (Object.values(registration).some((value) => !value)) return
  await store.registerInitialization({ ...registration, manifestVersion: '1' })
}
async function createPlan(): Promise<void> {
  if (plan.serviceKey && plan.moduleKey && plan.requestedVersion)
    await store.createInitializationPlan(plan)
}
async function apply(): Promise<void> {
  if (selectedPlan.value?.requestedMigrationVersion)
    await store.applyInitialization({
      planNId: selectedPlan.value.planNId,
      moduleKey: selectedPlan.value.moduleKey,
      requestedVersion: selectedPlan.value.requestedMigrationVersion,
    })
}
</script>
<template>
  <SystemDataAdminFrame
    kind="service-initialization"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
    ><div class="systemdata-init-tabs">
      <button
        v-for="item in [
          { id: 'registrations', label: '注册信息' },
          { id: 'seedsets', label: 'SeedSets' },
          { id: 'plans', label: '计划' },
          { id: 'operations', label: 'Operation' },
          { id: 'environment', label: '环境策略' },
        ]"
        :key="item.id"
        type="button"
        :aria-pressed="tab === item.id"
        @click="tab = item.id"
      >
        {{ item.label }}
      </button>
    </div>
    <section v-if="tab === 'registrations'">
      <h2>服务/模块注册</h2>
      <el-form label-width="150px"
        ><el-form-item label="ServiceKey"
          ><el-input
            v-model="registration.serviceKey"
            placeholder="服务发布物提供的稳定键" /></el-form-item
        ><el-form-item label="ModuleKey"
          ><el-input
            v-model="registration.moduleKey"
            placeholder="模块 manifest 提供的键" /></el-form-item
        ><el-form-item label="RequestedVersion"
          ><el-input v-model="registration.requestedVersion" /></el-form-item
        ><el-form-item label="LogicalDatabaseName"
          ><el-input v-model="registration.logicalDatabaseName" /></el-form-item
        ><el-form-item label="ArtifactChecksum"
          ><el-input
            v-model="registration.artifactChecksum"
            placeholder="SHA-256，来源于发布物" /></el-form-item></el-form
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationRegister"
        ><button type="button" @click="register">注册/重注册</button></PermissionGate
      >
      <AppDataTable
        v-if="store.initializationRegistrations?.items.length"
        table-key="systemdata-initialization-registrations"
        route-key="systemdata-service-initialization"
        row-key="moduleKey"
        :rows="store.initializationRegistrations.items"
        :total="store.initializationRegistrations.total"
        :columns="REGISTRATION_COLUMNS"
        :exporter="exportRegistrations"
      >
        <template #cell-status="{ row }">{{ row.status }} / {{ row.desiredState }}</template>
      </AppDataTable>
      <AppEmptyState v-else title="暂无注册清单" />
    </section>
    <section v-else-if="tab === 'seedsets'">
      <h2>SeedSets</h2>
      <p>
        仅声明 SeedKey、Version、Scope、RequiredForReadiness 和
        AllowedEnvironments；不接受种子内容、SQL、路径或 SecretRef。
      </p>
      <AppEmptyState title="SeedSets 随服务模块注册清单读取" />
    </section>
    <section v-else-if="tab === 'plans'">
      <h2>计划、审批、备份</h2>
      <el-form label-width="150px"
        ><el-form-item label="ServiceKey"><el-input v-model="plan.serviceKey" /></el-form-item
        ><el-form-item label="ModuleKey"><el-input v-model="plan.moduleKey" /></el-form-item
        ><el-form-item label="RequestedVersion"
          ><el-input v-model="plan.requestedVersion" /></el-form-item></el-form
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationPlan"
        ><button type="button" @click="createPlan">生成计划</button></PermissionGate
      >
      <ul>
        <li v-for="item in store.initializationPlans?.items ?? []" :key="item.planNId">
          <button type="button" @click="selectedPlanNId = item.planNId">{{ item.planNId }}</button>
          · {{ item.serviceKey }}/{{ item.moduleKey }} · {{ item.planChecksum }}
        </li>
      </ul>
      <div v-if="selectedPlan">
        <el-input v-model="reason" placeholder="审批理由" /><PermissionGate
          :permission-n-id="PERMISSIONS.systemDataServiceInitializationApprove"
          ><button type="button" @click="store.createApproval(selectedPlan.planNId, reason)">
            登记审批
          </button></PermissionGate
        ><el-input v-model="backup" placeholder="脱敏备份证据引用" /><PermissionGate
          :permission-n-id="PERMISSIONS.systemDataServiceInitializationBackup"
          ><button type="button" @click="store.createBackupEvidence(selectedPlan.planNId, backup)">
            登记备份证据
          </button></PermissionGate
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationApply"
          ><button type="button" @click="apply">Apply（服务端门禁）</button></PermissionGate
        >
      </div>
      <AppEmptyState v-else title="请选择计划后查看审批/备份/apply 门禁" />
    </section>
    <section v-else-if="tab === 'operations'">
      <h2>Operation/结果</h2>
      <AppDataTable
        v-if="store.initializationOperations?.items.length"
        table-key="systemdata-initialization-operations"
        route-key="systemdata-service-initialization"
        row-key="operationNId"
        :rows="store.initializationOperations.items"
        :total="store.initializationOperations.total"
        :columns="OPERATION_COLUMNS"
        :exporter="exportOperations"
      >
        <template #cell-status="{ row }">{{ row.status }} / {{ row.phase }}</template>
        <template #cell-steps="{ row }">{{
          row.steps
            .map((step: { phase: string; status: string }) => step.phase + ':' + step.status)
            .join(' / ')
        }}</template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceInitializationCancel"
            ><button
              v-if="['Queued', 'Running'].includes(row.status)"
              type="button"
              @click="store.cancelInitialization(row.operationNId)"
            >取消编排</button></PermissionGate
          >
        </template>
      </AppDataTable>
      <AppEmptyState v-else title="暂无 Operation" />
      <p>生产执行顺序：plan → approval → backup → apply → verify；页面不展示连接信息。</p>
    </section>
    <section v-else>
      <h2>环境策略</h2>
      <p>生产执行顺序：plan → approval → backup → apply → verify；页面不展示连接信息。</p>
    </section></SystemDataAdminFrame
  >
</template>
