import type { Component } from 'vue'

export type DashboardMediaKind =
  'Image' | 'StateImage' | 'ImageCarousel' | 'ContentSwitch' | 'EmbeddedPage'

export type DashboardMediaLocale = 'zh-CN' | 'en-US'
export type DashboardMediaTheme = 'dark' | 'light'
export type DashboardMediaFit = 'Contain' | 'Cover'

export type LocalizedLabel = string | Partial<Record<DashboardMediaLocale, string>>

export interface StateImageCase {
  value: unknown
  assetId: string
  label?: LocalizedLabel
}

export interface ContentSwitchItem {
  id: string
  label: LocalizedLabel
  /** Published dashboard component ids. A parent may render these through contentRegistry. */
  children?: readonly string[]
}

export type DashboardContentRegistry = Readonly<Record<string, Component>>

export type EmbeddedPageSandboxPolicy = 'studio-readonly' | 'studio-isolated'

export interface EmbeddedPageParameter {
  target: string
  source: unknown
}

export type EmbeddedPageParameterMap =
  Readonly<Record<string, unknown>> | readonly EmbeddedPageParameter[]

export interface EmbeddedPageDefinition {
  key: string
  title: LocalizedLabel
  description?: LocalizedLabel
  /** A fixed policy is part of the code registration and cannot be supplied by a URL. */
  sandboxPolicyKey: EmbeddedPageSandboxPolicy
  /** The implementation is code registered by the host application. */
  component?: Component
}

export type MediaAssetRegistry = Readonly<Record<string, string>>

export interface MediaAssetDiagnostic {
  code: 'MEDIA_ASSET_NOT_REGISTERED' | 'MEDIA_ASSET_UNSAFE_SOURCE'
  assetId: string
  message: Record<DashboardMediaLocale, string>
}

export interface MediaAssetResolution {
  assetId: string
  src: string | null
  diagnostic?: MediaAssetDiagnostic
}

export interface DashboardMediaVisualProps {
  kind: DashboardMediaKind
  assetId?: string
  fit?: DashboardMediaFit
  alt?: string
  state?: unknown
  cases?: readonly StateImageCase[]
  defaultAssetId?: string
  assetIds?: readonly string[]
  intervalSeconds?: number
  pauseOnHover?: boolean
  items?: readonly ContentSwitchItem[]
  activeItemId?: string
  autoSwitchSeconds?: number
  contentRegistry?: DashboardContentRegistry
  registeredPageKey?: string
  parameterMap?: EmbeddedPageParameterMap
  sandboxPolicyKey?: EmbeddedPageSandboxPolicy
  embeddedPages?: readonly EmbeddedPageDefinition[]
  assetRegistry?: MediaAssetRegistry
  locale?: DashboardMediaLocale
  theme?: DashboardMediaTheme
}

const svgDataUri = (svg: string): string =>
  `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg.trim())}`

/**
 * Small original SVGs keep the offline dashboard samples useful without opening a
 * remote-resource or arbitrary-HTML path. Additions should remain data-image URIs.
 */
