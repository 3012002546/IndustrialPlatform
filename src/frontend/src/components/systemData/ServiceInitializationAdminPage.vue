<script setup lang="ts">
import { reactive, ref, computed } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
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
      ><button type="button" @click="register">注册/重注册</button>
      <table v-if="store.initializationRegistrations?.items.length">
        <tbody>
          <tr
            v-for="item in store.initializationRegistrations.items"
            :key="item.serviceKey + item.moduleKey"
          >
            <td>{{ item.serviceKey }}</td>
            <td>{{ item.moduleKey }}</td>
            <td>{{ item.status }} / {{ item.desiredState }}</td>
            <td>{{ item.migrationVersion }}</td>
          </tr>
        </tbody>
      </table>
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
      ><button type="button" @click="createPlan">生成计划</button>
      <ul>
        <li v-for="item in store.initializationPlans?.items ?? []" :key="item.planNId">
          <button type="button" @click="selectedPlanNId = item.planNId">{{ item.planNId }}</button>
          · {{ item.serviceKey }}/{{ item.moduleKey }} · {{ item.planChecksum }}
        </li>
      </ul>
      <div v-if="selectedPlan">
        <el-input v-model="reason" placeholder="审批理由" /><button
          type="button"
          @click="store.createApproval(selectedPlan.planNId, reason)"
        >
          登记审批</button
        ><el-input v-model="backup" placeholder="脱敏备份证据引用" /><button
          type="button"
          @click="store.createBackupEvidence(selectedPlan.planNId, backup)"
        >
          登记备份证据</button
        ><button type="button" @click="apply">Apply（服务端门禁）</button>
      </div>
      <AppEmptyState v-else title="请选择计划后查看审批/备份/apply 门禁" />
    </section>
    <section v-else-if="tab === 'operations'">
      <h2>Operation/结果</h2>
      <table v-if="store.initializationOperations?.items.length">
        <tbody>
          <tr v-for="item in store.initializationOperations.items" :key="item.operationNId">
            <td>{{ item.operationNId }}</td>
            <td>{{ item.status }} / {{ item.phase }}</td>
            <td>{{ item.steps.map((step) => step.phase + ':' + step.status).join(' / ') }}</td>
            <td>
              <button
                v-if="['Queued', 'Running'].includes(item.status)"
                type="button"
                @click="store.cancelInitialization(item.operationNId)"
              >
                取消编排
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <AppEmptyState v-else title="暂无 Operation" />
      <p>生产执行顺序：plan → approval → backup → apply → verify；页面不展示连接信息。</p>
    </section>
    <section v-else>
      <h2>环境策略</h2>
      <p>生产执行顺序：plan → approval → backup → apply → verify；页面不展示连接信息。</p>
    </section></SystemDataAdminFrame
  >
</template>
