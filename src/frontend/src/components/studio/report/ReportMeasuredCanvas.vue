<script setup lang="ts">
import { computed, useId } from 'vue'
import { useReportCopy } from './copy'
import { reportUnitMm, type ReportUnit } from './geometry'

const props = defineProps<{
  widthMm: number
  heightMm: number
  margin: { top?: number; right?: number; bottom?: number; left?: number }
  unit: ReportUnit
  zoom: number
  sections: {
    id: string
    label: string
    name: string
    kind: string
    startMm: number
    heightMm: number
  }[]
  selectedSection?: string
}>()
const emit = defineEmits<{
  marginPointer: [side: 'top' | 'right' | 'bottom' | 'left', event: PointerEvent]
  selectSection: [id: string]
  sectionPointer: [id: string, event: PointerEvent]
}>()
const t = useReportCopy()
const markerId = useId().replaceAll(':', '')
const gutter = 28
const rail = 104
const width = computed(() => props.widthMm * 3)
const height = computed(() => props.heightMm * 3)
const margins = computed(
  () =>
    Object.fromEntries(
      ['top', 'right', 'bottom', 'left'].map((side) => [
        side,
        Number(props.margin[side as keyof typeof props.margin] ?? 15) * 3,
      ]),
    ) as Record<'top' | 'right' | 'bottom' | 'left', number>,
)
// Units change the ruler presentation only. Report coordinates remain millimetres.
const mmPerUnit = computed(() => reportUnitMm[props.unit])
const majorStep = computed(() => {
  const minimum = 42 / (3 * Math.max(props.zoom, 0.05) * mmPerUnit.value)
  const decade = 10 ** Math.floor(Math.log10(minimum))
  return [1, 2, 5, 10].map((n) => n * decade).find((n) => n >= minimum)!
})
function ticks(lengthMm: number) {
  const step = majorStep.value / 5
  const count = Math.min(2000, Math.floor(lengthMm / mmPerUnit.value / step))
  return Array.from({ length: count + 1 }, (_, index) => ({
    position: index * step * mmPerUnit.value * 3,
    label: index % 5 === 0 ? Number((index * step).toFixed(3)).toString() : '',
  }))
}
const horizontal = computed(() => ticks(props.widthMm))
const sectionRail = computed(() => [
  {
    id: 'top-margin',
    label: t.value('topMargin').replace(' mm', ''),
    kind: 'Margin',
    name: '',
    top: 0,
    height: margins.value.top,
  },
  ...props.sections.map((s) => ({ ...s, top: s.startMm * 3, height: s.heightMm * 3 })),
  {
    id: 'bottom-margin',
    name: '',
    label: t.value('bottomMargin').replace(' mm', ''),
    kind: 'Margin',
    top: height.value - margins.value.bottom,
    height: margins.value.bottom,
  },
])
const verticalSections = computed(() =>
  [
    { id: 'top-margin', startMm: 0, heightMm: margins.value.top / 3 },
    ...props.sections,
    {
      id: 'bottom-margin',
      startMm: props.heightMm - margins.value.bottom / 3,
      heightMm: margins.value.bottom / 3,
    },
  ].map((section) => ({ ...section, ticks: ticks(section.heightMm) })),
)
const selectedRegion = computed(() => sectionRail.value.find((s) => s.id === props.selectedSection))
const format = (pixels: number) =>
  `${Number((pixels / 3 / mmPerUnit.value).toFixed(2))} ${props.unit}`
