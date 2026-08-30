<script setup lang="ts">
import { computed, ref } from 'vue'

import { APP_INFO } from '@/app/appInfo'

export type PlatformBrandVariant = 'light' | 'dark' | 'mark' | 'monochrome'

export interface PlatformBrandProps {
  variant: PlatformBrandVariant
  compact?: boolean
  showName?: boolean
}

const props = withDefaults(defineProps<PlatformBrandProps>(), { showName: true })
const imageFailed = ref(false)

const source = computed(() => {
  if (props.compact || !props.showName || props.variant === 'mark') {
    return `${APP_INFO.brandAssetBasePath}/mark.svg`
  }
  return `${APP_INFO.brandAssetBasePath}/${props.variant === 'monochrome' ? 'monochrome' : `horizontal-${props.variant}`}.svg`
})

function onImageError(): void {
  imageFailed.value = true
}
</script>

<template>
  <span
    class="ip-brand"
    :class="{ 'ip-brand--compact': props.compact, 'ip-brand--fallback': imageFailed }"
    :aria-label="APP_INFO.name"
  >
    <span v-if="imageFailed" class="ip-brand__fallback-mark" role="img" :aria-label="APP_INFO.name">
      IP
    </span>
    <img v-else class="ip-brand__image" :src="source" :alt="APP_INFO.name" @error="onImageError" />
  </span>
</template>

<style scoped>
.ip-brand {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-width: 0;
  color: inherit;
  line-height: 1;
}

.ip-brand__image {
  display: block;
  width: auto;
  height: 32px;
  max-width: 200px;
}

.ip-brand--compact .ip-brand__image,
.ip-brand--compact .ip-brand__fallback-mark {
  width: 32px;
  height: 32px;
}

.ip-brand__name {
  overflow: hidden;
  font-size: var(--ip-font-size-lg);
  font-weight: 650;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-brand__fallback-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: currentColor;
  border: 1px solid currentColor;
  border-radius: var(--ip-radius-md);
  font-size: var(--ip-font-size-sm);
  font-weight: 700;
}
</style>
