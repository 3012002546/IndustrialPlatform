<script setup lang="ts">
/**
 * 权限目录页(TASK-ID-012,§16.3):只读展示权限目录树(identity.permission.view)。
 * 权限类型标注:Page=页面 / Action=操作(后端 PermissionType 枚举名)。
 */
import { onMounted, ref } from 'vue'

import type { PermissionTreeNodeDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'

import { reportManagementError } from './shared'

const management = getManagementApi()

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
  <section class="permissions-page">
    <div class="permissions-page__toolbar">
      <el-input
        placeholder="按名称 / 业务标识过滤"
        clearable
        class="permissions-page__filter"
        @input="applyFilter"
        @clear="applyFilter('')"
      />
      <span class="permissions-page__stats">
        共 {{ stats.total }} 项 · 页面 {{ stats.pages }} · 操作 {{ stats.actions }}
      </span>
      <div class="permissions-page__spacer" />
      <el-button @click="expandAll = !expandAll">{{
        expandAll ? '折叠全部' : '展开全部'
      }}</el-button>
      <el-button @click="loadTree">刷新</el-button>
    </div>

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
              {{ data.type === 'Page' ? '页面' : '操作' }}
            </el-tag>
            <span class="permissions-page__node-nid">{{ data.permissionNId }}</span>
            <span v-if="data.description" class="permissions-page__node-desc">{{
              data.description
            }}</span>
          </div>
        </template>
      </el-tree>
    </div>
  </section>
</template>

<style scoped>
.permissions-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
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
