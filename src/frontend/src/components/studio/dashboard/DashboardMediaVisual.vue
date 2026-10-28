<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { Component } from 'vue'

import {
  DASHBOARD_MEDIA_ASSETS,
  DEFAULT_CAROUSEL_ASSET_IDS,
  DEFAULT_EMBEDDED_PAGE_REGISTRY,
  DEFAULT_IMAGE_ASSET_ID,
  DEFAULT_STATE_IMAGE_ASSET_ID,
  localizedLabel,
  resolveMediaAsset,
  type ContentSwitchItem,
  type DashboardMediaKind,
  type DashboardMediaLocale,
  type DashboardMediaVisualProps,
  type EmbeddedPageDefinition,
  type StateImageCase,
} from './mediaAssets'

const props = withDefaults(defineProps<DashboardMediaVisualProps>(), {
  fit: 'Contain',
  intervalSeconds: 3,
  pauseOnHover: true,
  locale: 'zh-CN',
  theme: 'dark',
})

interface MediaChange {
  kind: DashboardMediaKind
  index?: number
  itemId?: string
  assetId?: string
}

const emit = defineEmits<{
  change: [payload: MediaChange]
  'update:activeItemId': [value: string]
}>()

const copyByLocale: Record<
  DashboardMediaLocale,
  {
    assetDiagnosticTitle: string
    assetDiagnosticHint: string
    carouselEmpty: string
    carouselPosition: string
    stateFallback: string
    contentEmpty: string
    contentMissing: string
    embeddedMissing: string
    embeddedPolicyMismatch: string
    embeddedRendererMissing: string
    sandbox: string
  }
> = {
  'zh-CN': {
    assetDiagnosticTitle: '媒体资源不可用',
    assetDiagnosticHint: '请在受信注册表中登记 assetId。',
    carouselEmpty: '未配置轮播素材',
    carouselPosition: '当前素材',
    stateFallback: '未知状态，使用备用图',
    contentEmpty: '暂无可切换内容',
    contentMissing: '内容组未登记可渲染组件',
    embeddedMissing: '嵌入页面未登记，已阻止加载',
    embeddedPolicyMismatch: '嵌入页面的固定沙盒策略不匹配',
    embeddedRendererMissing: '页面已登记，但没有代码渲染器',
    sandbox: '沙盒策略',
  },
  'en-US': {
    assetDiagnosticTitle: 'Media resource unavailable',
    assetDiagnosticHint: 'Register the assetId in a trusted registry.',
    carouselEmpty: 'No carousel assets configured',
    carouselPosition: 'Current asset',
    stateFallback: 'Unknown state; fallback image shown',
    contentEmpty: 'No switchable content',
    contentMissing: 'No registered renderable content for this group',
    embeddedMissing: 'Embedded page is not registered; loading was blocked',
    embeddedPolicyMismatch: 'Embedded page sandbox policy does not match',
    embeddedRendererMissing: 'Page is registered but has no code renderer',
    sandbox: 'Sandbox policy',
  },
}

const locale = computed(() => props.locale ?? 'zh-CN')
const copy = computed(() => copyByLocale[locale.value])
const themeClass = computed(() => ({
  'is-dark': (props.theme ?? 'dark') === 'dark',
  'is-light': (props.theme ?? 'dark') === 'light',
}))

const assetRegistry = computed(() => ({
  ...DASHBOARD_MEDIA_ASSETS,
  ...(props.assetRegistry ?? {}),
}))

function assetResolution(assetId: unknown) {
  return resolveMediaAsset(assetId, assetRegistry.value)
}

const imageResolution = computed(() =>
  assetResolution(props.assetId === undefined ? DEFAULT_IMAGE_ASSET_ID : props.assetId),
)

const stateCases = computed<readonly StateImageCase[]>(() => props.cases ?? [])
const stateCase = computed(() =>
  stateCases.value.find((item) => sameValue(item.value, props.state)),
)
const stateAssetId = computed(
  () => stateCase.value?.assetId || props.defaultAssetId || DEFAULT_STATE_IMAGE_ASSET_ID,
)
const stateResolution = computed(() => assetResolution(stateAssetId.value))
const stateLabel = computed(() => {
  const selected = localizedLabel(stateCase.value?.label, locale.value)
  if (selected) return selected
  if (stateCase.value) return String(props.state ?? '')
  return copy.value.stateFallback
})

