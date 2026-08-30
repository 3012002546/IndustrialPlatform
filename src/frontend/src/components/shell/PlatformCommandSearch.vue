<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

export interface PlatformCommandItem {
  id: string
  label: string
  kind: 'navigation' | 'recent' | 'command'
}

const props = defineProps<{ items: readonly PlatformCommandItem[] }>()
const emit = defineEmits<{ select: [id: string] }>()
const query = ref('')
const open = ref(false)
const input = ref<HTMLInputElement | null>(null)
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.commandSearch)

const results = computed(() => {
  const value = query.value.trim().toLocaleLowerCase()
  if (value === '') return props.items.slice(0, 8)
  return props.items.filter((item) => item.label.toLocaleLowerCase().includes(value)).slice(0, 8)
})

function focusSearch(): void {
  open.value = true
  void input.value?.focus()
}

function onGlobalKeydown(event: KeyboardEvent): void {
  if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === 'k') {
    event.preventDefault()
    focusSearch()
  }
}

function close(): void {
  open.value = false
  query.value = ''
}

function selectItem(item: PlatformCommandItem): void {
  emit('select', item.id)
  close()
}

onMounted(() => window.addEventListener('keydown', onGlobalKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onGlobalKeydown))
</script>

<template>
  <div class="ip-command-search" data-testid="command-search">
    <input
      ref="input"
      v-model="query"
      type="search"
      :placeholder="copy.placeholder"
      :aria-label="localeMessages[locale].shell.top.globalSearch"
      :aria-expanded="open"
      aria-controls="platform-command-search-results"
      @focus="open = true"
      @keydown.esc="close"
    />
    <div v-if="open" id="platform-command-search-results" class="ip-command-search__results">
      <button
        v-for="item in results"
        :key="item.id"
        type="button"
        data-testid="command-search-result"
        @click="selectItem(item)"
      >
        {{ item.label }}
      </button>
      <span v-if="results.length === 0" class="ip-command-search__empty">
        {{ copy.empty }}
      </span>
    </div>
  </div>
</template>

<style scoped>
.ip-command-search {
  position: relative;
  width: min(480px, 100%);
  min-width: 160px;
}

.ip-command-search input {
  box-sizing: border-box;
  width: 100%;
  min-height: 32px;
  padding: 0 var(--ip-space-3);
  color: inherit;
  background: rgb(255 255 255 / 0.12);
  border: 1px solid rgb(255 255 255 / 0.3);
  border-radius: var(--ip-radius-md);
}

.ip-command-search__results {
  position: absolute;
  z-index: 20;
  top: calc(100% + var(--ip-space-1));
  right: 0;
  left: 0;
  display: flex;
  flex-direction: column;
  padding: var(--ip-space-1);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}

.ip-command-search__results button {
  padding: var(--ip-space-2) var(--ip-space-3);
  color: inherit;
  text-align: left;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}

.ip-command-search__results button:hover,
.ip-command-search__results button:focus-visible {
  background: var(--ip-color-bg-muted);
}

.ip-command-search__empty {
  padding: var(--ip-space-3);
  color: var(--ip-color-text-secondary);
}
</style>