const labels = computed(() => [
  {
    side: 'top',
    title: t.value('topMargin').replace(' mm', ''),
    value: margins.value.top,
    x: width.value / 2,
    y: Math.max(9, margins.value.top / 2),
    rotate: 0,
  },
  {
    side: 'bottom',
    title: t.value('bottomMargin').replace(' mm', ''),
    value: margins.value.bottom,
    x: width.value / 2,
    y: height.value - Math.max(9, margins.value.bottom / 2),
    rotate: 0,
  },
  {
    side: 'left',
    title: t.value('leftMargin').replace(' mm', ''),
    value: margins.value.left,
    x: Math.max(9, margins.value.left / 2),
    y: height.value / 2,
    rotate: -90,
  },
  {
    side: 'right',
    title: t.value('rightMargin').replace(' mm', ''),
    value: margins.value.right,
    x: width.value - Math.max(9, margins.value.right / 2),
    y: height.value / 2,
    rotate: 90,
  },
])
const handles = computed(() => [
  {
    side: 'left' as const,
    x: rail + gutter + margins.value.left,
    y: gutter / 2,
    title: t.value('leftMargin'),
  },
  {
    side: 'right' as const,
    x: rail + gutter + width.value - margins.value.right,
    y: gutter / 2,
    title: t.value('rightMargin'),
  },
  {
    side: 'top' as const,
    x: rail + gutter / 2,
    y: gutter + margins.value.top,
    title: t.value('topMargin'),
  },
  {
    side: 'bottom' as const,
    x: rail + gutter / 2,
    y: gutter + height.value,
    title: t.value('bottomMargin'),
  },
])
</script>

