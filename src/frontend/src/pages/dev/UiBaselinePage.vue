<script setup lang="ts">
/**
 * DEV-only UI 视觉基线页(PF-01 §12.3):
 * 仅注册于 `import.meta.env.DEV`,生产构建不含此路由与导航入口。
 * 用通用组件展示真实静态控件状态(查询/树表/表单抽屉/Loading/Empty/Error/
 * Permission/Degraded),不使用业务名称、假 KPI 或假服务响应,供主题/密度
 * 视觉矩阵与键盘、缩放、无横向滚动验收截图。
 */

import { ref } from 'vue'

import AppDegradedState from '@/components/base/AppDegradedState.vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppErrorAlert from '@/components/base/AppErrorAlert.vue'
import AppLoadingState from '@/components/base/AppLoadingState.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppPermissionState from '@/components/base/AppPermissionState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppTreeTableLayout from '@/components/management/AppTreeTableLayout.vue'

const drawerOpen = ref(false)

/** 静态样例数据:仅用于展示控件状态,不含生产业务名与数值。 */
const TREE = [
  { group: '分组甲', items: ['条目甲一', '条目甲二'] },
  { group: '分组乙', items: ['条目乙一', '条目乙二', '条目乙三'] },
] as const

const TABLE_ROWS = [
  { code: 'SAMPLE-0001', name: '样例记录一', status: '启用' },
  { code: 'SAMPLE-0002', name: '样例记录二', status: '停用' },
  { code: 'SAMPLE-0003', name: '样例记录三', status: '启用' },
] as const
</script>

<template>
  <AppPage
    data-testid="ui-baseline"
    title="UI 基线"
    description="DEV 专用视觉基线页:展示通用组件与页面状态的真实静态控件,不渲染业务数据或假 KPI。"
  >
    <!-- 查询区 -->
    <AppQueryPanel data-testid="baseline-query" title="查询区" collapsible :collapsed="false">
      <form class="ip-baseline__query-form" @submit.prevent>
        <div class="ip-baseline__field">
          <label for="baseline-keyword">关键字</label>
          <input
            id="baseline-keyword"
            class="ip-baseline__input"
            type="text"
            placeholder="输入关键字"
          />
        </div>
        <div class="ip-baseline__field">
          <label for="baseline-status">状态</label>
          <select id="baseline-status" class="ip-baseline__input">
            <option value="">全部</option>
            <option value="enabled">启用</option>
            <option value="disabled">停用</option>
          </select>
        </div>
        <div class="ip-baseline__actions">
          <button type="submit" class="ip-baseline__btn ip-baseline__btn--primary">查询</button>
          <button type="reset" class="ip-baseline__btn">重置</button>
        </div>
      </form>
    </AppQueryPanel>

    <!-- 树 + 表两栏布局 -->
    <AppTreeTableLayout
      data-testid="baseline-tree-table"
      tree-label="功能树"
      content-label="数据列表"
      tree-width="narrow"
    >
      <template #tree>
        <nav class="ip-baseline__tree" aria-label="静态功能树">
          <div v-for="node in TREE" :key="node.group" class="ip-baseline__tree-group">
            <p class="ip-baseline__tree-title">{{ node.group }}</p>
            <ul class="ip-baseline__tree-list">
              <li v-for="item in node.items" :key="item" class="ip-baseline__tree-item">
                {{ item }}
              </li>
            </ul>
          </div>
        </nav>
      </template>

      <template #toolbar>
        <button type="button" class="ip-baseline__btn ip-baseline__btn--primary">新增</button>
        <button type="button" class="ip-baseline__btn">刷新</button>
      </template>

      <div class="ip-baseline__table-wrap">
        <table class="ip-baseline__table">
          <thead>
            <tr>
              <th scope="col">编号</th>
              <th scope="col">名称</th>
              <th scope="col">状态</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in TABLE_ROWS" :key="row.code">
              <td>{{ row.code }}</td>
              <td>{{ row.name }}</td>
              <td>{{ row.status }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <template #pagination>
        <button type="button" class="ip-baseline__btn" disabled>上一页</button>
        <span class="ip-baseline__page-hint">第 1 / 1 页</span>
        <button type="button" class="ip-baseline__btn" disabled>下一页</button>
      </template>
    </AppTreeTableLayout>

    <!-- 表单抽屉触发 -->
    <div class="ip-baseline__section">
      <button
        type="button"
        class="ip-baseline__btn ip-baseline__btn--primary"
        data-testid="baseline-drawer-trigger"
        @click="drawerOpen = true"
      >
        打开表单抽屉
      </button>
    </div>

    <!-- 页面状态组件 -->
    <section class="ip-baseline__states" data-testid="baseline-states" aria-label="页面状态组件">
      <AppLoadingState label="正在加载" />
      <AppEmptyState title="暂无数据" description="当前筛选条件下没有可展示的记录。" />
      <AppErrorAlert
        title="加载失败"
        message="数据获取失败,请稍后重试。"
        trace-id="dev-baseline-trace"
      />
      <AppPermissionState />
      <AppDegradedState
        :unavailable="['暂不可用能力一', '暂不可用能力二']"
        :available="['仍可使用能力一', '仍可使用能力二']"
      />
    </section>

    <!-- 表单抽屉(静态样例) -->
    <AppFormDrawer v-model="drawerOpen" title="样例表单" size="narrow">
      <div class="ip-baseline__drawer-form">
        <div class="ip-baseline__field">
          <label for="baseline-drawer-name">名称</label>
          <input
            id="baseline-drawer-name"
            class="ip-baseline__input"
            type="text"
            placeholder="输入名称"
          />
        </div>
        <div class="ip-baseline__field">
          <label for="baseline-drawer-desc">说明</label>
          <textarea
            id="baseline-drawer-desc"
            class="ip-baseline__input ip-baseline__input--textarea"
            placeholder="输入说明"
          />
        </div>
      </div>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.ip-baseline {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-6);
}

