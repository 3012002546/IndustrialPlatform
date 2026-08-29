<script setup lang="ts">
import { useLocalizationStore } from '@/stores/localizationStore'
import type { SupportedLocale } from '@/localization/types'

const localization = useLocalizationStore()

function onChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  if (value === 'zh-CN' || value === 'en-US') localization.setLocale(value as SupportedLocale)
}
</script>

<template>
  <label class="ip-locale-control">
    <span class="ip-sr-only">语言</span>
    <select aria-label="语言" :value="localization.locale" @change="onChange">
      <option value="zh-CN">中文</option>
      <option value="en-US">English</option>
    </select>
  </label>
</template>

<style scoped>
.ip-locale-control select {
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 1px solid currentColor;
  border-radius: var(--ip-radius-md);
  font: inherit;
}

.ip-sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
