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
      v-if="title || collapsible || $slots.actions || showActions"
      class="app-query-panel__header"
    >
      <h2 v-if="title" :id="titleId" class="app-query-panel__title">{{ title }}</h2>
      <div v-if="$slots.actions" class="app-query-panel__actions">
        <slot name="actions" />
      </div>
      <div v-if="showActions" class="app-query-panel__actions">
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
        {{ collapsed ? copy.expand : copy.collapse }}
      </button>
    </header>
    <div
      v-show="!collapsed"
      :id="bodyId"
      class="app-query-panel__body"
      :class="{ 'app-query-panel__body--grid': grid }"
    >
      <slot />
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
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
  align-items: end;
}
</style>