.ip-baseline__query-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--ip-space-4);
}

.ip-baseline__field {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.ip-baseline__field label {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.ip-baseline__input {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.ip-baseline__input--textarea {
  min-height: 88px;
  padding: var(--ip-space-2) var(--ip-space-3);
  resize: vertical;
}

.ip-baseline__actions {
  display: flex;
  gap: var(--ip-space-2);
}

.ip-baseline__btn {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-primary);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.ip-baseline__btn:hover {
  border-color: var(--ip-color-border-strong);
  background: var(--ip-color-bg-muted);
}

.ip-baseline__btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.ip-baseline__btn--primary {
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
  color: var(--ip-color-on-primary);
}

.ip-baseline__btn--primary:hover {
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.ip-baseline__btn:focus-visible,
.ip-baseline__input:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.ip-baseline__tree {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
}

.ip-baseline__tree-title {
  margin: 0 0 var(--ip-space-1);
  font-size: var(--ip-font-size-sm);
  font-weight: 600;
  color: var(--ip-color-text-primary);
}

.ip-baseline__tree-list {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
  margin: 0;
  padding-left: var(--ip-space-4);
}

.ip-baseline__tree-item {
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-secondary);
}

.ip-baseline__table-wrap {
  overflow-x: auto;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.ip-baseline__table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--ip-font-size-md);
}

.ip-baseline__table th,
.ip-baseline__table td {
  padding: var(--ip-space-2) var(--ip-space-3);
  text-align: left;
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-baseline__table th {
  color: var(--ip-color-text-secondary);
  font-weight: 600;
}

.ip-baseline__table td {
  color: var(--ip-color-text-primary);
}

.ip-baseline__page-hint {
  display: inline-flex;
  align-items: center;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.ip-baseline__section {
  display: flex;
  gap: var(--ip-space-3);
}

.ip-baseline__states {
  display: grid;
  gap: var(--ip-space-4);
}

.ip-baseline__drawer-form {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}
</style>
