<script setup lang="ts">
/**
 * WorkspaceTabLimitDialog(PF-01 §7.9/§10.1):业务标签达 12 上限的决议对话框。
 * 由 WorkspaceTabsStore.pending 驱动:非空即展示,选择复用/关闭后打开/取消并 emit 决议。
 * ElDialog 自带焦点圈闭与 Esc 关闭;Esc/遮罩/× 均按取消处理。
 */

import { computed, ref, watch } from 'vue'
import { ElButton, ElDialog, ElRadio, ElRadioGroup } from 'element-plus'

import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import type { TabLimitResolution } from '@/workspace'

const emit = defineEmits<{
  resolve: [resolution: TabLimitResolution]
}>()

const tabsStore = useWorkspaceTabsStore()

/** 选中待决议的现有业务标签(默认第一个)。 */
const selectedId = ref('')

watch(
  () => tabsStore.pending,
  (pending) => {
    if (pending !== null) {
      selectedId.value = tabsStore.businessTabs[0]?.id ?? ''
    }
  },
  { immediate: true },
)

/** pending 非空时可见;任何关闭路径统一按取消决议处理。 */
const visible = computed({
  get: () => tabsStore.pending !== null,
  set: (open: boolean) => {
    if (!open) emit('resolve', { action: 'cancel' })
  },
})

const hasSelection = computed(() => tabsStore.businessTabs.some((t) => t.id === selectedId.value))

function reuseSelected(): void {
  emit('resolve', { action: 'reuse', tabId: selectedId.value })
}

function closeAndOpen(): void {
  emit('resolve', { action: 'close-and-open', tabId: selectedId.value })
}
</script>

<template>
  <ElDialog
    :model-value="visible"
    class="ip-tab-limit-dialog"
    title="业务标签已达上限"
    width="420px"
    :close-on-click-modal="true"
    :close-on-press-escape="true"
    @update:model-value="
      (open: boolean) => {
        visible = open
      }
    "
  >
    <p class="ip-tab-limit-dialog__hint">
      同时打开的业务标签已达 12 个上限。请选择复用一个现有标签,或关闭一个标签后打开新页面。
    </p>
    <ElRadioGroup v-model="selectedId" class="ip-tab-limit-dialog__list">
      <ElRadio
        v-for="tab in tabsStore.businessTabs"
        :key="tab.id"
        :value="tab.id"
        class="ip-tab-limit-dialog__option"
      >
        {{ tab.title }}
      </ElRadio>
    </ElRadioGroup>
    <template #footer>
      <ElButton @click="visible = false">取消</ElButton>
      <ElButton type="primary" :disabled="!hasSelection" @click="reuseSelected">
        复用选中标签
      </ElButton>
      <ElButton type="warning" :disabled="!hasSelection" @click="closeAndOpen">
        关闭选中后打开
      </ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.ip-tab-limit-dialog__hint {
  margin: 0 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-md);
  line-height: 1.6;
}

.ip-tab-limit-dialog__list {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.ip-tab-limit-dialog__option {
  max-width: 100%;
}
</style>
