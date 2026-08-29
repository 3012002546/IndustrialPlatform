<script setup lang="ts">
import { computed, ref } from 'vue'
import { localeMessages } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'
import { operationLaunchers } from '@/operation/launchers'
import LocaleControl from '@/components/localization/LocaleControl.vue'
import PcExperienceModeControl from '@/components/shell/PcExperienceModeControl.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import type { OperationLauncher } from '@/operation/types'

const localization = useLocalizationStore()
const settingsOpen = ref(false)
const browserFullscreen = ref(false)

const copy = computed(() => localeMessages[localization.locale].operation)
function titleFor(id: string, fallback: string): string {
  const key = id.replaceAll('-', '')
  const mapping: Record<string, string> = {
    taskexecution: 'taskExecution',
    workorder: 'workOrder',
    materialfeeding: 'materialFeeding',
    weighing: 'weighing',
    feedingstatistics: 'feedingStatistics',
    materialconcentration: 'materialConcentration',
    materialreceipt: 'materialReceipt',
    recipeview: 'recipeView',
    interfacesettings: 'interfaceSettings',
  }
  return copy.value.launchers[mapping[key] ?? id] ?? fallback
}

async function toggleFullscreen(): Promise<void> {
  try {
    if (document.fullscreenElement !== null) await document.exitFullscreen()
    else await document.documentElement.requestFullscreen()
    browserFullscreen.value = document.fullscreenElement !== null
  } catch {
    browserFullscreen.value = false
  }
}

function activate(id: string, state: OperationLauncher['state']): void {
  if (state === 'coming-soon') return
  if (id === 'interface-settings') settingsOpen.value = !settingsOpen.value
}
</script>

<template>
  <section class="pc-operation-home" aria-labelledby="operation-title">
    <header class="pc-operation-home__header">
      <div>
        <h1 id="operation-title">{{ copy.title }}</h1>
        <p>{{ copy.description }}</p>
      </div>
      <strong class="pc-operation-home__state">{{ copy.launcherState.available }}</strong>
    </header>

    <div class="pc-operation-grid">
      <button
        v-for="launcher in operationLaunchers"
        :key="launcher.id"
        :data-operation-launcher="launcher.id"
        class="pc-operation-card"
        :class="{ 'pc-operation-card--disabled': launcher.state === 'coming-soon' }"
        type="button"
        :aria-disabled="launcher.state === 'coming-soon' ? 'true' : undefined"
        :disabled="launcher.state === 'coming-soon'"
        @click="activate(launcher.id, launcher.state)"
        @keydown.enter.prevent="activate(launcher.id, launcher.state)"
        @keydown.space.prevent="activate(launcher.id, launcher.state)"
      >
        <component :is="launcher.icon" class="pc-operation-card__icon" aria-hidden="true" />
        <span class="pc-operation-card__title">{{ titleFor(launcher.id, launcher.fallbackTitle) }}</span>
        <span class="pc-operation-card__status">
          {{ launcher.state === 'coming-soon' ? copy.launcherState.comingSoon : copy.launcherState.available }}
        </span>
      </button>
    </div>

    <aside v-if="settingsOpen" class="pc-operation-settings" data-testid="operation-settings-panel">
      <h2>{{ titleFor('interface-settings', '界面设置') }}</h2>
      <p>{{ copy.settingsDescription }}</p>
      <div class="pc-operation-settings__controls">
        <LocaleControl />
        <ThemeControl terminal="pc" />
        <PcExperienceModeControl mode="operation" />
        <button type="button" @click="toggleFullscreen">
          {{ browserFullscreen ? '退出全屏' : '全屏' }}
        </button>
      </div>
    </aside>
  </section>
</template>

<style scoped>
.pc-operation-home {
  max-width: 1440px;
  margin: 0 auto;
}

.pc-operation-home__header {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: var(--ip-space-4);
  margin-bottom: var(--ip-space-5);
}

.pc-operation-home h1 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: clamp(28px, 3vw, 36px);
  line-height: 1.2;
}

.pc-operation-home__header p {
  margin: var(--ip-space-2) 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-lg);
}

.pc-operation-home__state {
  color: var(--ip-color-success);
  font-size: var(--ip-font-size-lg);
}

.pc-operation-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--ip-space-4);
}

.pc-operation-card {
  display: flex;
  min-height: 176px;
  flex-direction: column;
  align-items: flex-start;
  justify-content: center;
  gap: var(--ip-space-3);
  padding: var(--ip-space-5);
  color: var(--ip-color-on-primary);
  background: var(--ip-color-primary);
  border: 1px solid color-mix(in srgb, var(--ip-color-primary) 80%, black);
  border-radius: var(--ip-radius-lg);
  cursor: pointer;
  text-align: left;
  transition: transform 120ms ease, filter 120ms ease;
}

.pc-operation-card:nth-child(3n + 2) {
  background: color-mix(in srgb, var(--ip-color-primary) 82%, #111827);
}

.pc-operation-card:nth-child(3n + 3) {
  background: color-mix(in srgb, var(--ip-color-primary) 68%, #111827);
}

.pc-operation-card:hover,
.pc-operation-card:focus-visible {
  filter: brightness(1.08);
  transform: translateY(-1px);
}

.pc-operation-card:focus-visible {
  outline: 3px solid var(--ip-focus-ring-color);
  outline-offset: 3px;
}

.pc-operation-card--disabled {
  cursor: not-allowed;
  filter: saturate(0.62);
  opacity: 0.78;
}

.pc-operation-card--disabled:hover {
  filter: saturate(0.62);
  transform: none;
}

.pc-operation-card__icon {
  width: 64px;
  height: 64px;
}

.pc-operation-card__title {
  font-size: clamp(24px, 2.2vw, 28px);
  font-weight: 700;
}

.pc-operation-card__status {
  font-size: var(--ip-font-size-md);
  font-weight: 600;
}

.pc-operation-settings {
  margin-top: var(--ip-space-5);
  padding: var(--ip-space-5);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.pc-operation-settings h2 {
  margin: 0;
  font-size: var(--ip-font-size-xl);
}

.pc-operation-settings p {
  margin: var(--ip-space-2) 0 var(--ip-space-4);
  color: var(--ip-color-text-secondary);
}

.pc-operation-settings__controls {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
}

.pc-operation-settings__controls > button {
  min-height: 36px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-on-primary);
  background: var(--ip-color-primary);
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

@media (max-width: 1279px) {
  .pc-operation-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 899px) {
  .pc-operation-main {
    padding: var(--ip-space-4);
  }

  .pc-operation-grid {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  .pc-operation-card {
    transition: none;
  }
}
</style>