function sameValue(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true
  try {
    return JSON.stringify(left) === JSON.stringify(right)
  } catch {
    return String(left) === String(right)
  }
}

function clampSeconds(value: unknown, fallback: number): number {
  const number = Number(value)
  if (!Number.isFinite(number)) return fallback
  return Math.min(3600, Math.max(3, number))
}

const carouselAssetIds = computed<readonly string[]>(() =>
  props.assetIds === undefined ? DEFAULT_CAROUSEL_ASSET_IDS : props.assetIds,
)
const carouselIndex = ref(0)
const carouselPaused = ref(false)
let carouselTimer: ReturnType<typeof setInterval> | undefined
let carouselMounted = false

const carouselIntervalSeconds = computed(() => clampSeconds(props.intervalSeconds, 3))
const currentCarouselAssetId = computed(() => carouselAssetIds.value[carouselIndex.value] ?? '')
const carouselResolution = computed(() => assetResolution(currentCarouselAssetId.value))

function clearCarouselTimer() {
  if (carouselTimer !== undefined) {
    clearInterval(carouselTimer)
    carouselTimer = undefined
  }
}

function advanceCarousel() {
  const count = carouselAssetIds.value.length
  if (count < 2) return
  carouselIndex.value = (carouselIndex.value + 1) % count
  emit('change', {
    kind: 'ImageCarousel',
    index: carouselIndex.value,
    assetId: currentCarouselAssetId.value,
  })
}

function startCarouselTimer() {
  clearCarouselTimer()
  if (!carouselMounted || carouselPaused.value || carouselAssetIds.value.length < 2) return
  carouselTimer = setInterval(advanceCarousel, carouselIntervalSeconds.value * 1000)
}

function pauseCarousel() {
  if (props.pauseOnHover) {
    carouselPaused.value = true
    clearCarouselTimer()
  }
}

function resumeCarousel() {
  if (props.pauseOnHover) {
    carouselPaused.value = false
    startCarouselTimer()
  }
}

watch(
  [carouselAssetIds, () => props.intervalSeconds, () => props.pauseOnHover],
  () => {
    carouselIndex.value = 0
    carouselPaused.value = false
    startCarouselTimer()
  },
  { deep: true },
)

const contentItems = computed<readonly ContentSwitchItem[]>(() => props.items ?? [])
const contentActiveId = ref('')
let contentTimer: ReturnType<typeof setInterval> | undefined
let contentMounted = false

const activeContentIndex = computed(() =>
  contentItems.value.findIndex((item) => item.id === contentActiveId.value),
)
const activeContentItem = computed(() => contentItems.value[activeContentIndex.value])
const activeContentLabel = computed(() =>
  localizedLabel(activeContentItem.value?.label, locale.value),
)
const registeredChildComponents = computed(() => {
  const item = activeContentItem.value
  const registry = props.contentRegistry
  const children = item?.children ?? []
  return children
    .map((id) => ({ id, component: registry?.[id] }))
    .filter((entry): entry is { id: string; component: Component } => Boolean(entry.component))
})
const missingChildIds = computed(() => {
  const item = activeContentItem.value
  const registry = props.contentRegistry
  return (item?.children ?? []).filter((id) => !registry?.[id])
})

const contentIntervalSeconds = computed(() => {
  if (props.autoSwitchSeconds === undefined || props.autoSwitchSeconds === null) return null
  return clampSeconds(props.autoSwitchSeconds, 3)
})

function reconcileContentSelection() {
  const items = contentItems.value
  const requested = String(props.activeItemId ?? '')
  if (requested && items.some((item) => item.id === requested)) {
    contentActiveId.value = requested
    return
  }
  if (contentActiveId.value && items.some((item) => item.id === contentActiveId.value)) return
  contentActiveId.value = items[0]?.id ?? ''
}

function clearContentTimer() {
  if (contentTimer !== undefined) {
    clearInterval(contentTimer)
    contentTimer = undefined
  }
}

function advanceContent() {
  const items = contentItems.value
  if (items.length < 2 || !contentActiveId.value) return
  const index = Math.max(0, activeContentIndex.value)
  const next = items[(index + 1) % items.length]
  if (!next) return
  contentActiveId.value = next.id
  emit('update:activeItemId', next.id)
  emit('change', { kind: 'ContentSwitch', index: (index + 1) % items.length, itemId: next.id })
}