<template>
  <div
    class="report-measured-canvas"
    :style="{ width: width + (rail + gutter) * 2 + 'px', height: height + gutter * 2 + 'px' }"
  >
    <div class="report-measured-sheet" :style="{ left: rail + gutter + 'px', top: gutter + 'px' }">
      <slot />
    </div>
    <svg
      class="report-measure-overlay"
      :width="width + (rail + gutter) * 2"
      :height="height + gutter * 2"
      role="img"
      :aria-label="t('rulerDescription')"
    >
      <defs>
        <marker
          :id="markerId"
          viewBox="0 0 6 6"
          refX="3"
          refY="3"
          markerWidth="5"
          markerHeight="5"
          orient="auto-start-reverse"
        >
          <path d="M 6 0 L 0 3 L 6 6" fill="none" stroke="currentColor" />
        </marker>
      </defs>
      <g :transform="`translate(${rail + gutter},${gutter})`">
        <rect
          class="report-ruler-background"
          :x="-gutter"
          :y="-gutter"
          :width="gutter"
          :height="gutter"
        />
        <rect class="report-ruler-background" x="0" :y="-gutter" :width="width" :height="gutter" />
        <rect class="report-ruler-background" :x="-gutter" y="0" :width="gutter" :height="height" />
        <text
          class="report-ruler-unit"
          :x="-gutter / 2"
          :y="-gutter / 2"
          text-anchor="middle"
          dominant-baseline="middle"
        >
          {{ unit }}
        </text>
        <g class="report-ruler-horizontal">
          <g
            v-for="tick in horizontal"
            :key="tick.position"
            :transform="`translate(${tick.position},0)`"
          >
            <line y1="0" :y2="tick.label ? -10 : -5" />
            <text v-if="tick.label" x="2" y="-14">{{ tick.label }}</text>
          </g>
        </g>
        <g class="report-ruler-vertical">
          <g
            v-for="section in verticalSections"
            :key="section.id"
            :data-ruler-section="section.id"
            :transform="`translate(0,${section.startMm * 3})`"
          >
            <line :x1="-gutter" x2="0" y1="0" y2="0" />
            <g
              v-for="tick in section.ticks"
              :key="tick.position"
              :transform="`translate(0,${tick.position})`"
            >
              <line x1="0" :x2="tick.label ? -10 : -5" />
              <text v-if="tick.label" transform="rotate(-90)" x="2" y="-14">{{ tick.label }}</text>
            </g>
          </g>
        </g>
        <g class="report-margin-shading">
          <rect data-margin="top" x="0" y="0" :width="width" :height="margins.top" />
          <rect
            data-margin="bottom"
            x="0"
            :y="height - margins.bottom"
            :width="width"
            :height="margins.bottom"
          />
          <rect
            data-margin="left"
            x="0"
            :y="margins.top"
            :width="margins.left"
            :height="Math.max(0, height - margins.top - margins.bottom)"
          />
          <rect
            data-margin="right"
            :x="width - margins.right"
            :y="margins.top"
            :width="margins.right"
            :height="Math.max(0, height - margins.top - margins.bottom)"
          />
        </g>
        <g class="report-margin-boundaries">
          <line :x1="-gutter" :y1="margins.top" :x2="width" :y2="margins.top" />
          <line
            :x1="-gutter"
            :y1="height - margins.bottom"
            :x2="width"
            :y2="height - margins.bottom"
          />
          <line :x1="margins.left" :y1="-gutter" :x2="margins.left" :y2="height" />
          <line
            :x1="width - margins.right"
            :y1="-gutter"
            :x2="width - margins.right"
            :y2="height"
          />
        </g>
        <g
          class="report-margin-dimensions"
          :marker-start="`url(#${markerId})`"
          :marker-end="`url(#${markerId})`"
        >
          <line :x1="width / 2 - 85" y1="0" :x2="width / 2 - 85" :y2="margins.top" />
          <line
            :x1="width / 2 - 85"
            :y1="height - margins.bottom"
            :x2="width / 2 - 85"
            :y2="height"
          />
          <line x1="0" :y1="height / 2 - 85" :x2="margins.left" :y2="height / 2 - 85" />
          <line
            :x1="width - margins.right"
            :y1="height / 2 - 85"
            :x2="width"
            :y2="height / 2 - 85"
          />
        </g>
        <text
          v-for="label in labels"
          :key="label.side"
          class="report-margin-label"
          :data-side="label.side"
          :transform="`translate(${label.x},${label.y}) rotate(${label.rotate})`"
          text-anchor="middle"
          dominant-baseline="middle"
        >
          {{ label.title }} {{ format(label.value) }}
        </text>
      </g>
    </svg>
    <div
      v-if="selectedRegion"
      class="report-section-selection"
      :data-section-id="selectedRegion.id"
      aria-hidden="true"
      :style="{
        top: gutter + selectedRegion.top + 'px',
        width: rail + gutter + width + 'px',
        height: selectedRegion.height + 'px',
      }"
    />
    <button
      v-for="section in sectionRail"
      :key="section.id"
      type="button"
      class="report-section-rail"
      :data-kind="section.kind"
      :data-section-id="section.id"
      :class="{ selected: selectedSection === section.id }"
      :style="{
        top: gutter + section.top + 'px',
        height: section.height + 'px',
        width: rail + 'px',
      }"
      :aria-label="`${t('selectSection')} · ${section.label}`"
      :title="`${section.label}${section.name ? ' · ' + section.name : ''} · ${format(section.height)}`"
      :aria-pressed="selectedSection === section.id"
      @click="emit('selectSection', section.id)"
    >
      <span
        >{{ section.label.replace('区段', '')
        }}<template v-if="section.name"> · {{ section.name }}</template></span
      >
    </button>
    <button
      v-for="section in sectionRail"
      :key="`resize-${section.id}`"
      type="button"
      class="report-section-resize"
      :data-section-id="section.id"
      :style="{ top: gutter + section.top + section.height + 'px', width: rail + 'px' }"
      :aria-label="`${t('resizeBand')} · ${section.label}${section.name ? ' · ' + section.name : ''}`"
      :title="`${t('resizeBand')} · ${format(section.height)}`"
      @pointerdown.stop.prevent="
        section.kind === 'Margin'
          ? emit('marginPointer', section.id === 'top-margin' ? 'top' : 'bottom', $event)
          : emit('sectionPointer', section.id, $event)
      "
    />
    <button
      v-for="handle in handles"
      :key="handle.side"
      type="button"
      class="report-margin-handle"
      :data-side="handle.side"
      :class="{ horizontal: handle.side === 'left' || handle.side === 'right' }"
      :style="{ left: handle.x + 'px', top: handle.y + 'px' }"
      :aria-label="`${t('dragMargin')} · ${handle.title.replace(' mm', '')} ${unit}`"
      :title="`${handle.title.replace(' mm', '')}: ${format(margins[handle.side])} · ${t('dragHint')}`"
      @pointerdown.stop.prevent="emit('marginPointer', handle.side, $event)"
    />
  </div>
</template>

