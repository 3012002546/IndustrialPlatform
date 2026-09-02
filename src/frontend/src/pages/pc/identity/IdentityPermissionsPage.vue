<script setup lang="ts">
/**
 * 权限目录页(TASK-ID-012,§16.3):只读展示权限目录树(identity.permission.view)。
 * 权限类型标注:Page=页面 / Action=操作(后端 PermissionType 枚举名)。
 */
import { computed, onMounted, ref } from 'vue'

import type { PermissionTreeNodeDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

import { reportManagementError } from './shared'

const management = getManagementApi()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.permissions)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

const loading = ref(false)
const tree = ref<PermissionTreeNodeDto[]>([])
const rawTree = ref<PermissionTreeNodeDto[]>([])
const expandAll = ref(true)

/** 计数统计:总数 / 页面 / 操作。 */
const stats = ref({ total: 0, pages: 0, actions: 0 })

/** 按关键字过滤(名称 / NId),命中祖先链保留。 */
function filterTree(nodes: PermissionTreeNodeDto[], keyword: string): PermissionTreeNodeDto[] {
  if (keyword.length === 0) return nodes
  return nodes
    .map((node) => {
      const children = filterTree(node.children, keyword)
      const selfHit = node.name.includes(keyword) || node.permissionNId.includes(keyword)
      return selfHit || children.length > 0 ? { ...node, children } : null
    })
    .filter((node): node is PermissionTreeNodeDto => node !== null)
}

function countNodes(nodes: PermissionTreeNodeDto[]): void {
  let total = 0
  let pages = 0
  let actions = 0
  const walk = (list: PermissionTreeNodeDto[]): void => {
    for (const node of list) {
      total += 1
      if (node.type === 'Page') pages += 1
      else if (node.type === 'Action') actions += 1
      walk(node.children)
    }
  }
  walk(nodes)
  stats.value = { total, pages, actions }
}

function applyFilter(keyword: string): void {
  tree.value = filterTree(rawTree.value, keyword.trim())
}

async function loadTree(): Promise<void> {
  loading.value = true
  try {
    rawTree.value = await management.getPermissionTree()
    tree.value = rawTree.value
    countNodes(rawTree.value)
  } catch (error) {
    reportManagementError(error, '加载权限目录失败')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void loadTree()
})
</script>

<template>
  <AppPage
    class="permissions-page"
    data-testid="identity-permissions-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="permissions-page__stats">
        {{ stats.total }} {{ copy.countSuffix }} · {{ copy.page }} {{ stats.pages }} ·
        {{ copy.operation }} {{ stats.actions }}
      </span>
    </template>

    <AppQueryPanel class="permissions-page__query-panel" :title="commonCopy.queryTitle">
      <el-input
        :placeholder="copy.filter"
        :aria-label="copy.filter"
        clearable
        class="permissions-page__filter"
        @input="applyFilter"
        @clear="applyFilter('')"
      />
      <template #actions>
        <el-button @click="expandAll = !expandAll">{{
          expandAll ? commonCopy.collapseAll : commonCopy.expandAll
        }}</el-button>
        <el-button @click="loadTree">{{ commonCopy.refresh }}</el-button>
      </template>
    </AppQueryPanel>

    <div class="permissions-page__tree" v-loading="loading">
      <el-tree
        :data="tree"
        row-key="permissionNId"
        :default-expand-all="expandAll"
        :expand-on-click-node="false"
        node-key="permissionNId"
        :props="{ label: 'name', children: 'children' }"
      >
        <template #default="{ data }">
          <div class="permissions-page__node">
            <span class="permissions-page__node-name">{{ data.name }}</span>
            <el-tag
              size="small"
              :type="data.type === 'Page' ? 'primary' : 'success'"
              effect="plain"
            >
              {{ data.type === 'Page' ? copy.page : copy.operation }}
            </el-tag>
            <span class="permissions-page__node-nid">{{ data.permissionNId }}</span>
            <span v-if="data.description" class="permissions-page__node-desc">{{
              data.description
            }}</span>
          </div>
        </template>
      </el-tree>
    </div>
  </AppPage>
</template>

<style scoped>
.permissions-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.permissions-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.permissions-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.permissions-page :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  border-bottom: 1px solid var(--ip-color-border);
}

.permissions-page :deep(.app-query-panel__header) {
  justify-content: space-between;
  margin-bottom: var(--ip-space-3);
}

.permissions-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.permissions-page__filter {
  width: 260px;
}

.permissions-page__stats {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.permissions-page__spacer {
  flex: 1;
}

.permissions-page__tree {
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  padding: var(--ip-space-3);
  overflow: auto;
  max-height: calc(100vh - 260px);
}

.permissions-page__node {
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
  min-width: 0;
}

.permissions-page__node-name {
  font-weight: 500;
  color: var(--ip-color-text-primary);
}

.permissions-page__node-nid {
  font-family: var(--ip-font-mono);
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.permissions-page__node-desc {
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-tertiary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