function startContentTimer() {
  clearContentTimer()
  const seconds = contentIntervalSeconds.value
  if (!contentMounted || seconds === null || contentItems.value.length < 2) return
  contentTimer = setInterval(advanceContent, seconds * 1000)
}

function selectContent(item: ContentSwitchItem) {
  contentActiveId.value = item.id
  emit('update:activeItemId', item.id)
  emit('change', {
    kind: 'ContentSwitch',
    index: activeContentIndex.value,
    itemId: item.id,
  })
}

watch(
  [contentItems, () => props.activeItemId, () => props.autoSwitchSeconds],
  () => {
    reconcileContentSelection()
    startContentTimer()
  },
  { deep: true, immediate: true },
)

const embeddedPages = computed<readonly EmbeddedPageDefinition[]>(
  () => props.embeddedPages ?? DEFAULT_EMBEDDED_PAGE_REGISTRY,
)
const embeddedPage = computed(() =>
  embeddedPages.value.find((page) => page.key === String(props.registeredPageKey ?? '')),
)
const embeddedPolicyMismatch = computed(() =>
  Boolean(
    embeddedPage.value &&
    props.sandboxPolicyKey &&
    props.sandboxPolicyKey !== embeddedPage.value.sandboxPolicyKey,
  ),
)
const embeddedTitle = computed(() => localizedLabel(embeddedPage.value?.title, locale.value))
const embeddedDescription = computed(() =>
  localizedLabel(embeddedPage.value?.description, locale.value),
)

onMounted(() => {
  carouselMounted = true
  contentMounted = true
  reconcileContentSelection()
  startCarouselTimer()
  startContentTimer()
})

onBeforeUnmount(() => {
  carouselMounted = false
  contentMounted = false
  clearCarouselTimer()
  clearContentTimer()
})
</script>

