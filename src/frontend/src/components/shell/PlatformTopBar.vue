<script setup lang="ts">
/**
 * PlatformTopBar(PF-01 §7.8/§6.1):PC 品牌顶栏,高度固定 52px,全宽覆盖工具轨、
 * 功能树与内容区。提供 brand / global-search / global-actions / user 四个具名槽,
 * 空槽不渲染占位按钮。顶栏文字与图标使用 --ip-shell-topbar-text(白),渐变由
 * --ip-shell-topbar-background 提供(三配色各自映射,§7.5)。
 */

defineSlots<{
  brand?: () => unknown
  'global-search'?: () => unknown
  'global-actions'?: () => unknown
  user?: () => unknown
}>()
</script>

<template>
  <header class="ip-topbar">
    <div class="ip-topbar__brand">
      <slot name="brand" />
    </div>

    <div v-if="$slots['global-search']" class="ip-topbar__search">
      <slot name="global-search" />
    </div>

    <div class="ip-topbar__actions">
      <slot name="global-actions" />
    </div>

    <div class="ip-topbar__user">
      <slot name="user" />
    </div>
  </header>
</template>

<style scoped>
.ip-topbar {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-4);
  height: var(--ip-shell-topbar-height);
  padding: 0 var(--ip-space-4);
  background: var(--ip-shell-topbar-background);
  color: var(--ip-shell-topbar-text);
}

.ip-topbar__brand {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  min-width: 0;
}

.ip-topbar__search {
  display: inline-flex;
  flex: 1 1 auto;
  min-width: 0;
  justify-content: center;
}

.ip-topbar__actions {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-2);
  margin-left: auto;
}

.ip-topbar__user {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
}
</style>
