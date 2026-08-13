<script setup lang="ts">
/**
 * AppTreeTableLayout(PF-01 §7.10):树 + 内容区两栏布局。
 * slots:tree / toolbar / default / pagination。
 */

withDefaults(
  defineProps<{
    treeLabel: string
    contentLabel: string
    treeWidth?: 'narrow' | 'medium'
  }>(),
  { treeWidth: 'medium' },
)
</script>

<template>
  <div class="app-tree-table">
    <aside
      class="app-tree-table__tree"
      :class="`app-tree-table__tree--${treeWidth}`"
      :aria-label="treeLabel"
    >
      <slot name="tree" />
    </aside>
    <section class="app-tree-table__content" :aria-label="contentLabel">
      <div v-if="$slots.toolbar" class="app-tree-table__toolbar">
        <slot name="toolbar" />
      </div>
      <div class="app-tree-table__body">
        <slot />
      </div>
      <div v-if="$slots.pagination" class="app-tree-table__pagination">
        <slot name="pagination" />
      </div>
    </section>
  </div>
</template>

<style scoped>
.app-tree-table {
  display: flex;
  flex: 1 1 auto;
  align-items: stretch;
  gap: var(--ip-space-4);
  min-height: 0;
  width: 100%;
}

.app-tree-table__tree {
  flex: 0 0 auto;
  overflow-y: auto;
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.app-tree-table__tree--narrow {
  width: 240px;
}

.app-tree-table__tree--medium {
  width: 320px;
}

.app-tree-table__content {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: var(--ip-space-3);
  min-width: 0;
}

.app-tree-table__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
}

.app-tree-table__body {
  flex: 1 1 auto;
  min-height: 0;
}

.app-tree-table__pagination {
  display: flex;
  justify-content: flex-end;
}
</style>
