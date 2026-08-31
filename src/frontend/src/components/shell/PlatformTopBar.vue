<script setup lang="ts">
/**
 * PlatformTopBar(PF-01 §7.8/§6.1):PC 品牌顶栏,高度固定 56px,全宽覆盖工具轨、
 * 功能树与内容区。提供 brand / context / global-search / global-actions / user 具名槽,
 * 空槽不渲染占位按钮。顶栏文字与图标使用 --ip-shell-topbar-text(白),渐变由
 * --ip-shell-topbar-background 提供(三配色各自映射,§7.5)。
 */

defineSlots<{
  brand?: () => unknown
  context?: () => unknown
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

    <div v-if="$slots.context" class="ip-topbar__context">
      <slot name="context" />
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
  gap: 20px;
  height: var(--ip-shell-topbar-height);
  padding: 0 4px 0 14px;
  overflow: visible;
  background: var(--ip-shell-topbar-background);
  color: var(--ip-shell-topbar-text);
}

.ip-topbar__brand {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 36vw;
}

.ip-topbar__search {
  position: absolute;
  top: 50%;
  left: 50%;
  margin-top: -16px;
  display: inline-flex;
  min-width: 0;
  margin-left: -150px;
  width: min(100%, 300px);
  max-width: 300px;
  justify-content: center;
}

.ip-topbar__context {
  display: inline-flex;
  min-width: 0;
  max-width: 22vw;
  white-space: nowrap;
}

.ip-topbar__right {
  display: inline-flex;
  flex: 0 1 auto;
  align-items: center;
  gap: 4px;
  min-width: 0;
  width: auto;
  max-width: 100%;
  overflow: hidden;
  margin-left: auto;
  margin-right: 0;
}

.ip-topbar__actions {
  display: inline-flex;
  flex: 0 1 auto;
  align-items: center;
  gap: 4px;
  min-width: 0;
  margin-left: auto;
  overflow: hidden;
}

.ip-topbar__user {
  display: inline-flex;
  flex: 0 1 190px;
  align-items: center;
  width: 190px;
  max-width: 190px;
  min-width: 146px;
  overflow: hidden;
}

.ip-topbar__user > * {
  display: block;
  min-width: 0;
  width: 100%;
  max-width: 100%;
}

@media (max-width: 1280px) {
  .ip-topbar {
    gap: 12px;
    padding: 0 4px 0 14px;
  }

  .ip-topbar__right {
    gap: 4px;
  }

  .ip-topbar__actions {
    gap: 3px;
    max-width: calc(100% - 150px);
  }

  .ip-topbar__search {
    margin-left: -80px;
    max-width: 160px;
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
