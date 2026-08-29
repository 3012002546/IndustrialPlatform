<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import type { NavigationNodeDto } from '@/api/systemData/managementTypes'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '导航与资源发布',
    description: '编辑导航草稿并按终端预览；保存后先验证权限回执，再发布，409 时重新读取草稿。',
    permission: PERMISSIONS.systemDataNavigationView,
  },
)
const store = useSystemDataManagementStore()
const terminal = ref('Pc')
const selected = ref<string | null>(null)
const draft = reactive({
  label: '',
  kind: 'Group',
  resourceNId: '',
  featureNId: '',
  iconKey: '',
  displayOrder: 0,
  visibleTerminals: ['Pc', 'Pda', 'Mobile'],
})
const nodes = computed(() => {
  const flatten = (items: readonly NavigationNodeDto[]): NavigationNodeDto[] =>
    items.flatMap((item) => [item, ...flatten(item.children ?? [])])
  return flatten(store.navigationDraft?.nodes ?? [])
})
function edit(node: NavigationNodeDto): void {
  selected.value = node.nodeNId
  Object.assign(draft, {
    label: node.label,
    kind: node.kind,
    resourceNId: node.resourceNId ?? '',
    featureNId: node.featureNId ?? '',
    iconKey: node.iconKey ?? '',
    displayOrder: node.displayOrder,
    visibleTerminals: [...node.visibleTerminals],
  })
}
async function save(): Promise<void> {
  const request = {
    label: draft.label.trim(),
    kind: draft.kind,
    displayOrder: draft.displayOrder,
    visibleTerminals: draft.visibleTerminals,
    ...(draft.resourceNId ? { resourceNId: draft.resourceNId } : {}),
    ...(draft.featureNId ? { featureNId: draft.featureNId } : {}),
    ...(draft.iconKey ? { iconKey: draft.iconKey } : {}),
  }
  if (selected.value) await store.updateNavigationNode(selected.value, request)
  else await store.addNavigationNode(request)
}
</script>
<template>
  <SystemDataAdminFrame
    kind="navigation"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
  >
    <template #toolbar
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage"
        ><button type="button" data-testid="systemdata-navigation-validate" @click="store.validateNavigation">
          验证草稿
        </button></PermissionGate
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationPublish"
        ><button
          type="button"
          data-testid="systemdata-navigation-publish"
          :disabled="store.loading"
          @click="store.publishNavigation"
        >
          发布
        </button></PermissionGate
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationRollback"
        ><button type="button" data-testid="systemdata-navigation-rollback" @click="store.rollbackNavigation">
          回滚 PreviousSnapshot
        </button></PermissionGate></template
    >
    <div class="systemdata-navigation-layout">
      <section>
        <h2>草稿树</h2>
        <ul>
          <li v-for="node in nodes" :key="node.nodeNId">
            <button type="button" @click="edit(node)">{{ node.label }}</button> · {{ node.kind }} ·
            {{ node.status
            }}<PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage"
              ><button type="button" @click="store.deleteNavigationNode(node.nodeNId)">
                停用节点
              </button></PermissionGate
            >
          </li>
        </ul>
        <AppEmptyState v-if="!nodes.length" title="暂无导航草稿" />
      </section>
      <section>
        <h2>节点属性</h2>
        <el-form label-width="100px"
          ><el-form-item label="Label"
            ><el-input v-model="draft.label" aria-label="导航 Label" /></el-form-item
          ><el-form-item label="Kind"
            ><el-select v-model="draft.kind"
              ><el-option label="Group" value="Group" /><el-option
                label="Link"
                value="Link" /></el-select></el-form-item
          ><el-form-item label="Resource"
            ><el-select v-model="draft.resourceNId" clearable
              ><el-option
                v-for="item in store.resources.filter(
                  (resource) => resource.status === 'Active' && resource.type === 'Page',
                )"
                :key="item.resourceNId"
                :label="item.name + '（' + item.resourceNId + '）'"
                :value="item.resourceNId" /></el-select></el-form-item
          ><el-form-item label="Feature"
            ><el-select v-model="draft.featureNId" clearable
              ><el-option
                v-for="item in store.features"
                :key="item.featureNId"
                :label="item.name + '（' + item.featureNId + '）'"
                :value="item.featureNId" /></el-select></el-form-item
          ><el-form-item label="图标 Key"><el-input v-model="draft.iconKey" /></el-form-item
          ><el-form-item label="支持终端"
            ><el-checkbox-group v-model="draft.visibleTerminals"
              ><el-checkbox label="Pc" /><el-checkbox label="Pda" /><el-checkbox
                label="Mobile" /></el-checkbox-group></el-form-item></el-form
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage"
          ><button type="button" data-testid="systemdata-navigation-save" @click="save">
            保存草稿节点
          </button></PermissionGate
        >
      </section>
      <section>
        <h2>运行预览</h2>
        <el-select v-model="terminal"
          ><el-option label="Pc" value="Pc" /><el-option label="Pda" value="Pda" /><el-option
            label="Mobile"
            value="Mobile"
        /></el-select>
        <ul>
          <li
            v-for="node in nodes.filter((item) => item.visibleTerminals.includes(terminal))"
            :key="node.nodeNId"
          >
            {{ node.label }} · {{ node.kind }}
          </li>
        </ul>
        <p>预览使用真实终端可见性；发布前必须验证权限回执。</p>
      </section>
    </div>
  </SystemDataAdminFrame>
</template>