<template>
  <div
    class="dashboard-media-visual"
    :class="themeClass"
    :data-media-kind="props.kind"
    :data-locale="locale"
  >
    <template v-if="props.kind === 'Image'">
      <img
        v-if="imageResolution.src"
        class="dashboard-media-image"
        :src="imageResolution.src"
        :alt="props.alt || 'Dashboard image'"
        :style="{ objectFit: props.fit === 'Cover' ? 'cover' : 'contain' }"
        :data-asset-id="imageResolution.assetId"
      />
      <div
        v-else
        class="dashboard-media-diagnostic"
        role="alert"
        :data-diagnostic-code="imageResolution.diagnostic?.code"
      >
        <strong>{{ copy.assetDiagnosticTitle }}</strong>
        <span>{{ imageResolution.diagnostic?.message[locale] }}</span>
        <small>{{ copy.assetDiagnosticHint }}</small>
      </div>
    </template>

    <template v-else-if="props.kind === 'StateImage'">
      <div class="dashboard-media-state-image" :data-state-fallback="stateCase ? 'false' : 'true'">
        <img
          v-if="stateResolution.src"
          class="dashboard-media-image"
          :src="stateResolution.src"
          :alt="props.alt || stateLabel"
          :style="{ objectFit: props.fit === 'Cover' ? 'cover' : 'contain' }"
          :data-asset-id="stateResolution.assetId"
        />
        <div
          v-else
          class="dashboard-media-diagnostic"
          role="alert"
          :data-diagnostic-code="stateResolution.diagnostic?.code"
        >
          <strong>{{ copy.assetDiagnosticTitle }}</strong>
          <span>{{ stateResolution.diagnostic?.message[locale] }}</span>
          <small>{{ copy.assetDiagnosticHint }}</small>
        </div>
        <span class="dashboard-media-caption">{{ stateLabel }}</span>
      </div>
    </template>

    <template v-else-if="props.kind === 'ImageCarousel'">
      <div
        v-if="carouselAssetIds.length"
        class="dashboard-media-carousel"
        :class="{ 'is-paused': carouselPaused }"
        @mouseenter="pauseCarousel"
        @mouseleave="resumeCarousel"
      >
        <img
          v-if="carouselResolution.src"
          class="dashboard-media-image"
          :src="carouselResolution.src"
          :alt="props.alt || `${copy.carouselPosition} ${carouselIndex + 1}`"
          :style="{ objectFit: props.fit === 'Cover' ? 'cover' : 'contain' }"
          :data-asset-id="carouselResolution.assetId"
        />
        <div
          v-else
          class="dashboard-media-diagnostic"
          role="alert"
          :data-diagnostic-code="carouselResolution.diagnostic?.code"
        >
          <strong>{{ copy.assetDiagnosticTitle }}</strong>
          <span>{{ carouselResolution.diagnostic?.message[locale] }}</span>
          <small>{{ copy.assetDiagnosticHint }}</small>
        </div>
        <div class="dashboard-media-carousel__controls" aria-live="polite">
          <span>{{ carouselIndex + 1 }} / {{ carouselAssetIds.length }}</span>
          <button
            v-for="(_, index) in carouselAssetIds"
            :key="index"
            type="button"
            :aria-label="`${copy.carouselPosition} ${index + 1}`"
            :aria-current="index === carouselIndex ? 'true' : undefined"
            @click="carouselIndex = index"
          />
        </div>
      </div>
      <div v-else class="dashboard-media-diagnostic" role="status">
        <strong>{{ copy.carouselEmpty }}</strong>
      </div>
    </template>

    <template v-else-if="props.kind === 'ContentSwitch'">
      <section
        class="dashboard-media-content-switch"
        :aria-label="activeContentLabel || copy.contentEmpty"
      >
        <div v-if="contentItems.length" class="dashboard-media-tabs" role="tablist">
          <button
            v-for="item in contentItems"
            :key="item.id"
            type="button"
            role="tab"
            :aria-selected="item.id === contentActiveId"
            :class="{ 'is-active': item.id === contentActiveId }"
            @click="selectContent(item)"
          >
            {{ localizedLabel(item.label, locale) || item.id }}
          </button>
        </div>
        <div v-if="activeContentItem" class="dashboard-media-content" role="tabpanel">
          <slot
            v-if="$slots.default"
            :item="activeContentItem"
            :index="activeContentIndex"
            :item-id="contentActiveId"
          />
          <template v-else-if="registeredChildComponents.length">
            <component
              :is="entry.component"
              v-for="entry in registeredChildComponents"
              :key="entry.id"
            />
            <small v-if="missingChildIds.length" class="dashboard-media-inline-diagnostic">
              {{ copy.contentMissing }}: {{ missingChildIds.join(', ') }}
            </small>
          </template>
          <div v-else class="dashboard-media-diagnostic" role="alert">
            <strong>{{ copy.contentMissing }}</strong>
            <span>{{ activeContentItem.children?.join(', ') || activeContentItem.id }}</span>
          </div>
        </div>
        <div v-else class="dashboard-media-diagnostic" role="status">
          <strong>{{ copy.contentEmpty }}</strong>
        </div>
      </section>
    </template>

    <template v-else-if="props.kind === 'EmbeddedPage'">
      <section class="dashboard-media-embedded-page">
        <template v-if="embeddedPage && !embeddedPolicyMismatch">
          <header>
            <strong>{{ embeddedTitle || embeddedPage.key }}</strong>
            <span>{{ copy.sandbox }}: {{ embeddedPage.sandboxPolicyKey }}</span>
          </header>
          <p v-if="embeddedDescription" class="dashboard-media-embedded-page__description">
            {{ embeddedDescription }}
          </p>
          <component
            :is="embeddedPage.component"
            v-if="embeddedPage.component"
            class="dashboard-media-embedded-page__body"
            :parameters="props.parameterMap ?? {}"
            :sandbox-policy-key="embeddedPage.sandboxPolicyKey"
          />
          <div v-else class="dashboard-media-diagnostic" role="alert">
            <strong>{{ copy.embeddedRendererMissing }}</strong>
            <span>{{ embeddedPage.key }}</span>
          </div>
        </template>
        <div v-else-if="embeddedPolicyMismatch" class="dashboard-media-diagnostic" role="alert">
          <strong>{{ copy.embeddedPolicyMismatch }}</strong>
          <span>{{ String(props.registeredPageKey ?? '') }}</span>
        </div>
        <div v-else class="dashboard-media-diagnostic" role="alert">
          <strong>{{ copy.embeddedMissing }}</strong>
          <span>{{ String(props.registeredPageKey ?? '') || '—' }}</span>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.dashboard-media-visual {
  --media-bg: rgba(7, 25, 39, 0.88);
  --media-surface: rgba(18, 57, 77, 0.8);
  --media-border: rgba(119, 210, 218, 0.42);
  --media-text: #e5f8fb;
  --media-muted: #a9c4cf;
  --media-accent: #70d8d6;
  box-sizing: border-box;
  display: flex;
  width: 100%;
  height: 100%;
  min-height: 72px;
  overflow: hidden;
  color: var(--media-text);
  background: var(--media-bg);
  border: 1px solid var(--media-border);
  border-radius: 6px;
}