export const DASHBOARD_MEDIA_ASSETS: MediaAssetRegistry = Object.freeze({
  'dashboard.factory-overview': svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 180">
      <defs><linearGradient id="factory-bg" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#103b55"/><stop offset="1" stop-color="#081c2c"/></linearGradient></defs>
      <rect width="320" height="180" rx="18" fill="url(#factory-bg)"/>
      <path d="M18 142h284" stroke="#6fd6dc" stroke-width="4" opacity=".55"/>
      <path d="M28 138V82l48-26v82m0 0V70l42-24v92m0 0V86l54-30v82m0 0V72l48-21v97" fill="#1a5770" stroke="#9ae8e6" stroke-width="3"/>
      <circle cx="246" cy="36" r="12" fill="#ffc857"/><path d="M246 15v-8m0 50v8m21-29h8m-58 0h-8" stroke="#ffc857" stroke-width="3" stroke-linecap="round"/>
      <text x="28" y="32" fill="#e7fbff" font-family="sans-serif" font-size="15" font-weight="700">FACTORY / 生产总览</text>
    </svg>
  `),
  'dashboard.machine-running': svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 260 180">
      <rect width="260" height="180" rx="18" fill="#092a34"/>
      <rect x="50" y="43" width="160" height="92" rx="14" fill="#154e5a" stroke="#65d7d0" stroke-width="4"/>
      <circle cx="130" cy="89" r="29" fill="#092a34" stroke="#8de7d6" stroke-width="4"/>
      <path d="M130 72v18l13 8" stroke="#8de7d6" stroke-width="5" stroke-linecap="round" fill="none"/>
      <circle cx="187" cy="30" r="9" fill="#37df8a"/><path d="M30 147h200" stroke="#65d7d0" stroke-width="3" opacity=".45"/>
      <text x="130" y="163" text-anchor="middle" fill="#d7fff5" font-family="sans-serif" font-size="14">RUNNING / 运行</text>
    </svg>
  `),
  'dashboard.machine-stopped': svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 260 180">
      <rect width="260" height="180" rx="18" fill="#172b3b"/>
      <rect x="50" y="43" width="160" height="92" rx="14" fill="#294458" stroke="#7aa0b2" stroke-width="4"/>
      <path d="M111 66v46m38-46v46" stroke="#d7edf6" stroke-width="8" stroke-linecap="round"/>
      <circle cx="187" cy="30" r="9" fill="#9aaeba"/><path d="M30 147h200" stroke="#7aa0b2" stroke-width="3" opacity=".45"/>
      <text x="130" y="163" text-anchor="middle" fill="#d7edf6" font-family="sans-serif" font-size="14">STOPPED / 停止</text>
    </svg>
  `),
  'dashboard.machine-alarm': svgDataUri(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 260 180">
      <rect width="260" height="180" rx="18" fill="#3b2026"/>
      <rect x="50" y="43" width="160" height="92" rx="14" fill="#713543" stroke="#ff9b7e" stroke-width="4"/>
      <path d="m130 62 30 55h-60z" fill="#ffca74"/><path d="M130 80v19m0 11v2" stroke="#5e2937" stroke-width="6" stroke-linecap="round"/>
      <circle cx="187" cy="30" r="9" fill="#ff806b"/><path d="M30 147h200" stroke="#ff9b7e" stroke-width="3" opacity=".5"/>
      <text x="130" y="163" text-anchor="middle" fill="#ffe4d3" font-family="sans-serif" font-size="14">ALARM / 告警</text>
    </svg>
  `),
})

export const DASHBOARD_MEDIA_ASSET_IDS = Object.freeze({
  factoryOverview: 'dashboard.factory-overview',
  machineRunning: 'dashboard.machine-running',
  machineStopped: 'dashboard.machine-stopped',
  machineAlarm: 'dashboard.machine-alarm',
})

export const DEFAULT_IMAGE_ASSET_ID = DASHBOARD_MEDIA_ASSET_IDS.factoryOverview
export const DEFAULT_STATE_IMAGE_ASSET_ID = DASHBOARD_MEDIA_ASSET_IDS.machineStopped
export const DEFAULT_CAROUSEL_ASSET_IDS = Object.freeze([
  DASHBOARD_MEDIA_ASSET_IDS.factoryOverview,
  DASHBOARD_MEDIA_ASSET_IDS.machineRunning,
  DASHBOARD_MEDIA_ASSET_IDS.machineAlarm,
])

const safeDataImagePattern = /^data:image\/(?:svg\+xml|png|jpeg|jpg|webp|gif);/iu

export function isSafeRegisteredAssetSource(source: unknown): source is string {
  return typeof source === 'string' && safeDataImagePattern.test(source) && !/[\r\n]/u.test(source)
}

export function resolveMediaAsset(
  assetId: unknown,
  registry: MediaAssetRegistry = DASHBOARD_MEDIA_ASSETS,
): MediaAssetResolution {
  const normalizedId = typeof assetId === 'string' ? assetId.trim() : ''
  const source = normalizedId ? registry[normalizedId] : undefined
  if (source && isSafeRegisteredAssetSource(source)) {
    return { assetId: normalizedId, src: source }
  }

  const code = source ? 'MEDIA_ASSET_UNSAFE_SOURCE' : 'MEDIA_ASSET_NOT_REGISTERED'
  return {
    assetId: normalizedId,
    src: null,
    diagnostic: {
      code,
      assetId: normalizedId,
      message: {
        'zh-CN': source
          ? `素材来源不安全：${normalizedId || '未提供 assetId'}`
          : `素材未登记：${normalizedId || '未提供 assetId'}`,
        'en-US': source
          ? `Unsafe media source: ${normalizedId || 'assetId is missing'}`
          : `Media asset is not registered: ${normalizedId || 'assetId is missing'}`,
      },
    },
  }
}

export function localizedLabel(
  label: LocalizedLabel | undefined,
  locale: DashboardMediaLocale,
): string {
  if (typeof label === 'string') return label
  return label?.[locale] ?? label?.['zh-CN'] ?? label?.['en-US'] ?? ''
}

export const DEFAULT_EMBEDDED_PAGE_REGISTRY: readonly EmbeddedPageDefinition[] = Object.freeze([])
