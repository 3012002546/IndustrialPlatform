<script setup lang="ts">
import { computed } from 'vue'
import { Bell, Document } from '@element-plus/icons-vue'
import { RouterLink } from 'vue-router'

import { systemDataPageCopy } from '@/localization/systemData'
import { usePlatformLocale } from '@/localization/localeContext'
import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES } from '@/router/routeNames'
import { useAuthStore } from '@/stores/authStore'

const props = defineProps<{ terminal: 'pda' | 'mobile' }>()

const authStore = useAuthStore()
const locale = usePlatformLocale()
const copy = computed(() => systemDataPageCopy(locale.value, 'terminalFeatureMenu'))
const canUpload = computed(() => authStore.hasPermission(PERMISSIONS.systemDataFileRead))
const canReadNotifications = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationInboxRead))
const showNotification = computed(() => props.terminal === 'pda' && canReadNotifications.value)
const hasFeature = computed(() => canUpload.value || showNotification.value)
const fileRouteName = computed(() =>
  props.terminal === 'pda' ? ROUTE_NAMES.pdaFiles : ROUTE_NAMES.mobileFiles,
)
</script>

<template>
  <section
    v-if="hasFeature"
    class="terminal-feature-menu"
    data-testid="terminal-feature-menu"
    :aria-labelledby="`terminal-feature-menu-title-${terminal}`"
  >
    <div class="terminal-feature-menu__header">
      <div>
        <p class="terminal-feature-menu__eyebrow">{{ copy.available }}</p>
        <h2 :id="`terminal-feature-menu-title-${terminal}`">{{ copy.title }}</h2>
        <p class="terminal-feature-menu__description">{{ copy.description }}</p>
      </div>
    </div>

    <div class="terminal-feature-menu__group">
      <h3>{{ copy.fileGroup }}</h3>
      <p>{{ copy.fileGroupDescription }}</p>
    </div>

    <RouterLink
      class="terminal-feature-menu__item"
      data-testid="terminal-feature-menu-file"
      :to="{ name: fileRouteName }"
    >
      <span class="terminal-feature-menu__icon" aria-hidden="true"><Document :size="24" /></span>
      <span class="terminal-feature-menu__item-copy">
        <strong>{{ copy.fileUpload }}</strong>
        <span>{{ copy.fileUploadDescription }}</span>
      </span>
      <span class="terminal-feature-menu__arrow" aria-hidden="true">→</span>
    </RouterLink>

    <div v-if="showNotification" class="terminal-feature-menu__group">
      <h3>{{ copy.notificationGroup }}</h3>
      <p>{{ copy.notificationGroupDescription }}</p>
    </div>

    <RouterLink
      v-if="showNotification"
      class="terminal-feature-menu__item"
      data-testid="terminal-feature-menu-notifications"
      :to="{ name: ROUTE_NAMES.pdaNotifications }"
    >
      <span class="terminal-feature-menu__icon" aria-hidden="true"><Bell :size="24" /></span>
      <span class="terminal-feature-menu__item-copy">
        <strong>{{ copy.notification }}</strong>
        <span>{{ copy.notificationDescription }}</span>
      </span>
      <span class="terminal-feature-menu__arrow" aria-hidden="true">→</span>
    </RouterLink>
  </section>
</template>

<style scoped>
.terminal-feature-menu {
  display: grid;
  gap: var(--ip-space-4);
  padding: var(--ip-space-5);
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--ip-color-primary) 7%, transparent), transparent 58%),
    var(--ip-color-bg-container);
  border: 1px solid color-mix(in srgb, var(--ip-color-primary) 22%, var(--ip-color-border));
  border-radius: var(--ip-radius-lg);
  box-shadow: 0 4px 14px color-mix(in srgb, var(--ip-color-text-primary) 5%, transparent);
}

.terminal-feature-menu__eyebrow {
  margin: 0 0 var(--ip-space-1);
  color: var(--ip-color-primary);
  font-size: var(--ip-font-size-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.terminal-feature-menu h2,
.terminal-feature-menu h3,
.terminal-feature-menu p {
  margin: 0;
}

.terminal-feature-menu h2 {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-xl);
  line-height: var(--ip-line-height-tight);
}

.terminal-feature-menu__description,
.terminal-feature-menu__group p,
.terminal-feature-menu__item-copy span {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  line-height: var(--ip-line-height-normal);
}

.terminal-feature-menu__group {
  display: grid;
  gap: var(--ip-space-1);
  padding-top: var(--ip-space-2);
  border-top: 1px solid var(--ip-color-border);
}

.terminal-feature-menu__group h3 {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.terminal-feature-menu__item {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--ip-space-3);
  min-height: var(--ip-touch-min-size);
  padding: var(--ip-space-3);
  color: inherit;
  text-decoration: none;
  background: var(--ip-color-bg-page);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  transition: border-color 150ms ease, background-color 150ms ease, transform 150ms ease;
}

.terminal-feature-menu__item:hover,
.terminal-feature-menu__item:focus-visible {
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-primary);
}

.terminal-feature-menu__item:active {
  transform: translateY(1px);
}

.terminal-feature-menu__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--ip-touch-min-size);
  height: var(--ip-touch-min-size);
  color: var(--ip-color-primary);
  background: color-mix(in srgb, var(--ip-color-primary) 12%, transparent);
  border-radius: var(--ip-radius-md);
}

.terminal-feature-menu__item-copy {
  display: grid;
  gap: var(--ip-space-1);
  min-width: 0;
}

.terminal-feature-menu__item-copy strong {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.terminal-feature-menu__arrow {
  color: var(--ip-color-primary);
  font-size: var(--ip-font-size-xl);
}

@media (max-width: 600px) {
  .terminal-feature-menu {
    padding: var(--ip-space-4);
  }

  .terminal-feature-menu__item {
    min-height: calc(var(--ip-touch-min-size-mobile) + var(--ip-space-3));
  }
}

@media (prefers-reduced-motion: reduce) {
  .terminal-feature-menu__item {
    transition: none;
  }
}
</style>
