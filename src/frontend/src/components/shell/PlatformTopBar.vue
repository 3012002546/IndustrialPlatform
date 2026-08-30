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

    <div class="ip-topbar__right">
      <div class="ip-topbar__actions">
        <slot name="global-actions" />
      </div>

      <div class="ip-topbar__user">
        <slot name="user" />
      </div>
    </div>
  </header>
</template>

<style scoped>
.ip-topbar {
  position: relative;
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 4px;
  height: var(--ip-shell-topbar-height);
  padding: 0 4px;
  overflow: visible;
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
  flex: 1 1 160px;
  min-width: 0;
  justify-content: center;
}

.ip-topbar__right {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-3);
  min-width: 0;
  margin-left: auto;
  margin-right: 0;
}

.ip-topbar__actions {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-2);
}

.ip-topbar__user {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  width: clamp(120px, 14vw, 220px);
  min-width: 120px;
  overflow: hidden;
}

.ip-topbar__user > * {
  min-width: 0;
  max-width: 100%;
}

@media (max-width: 1280px) {
  .ip-topbar {
    gap: 4px;
    padding: 0 4px;
  }

  .ip-topbar__right {
    gap: var(--ip-space-2);
  }

  .ip-topbar__actions {
    gap: var(--ip-space-1);
  }

  /* Mock 仅是开发提示,保留 status 文本在无障碍树中,视觉上收为状态点。 */
  .ip-topbar__actions :deep(.mock-mode-banner) {
    box-sizing: border-box;
    width: 24px;
    min-width: 24px;
    max-width: 24px;
    padding: var(--ip-space-1);
    overflow: hidden;
  }

  .ip-topbar__actions :deep(.mock-mode-banner__dot) {
    flex: 0 0 auto;
  }

  .ip-topbar__actions :deep(.mock-mode-banner > span:last-child) {
    overflow: hidden;
    white-space: nowrap;
  }
}
</style>