.dashboard-media-visual.is-light {
  --media-bg: #f7fafc;
  --media-surface: #e9f3f5;
  --media-border: #9bc5cb;
  --media-text: #183442;
  --media-muted: #4a6975;
  --media-accent: #11747b;
}

.dashboard-media-image {
  display: block;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
}

.dashboard-media-state-image,
.dashboard-media-carousel,
.dashboard-media-content-switch,
.dashboard-media-embedded-page {
  position: relative;
  display: flex;
  width: 100%;
  min-width: 0;
  min-height: 0;
  flex: 1;
}

.dashboard-media-state-image,
.dashboard-media-carousel {
  align-items: stretch;
  justify-content: center;
}

.dashboard-media-caption {
  position: absolute;
  right: 8px;
  bottom: 6px;
  max-width: calc(100% - 16px);
  padding: 3px 7px;
  overflow: hidden;
  color: var(--media-text);
  background: color-mix(in srgb, var(--media-bg) 84%, transparent);
  border: 1px solid var(--media-border);
  border-radius: 4px;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.dashboard-media-diagnostic {
  display: grid;
  align-content: center;
  width: 100%;
  min-width: 0;
  gap: 6px;
  padding: 12px;
  color: var(--media-text);
  background: var(--media-surface);
  text-align: center;
}

.dashboard-media-diagnostic strong {
  color: #ffb18d;
  font-size: 13px;
}

.dashboard-media-diagnostic span,
.dashboard-media-diagnostic small {
  overflow-wrap: anywhere;
}

.dashboard-media-diagnostic span,
.dashboard-media-diagnostic small,
.dashboard-media-inline-diagnostic,
.dashboard-media-embedded-page header span {
  color: var(--media-muted);
  font-size: 12px;
}

.dashboard-media-carousel {
  flex-direction: column;
}

.dashboard-media-carousel__controls {
  position: absolute;
  right: 8px;
  bottom: 8px;
  left: 8px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  color: var(--media-muted);
  font-size: 11px;
  pointer-events: none;
}

.dashboard-media-carousel__controls button {
  width: 8px;
  height: 8px;
  padding: 0;
  border: 1px solid var(--media-border);
  border-radius: 50%;
  background: transparent;
  cursor: pointer;
  pointer-events: auto;
}

.dashboard-media-carousel__controls button[aria-current='true'] {
  background: var(--media-accent);
}

.dashboard-media-tabs {
  display: flex;
  min-height: 32px;
  gap: 4px;
  padding: 6px;
  overflow-x: auto;
  border-bottom: 1px solid var(--media-border);
}

.dashboard-media-tabs button {
  flex: 0 0 auto;
  padding: 5px 10px;
  color: var(--media-muted);
  background: transparent;
  border: 1px solid transparent;
  border-radius: 4px;
  cursor: pointer;
}

.dashboard-media-tabs button:hover,
.dashboard-media-tabs button:focus-visible,
.dashboard-media-tabs button.is-active {
  color: var(--media-text);
  background: var(--media-surface);
  border-color: var(--media-border);
}

.dashboard-media-content-switch {
  flex-direction: column;
}

.dashboard-media-content {
  min-height: 0;
  flex: 1;
  overflow: auto;
  padding: 8px;
}

.dashboard-media-inline-diagnostic {
  display: block;
  margin-top: 8px;
}

.dashboard-media-embedded-page {
  flex-direction: column;
  gap: 6px;
  padding: 8px;
  overflow: auto;
}

.dashboard-media-embedded-page header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}

.dashboard-media-embedded-page__description {
  margin: 0;
  color: var(--media-muted);
  font-size: 12px;
}

.dashboard-media-embedded-page__body {
  min-height: 0;
  flex: 1;
}
</style>
