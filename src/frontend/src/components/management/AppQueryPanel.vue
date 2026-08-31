<script setup lang="ts">
/**
 * AppQueryPanel(PF-01 §7.10):查询区容器。可选标题、动作槽与折叠。
 */

import { computed, useId } from 'vue'
import type { QueryDescriptor } from '@/querying'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const props = withDefaults(
  defineProps<{
    title?: string
    collapsible?: boolean
    collapsed?: boolean
    showActions?: boolean
    grid?: boolean
    descriptor?: QueryDescriptor
    submitLabel?: string
    resetLabel?: string
    toggleLabel?: string
  }>(),
  {
    collapsible: false,
    collapsed: false,
    showActions: false,
    grid: false,
  },
)

const locale = usePlatformLocale()
const copy = computed(() => ({
  ...localeMessages[locale.value].common.query,
  submit: props.submitLabel ?? localeMessages[locale.value].common.query.submit,
  reset: props.resetLabel ?? localeMessages[locale.value].common.query.reset,
}))

const emit = defineEmits<{
  'update:collapsed': [value: boolean]
  submit: [descriptor?: QueryDescriptor]
  reset: []
}>()

const titleId = useId()
const bodyId = useId()
</script>

<template>
  <section class="app-query-panel">
    <header
      v-if="title || collapsible || $slots.actions || (showActions && !grid)"
      class="app-query-panel__header"
    >
      <h2 v-if="title" :id="titleId" class="app-query-panel__title">{{ title }}</h2>
      <div v-if="$slots.actions" class="app-query-panel__actions">
        <slot name="actions" />
      </div>
      <div v-if="showActions && !grid" class="app-query-panel__actions">
        <button type="button" data-testid="query-panel-reset" @click="emit('reset')">
          {{ copy.reset }}
        </button>
        <button
          type="button"
          data-testid="query-panel-submit"
          @click="emit('submit', descriptor)"
        >
          {{ copy.submit }}
        </button>
      </div>
      <button
        v-if="collapsible"
        type="button"
        class="app-query-panel__toggle"
        data-testid="query-panel-toggle"
        :aria-expanded="!collapsed"
        :aria-controls="bodyId"
        @click="emit('update:collapsed', !collapsed)"
      >
        {{ collapsed ? copy.expand : (toggleLabel ?? copy.collapse) }}
      </button>
    </header>
    <div
      v-show="!collapsed"
      :id="bodyId"
      class="app-query-panel__body"
      :class="{ 'app-query-panel__body--grid': grid }"
    >
      <slot />
      <div
        v-if="(showActions && grid) || $slots['body-actions']"
        class="app-query-panel__actions app-query-panel__body-actions"
      >
        <button
          v-if="showActions && grid"
          type="button"
          class="app-query-panel__submit"
          data-testid="query-panel-submit"
          @click="emit('submit', descriptor)"
        >
          {{ copy.submit }}
        </button>
        <button
          v-if="showActions && grid"
          type="button"
          class="app-query-panel__reset"
          data-testid="query-panel-reset"
          @click="emit('reset')"
        >
          {{ copy.reset }}
        </button>
        <slot name="body-actions" />
      </div>
    </div>
  </section>
</template>

<style scoped>
.app-query-panel {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}

.app-query-panel__header {
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
}

.app-query-panel__title {
  flex: 1 1 auto;
  margin: 0;
  font-size: var(--ip-font-size-md);
  font-weight: 600;
  color: var(--ip-color-text-primary);
}

.app-query-panel__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
}

.app-query-panel__toggle {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  background: transparent;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
  cursor: pointer;
  font-size: var(--ip-font-size-sm);
}

.app-query-panel__toggle:hover {
  color: var(--ip-color-text-primary);
  border-color: var(--ip-color-border-strong);
}

.app-query-panel__toggle:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.app-query-panel__body {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: var(--ip-space-3);
}

.app-query-panel__body--grid {
  display: flex;
  flex-direction: row;
  flex-wrap: wrap;
  align-items: end;
  gap: var(--ip-density-control-gap);
}

.app-query-panel__body-actions {
  align-self: flex-end;
}

.app-query-panel__actions button {
  box-sizing: border-box;
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font-family: inherit;
  font-size: var(--ip-font-size-sm);
  line-height: 1.2;
}

.app-query-panel__actions .app-query-panel__submit {
  color: var(--ip-color-text-inverse, #fff);
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}
</style>
