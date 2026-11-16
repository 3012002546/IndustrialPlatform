<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import { advancedOptions } from './advancedControls'
import { useReportCopy } from './copy'
const props = defineProps<{ control: Json; caption: string; label: string }>()
const t = useReportCopy()
const options = computed(() => ({
  ...advancedOptions(String(props.control.type)),
  ...props.control.advanced,
}))
const cells = computed(() => Math.max(1, Math.min(128, Number(options.value.cells) || 12)))
const characters = computed(() =>
  Array.from(
    new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(props.caption),
    (part) => part.segment,
  ),
)
const line = computed(() => {
  switch (options.value.direction) {
    case 'Vertical':
      return { x1: 50, y1: 1, x2: 50, y2: 99 }
    case 'DiagonalDown':
      return { x1: 1, y1: 1, x2: 99, y2: 99 }
    case 'DiagonalUp':
      return { x1: 1, y1: 99, x2: 99, y2: 1 }
    default:
      return { x1: 1, y1: 50, x2: 99, y2: 50 }
  }
})
const stroke = computed(() => ({
  stroke: options.value.stroke,
  strokeWidth: Number(options.value.strokeWidthMm) * 3 + 'px',
  vectorEffect: 'non-scaling-stroke' as const,
}))
</script>
<template>
  <div
    v-if="control.type === 'CharacterComb'"
    class="design-comb"
    :style="{ gridTemplateColumns: 'repeat(' + cells + ', minmax(0, 1fr))' }"
    :title="characters.length > cells ? t('combOverflow') : caption"
  >
    <span v-for="index in cells" :key="index" class="design-comb-cell">{{
      characters[index - 1] ?? ''
    }}</span>
    <span v-if="characters.length > cells" class="design-overflow">!</span>
  </div>
  <svg
    v-else-if="control.type === 'Shape' || control.type === 'Line'"
    class="design-shape"
    viewBox="0 0 100 100"
    preserveAspectRatio="none"
    :aria-label="label"
  >
    <line
      v-if="control.type === 'Line'"
      v-bind="line"
      :style="stroke"
      :stroke-dasharray="
        options.dash === 'Dashed' ? '8 4' : options.dash === 'Dotted' ? '2 3' : undefined
      "
    />
    <ellipse
      v-else-if="options.kind === 'Ellipse'"
      cx="50"
      cy="50"
      rx="48"
      ry="48"
      :style="stroke"
      :fill="options.fill"
    />
    <polygon
      v-else-if="options.kind === 'Diamond'"
      points="50,2 98,50 50,98 2,50"
      :style="stroke"
      :fill="options.fill"
    />
    <rect v-else x="2" y="2" width="96" height="96" :style="stroke" :fill="options.fill" />
  </svg>
  <div v-else class="design-guide">
    <strong>{{ label }}</strong>
    <span>{{ control.type === 'Barcode' ? t('barcodeDesignHint') : t('advancedDesignHint') }}</span>
    <small>{{ caption }}</small>
  </div>
</template>
<style scoped>
.design-comb {
  display: grid;
  width: 100%;
  height: 100%;
  position: relative;
}
.design-comb-cell {
  border: 1px solid currentColor;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}
.design-comb-cell + .design-comb-cell {
  border-left: 0;
}
.design-overflow {
  position: absolute;
  right: 0;
  top: 0;
  color: #b42318;
  font-weight: bold;
}
.design-shape {
  display: block;
  height: 100%;
  width: 100%;
}
.design-guide {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  border: 1px dashed #64748b;
  box-sizing: border-box;
  overflow: hidden;
  padding: 3px;
}
.design-guide span,
.design-guide small {
  font-size: 10px;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
