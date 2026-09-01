<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'

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
const resultsPanel = ref<HTMLElement | null>(null)
const resultsStyle = ref<Record<string, string>>({})
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.commandSearch)
let positionFrame: number | null = null

const results = computed(() => {
  const value = query.value.trim().toLocaleLowerCase()
  const candidates = value === ''
    ? props.items
    : props.items.filter((item) => item.label.toLocaleLowerCase().includes(value))
  const seen = new Set<string>()
  return candidates
    .filter((item) => {
      if (seen.has(item.id)) return false
      seen.add(item.id)
      return true
    })
    .slice(0, 8)
})

function focusSearch(): void {
  open.value = true
  void input.value?.focus()
  schedulePositionResults()
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

function positionResults(): void {
  const rect = input.value?.getBoundingClientRect()
  if (rect === undefined) return
  const width = Math.min(rect.width, window.innerWidth - 16)
  const left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8))
  resultsStyle.value = {
    top: `${Math.min(window.innerHeight - 8, rect.bottom + 6)}px`,
    left: `${left}px`,
    width: `${width}px`,
  }
}

function schedulePositionResults(): void {
  void nextTick(() => {
    positionResults()
    if (typeof window.requestAnimationFrame !== 'function') return
    if (positionFrame !== null) window.cancelAnimationFrame(positionFrame)
    positionFrame = window.requestAnimationFrame(() => {
      positionFrame = null
      positionResults()
    })
  })
}

function onDocumentPointerDown(event: PointerEvent): void {
  if (input.value?.contains(event.target as Node) || resultsPanel.value?.contains(event.target as Node)) return
  close()
}

function selectItem(item: PlatformCommandItem): void {
  emit('select', item.id)
  close()
}

onMounted(() => {
  window.addEventListener('keydown', onGlobalKeydown)
  window.addEventListener('resize', schedulePositionResults)
  window.addEventListener('scroll', schedulePositionResults, true)
  document.addEventListener('pointerdown', onDocumentPointerDown)
})
onBeforeUnmount(() => {
  if (positionFrame !== null) window.cancelAnimationFrame(positionFrame)
  window.removeEventListener('keydown', onGlobalKeydown)
  window.removeEventListener('resize', schedulePositionResults)
  window.removeEventListener('scroll', schedulePositionResults, true)
  document.removeEventListener('pointerdown', onDocumentPointerDown)
})
</script>

<template>
  <div class="ip-command-search" data-testid="command-search">
    <input
      ref="input"
      v-model="query"
      type="search"
      :placeholder="copy.placeholder"
      :aria-label="localeMessages[locale].shell.top.globalSearch"
      aria-keyshortcuts="Control+K"
      :aria-expanded="open"
      aria-controls="platform-command-search-results"
      @focus="open = true; schedulePositionResults()"
      @click="focusSearch"
      @keydown.esc="close"
    />
    <kbd data-testid="command-search-shortcut" aria-hidden="true">Ctrl+K</kbd>
    <div
      v-if="open"
      id="platform-command-search-results"
      ref="resultsPanel"
      class="ip-command-search__results"
      :style="resultsStyle"
    >
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
  min-width: 0;
}

.ip-command-search input {
  box-sizing: border-box;
  width: 100%;
  min-height: 32px;
  padding: 0 58px 0 var(--ip-space-3);
  color: var(--ip-shell-topbar-text);
  font: inherit;
  background: rgb(255 255 255 / 0.12);
  border: 1px solid rgb(255 255 255 / 0.3);
  border-radius: var(--ip-radius-md);
}

.ip-command-search input::placeholder {
  color: var(--ip-shell-topbar-text-secondary);
  opacity: 1;
}

.ip-command-search kbd {
  position: absolute;
  top: 50%;
  right: var(--ip-space-2);
  pointer-events: none;
  padding: 2px 5px;
  color: rgb(255 255 255 / 0.78);
  font: inherit;
  font-size: 11px;
  line-height: 1.2;
  border: 1px solid rgb(255 255 255 / 0.28);
  border-radius: var(--ip-radius-sm);
  transform: translateY(-50%);
}

.ip-command-search__results {
  position: fixed;
  box-sizing: border-box;
  z-index: 20;
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 16px);
  overflow: auto;
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