<style scoped>
.report-section-selection {
  position: absolute;
  left: 0;
  z-index: 6;
  box-sizing: border-box;
  border: 2px solid #267cae;
  background: #267cae0a;
  pointer-events: none;
}
.report-section-rail {
  position: absolute;
  left: 0;
  z-index: 4;
  overflow: hidden;
  padding: 2px;
  border: 1px solid #9aafc0;
  background: #dce9f3;
  color: #35556d;
  cursor: pointer;
}
.report-section-rail span {
  display: block;
  margin: auto;
  max-height: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: normal;
  overflow-wrap: anywhere;
  font: 11px/14px sans-serif;
}
.report-section-rail[data-kind='Margin'] {
  background: #e8ebef;
  color: #526374;
}
.report-section-rail[data-kind='PageHeader'],
.report-section-rail[data-kind='PageFooter'] {
  background: #cee8dc;
  color: #28634b;
}
.report-section-rail[data-kind='GroupHeader'],
.report-section-rail[data-kind='GroupFooter'] {
  background: #e6d7ef;
  color: #65477b;
}
.report-section-rail[data-kind='ReportHeader'],
.report-section-rail[data-kind='ReportFooter'] {
  background: #d8dff0;
  color: #3f5278;
}
.report-section-rail.selected,
.report-section-rail:hover {
  box-shadow: inset 3px 0 #267cae;
  filter: brightness(0.96);
}
.report-section-rail.selected {
  outline: 2px solid #267cae;
  outline-offset: -2px;
  box-shadow: inset 5px 0 #267cae;
  font-weight: 600;
}
.report-section-rail.selected::before {
  content: '';
  position: absolute;
  right: 4px;
  top: 4px;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: #267cae;
}
.report-section-resize {
  position: absolute;
  left: 0;
  z-index: 5;
  height: 8px;
  transform: translateY(-50%);
  padding: 0;
  border: 0;
  background: transparent;
  cursor: ns-resize;
  touch-action: none;
}
.report-section-resize::after {
  content: '';
  display: block;
  width: 20px;
  margin: auto;
  border-top: 2px solid #688ba5;
}
.report-section-resize:hover,
.report-section-resize:focus-visible {
  background: #79a7c366;
}
.report-margin-handle {
  position: absolute;
  z-index: 4;
  width: 18px;
  height: 10px;
  padding: 0;
  transform: translate(-50%, -50%);
  border: 1px solid #316f91;
  border-radius: 2px;
  background: #5fa5c8;
  cursor: ns-resize;
  touch-action: none;
}
.report-margin-handle.horizontal {
  width: 10px;
  height: 18px;
  cursor: ew-resize;
}
.report-margin-handle:hover,
.report-margin-handle:focus-visible {
  background: #156e9b;
  outline: 2px solid #89c7ed;
}
.report-measured-canvas {
  position: relative;
  background: var(--el-fill-color-light);
}
.report-measured-sheet {
  position: absolute;
  box-shadow: 0 1px 5px #0002;
}
.report-measured-sheet :deep(.report-paper) {
  background-image:
    linear-gradient(to right, #94a3b817 1px, transparent 1px),
    linear-gradient(to bottom, #94a3b817 1px, transparent 1px);
  background-size: 15px 15px;
  background-origin: content-box;
  background-clip: content-box;
}
.report-measure-overlay {
  position: absolute;
  inset: 0;
  pointer-events: none;
  overflow: visible;
  color: #8b6b2a;
}
.report-ruler-background {
  fill: #fff0c6;
  stroke: #c7b98e;
  stroke-width: 0.5;
}
.report-ruler-horizontal line,
.report-ruler-vertical line {
  stroke: #85764e;
  stroke-width: 0.7;
}
.report-ruler-horizontal text,
.report-ruler-vertical text,
.report-ruler-unit {
  fill: #63532c;
  font: 10px sans-serif;
}
.report-ruler-unit {
  font-weight: 600;
}
.report-margin-shading {
  fill: #e7edf4;
  fill-opacity: 0.8;
}
.report-margin-boundaries {
  stroke: #7c95ac;
  stroke-width: 0.8;
  stroke-dasharray: 4 3;
}
.report-margin-dimensions {
  stroke: #6c8295;
  stroke-width: 0.8;
  color: #6c8295;
}
.report-margin-label {
  fill: #496477;
  font: 11px sans-serif;
  paint-order: stroke;
  stroke: #edf1f6;
  stroke-width: 3px;
  stroke-linejoin: round;
}
</style>
