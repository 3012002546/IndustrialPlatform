import { h, defineComponent, nextTick } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

import DashboardMediaVisual from '@/components/studio/dashboard/DashboardMediaVisual.vue'
import {
  DASHBOARD_MEDIA_ASSET_IDS,
  DEFAULT_CAROUSEL_ASSET_IDS,
  type ContentSwitchItem,
  type EmbeddedPageDefinition,
} from '@/components/studio/dashboard/mediaAssets'

describe('DashboardMediaVisual', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('renders a registered image and reports unknown or unsafe assets', () => {
    const registered = mount(DashboardMediaVisual, {
      props: { kind: 'Image', assetId: DASHBOARD_MEDIA_ASSET_IDS.factoryOverview },
    })
    expect(registered.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.factoryOverview,
    )
    expect(registered.get('img').attributes('src')).toMatch(/^data:image\/svg\+xml;/u)

    const unknown = mount(DashboardMediaVisual, {
      props: { kind: 'Image', assetId: 'dashboard.not-registered', locale: 'en-US' },
    })
    expect(unknown.get('[role="alert"]').text()).toContain('Media asset is not registered')
    expect(unknown.get('[data-diagnostic-code]').attributes('data-diagnostic-code')).toBe(
      'MEDIA_ASSET_NOT_REGISTERED',
    )

    const unsafe = mount(DashboardMediaVisual, {
      props: {
        kind: 'Image',
        assetId: 'dashboard.external',
        assetRegistry: { 'dashboard.external': 'https://example.invalid/image.svg' },
      },
    })
    expect(unsafe.get('[data-diagnostic-code]').attributes('data-diagnostic-code')).toBe(
      'MEDIA_ASSET_UNSAFE_SOURCE',
    )
  })

  it('selects a state asset and uses the registered fallback for an unknown state', async () => {
    const wrapper = mount(DashboardMediaVisual, {
      props: {
        kind: 'StateImage',
        state: 'Running',
        cases: [
          { value: 'Running', assetId: DASHBOARD_MEDIA_ASSET_IDS.machineRunning },
          { value: 'Alarm', assetId: DASHBOARD_MEDIA_ASSET_IDS.machineAlarm },
        ],
        defaultAssetId: DASHBOARD_MEDIA_ASSET_IDS.machineStopped,
      },
    })

    expect(wrapper.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.machineRunning,
    )
    await wrapper.setProps({ state: 'Offline' })
    expect(wrapper.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.machineStopped,
    )
    expect(wrapper.get('[data-state-fallback]').attributes('data-state-fallback')).toBe('true')
    expect(wrapper.text()).toContain('未知状态')
  })

  it('switches carousel images, pauses on hover, and clears its timer on unmount', async () => {
    vi.useFakeTimers()
    const wrapper = mount(DashboardMediaVisual, {
      props: {
        kind: 'ImageCarousel',
        assetIds: DEFAULT_CAROUSEL_ASSET_IDS.slice(0, 2),
        intervalSeconds: 3,
        pauseOnHover: true,
      },
    })
    const carousel = wrapper.get('.dashboard-media-carousel')

    expect(carousel.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.factoryOverview,
    )
    await vi.advanceTimersByTimeAsync(3000)
    expect(carousel.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.machineRunning,
    )

    await carousel.trigger('mouseenter')
    await vi.advanceTimersByTimeAsync(6000)
    expect(carousel.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.machineRunning,
    )
    await carousel.trigger('mouseleave')
    await vi.advanceTimersByTimeAsync(3000)
    expect(carousel.get('img').attributes('data-asset-id')).toBe(
      DASHBOARD_MEDIA_ASSET_IDS.factoryOverview,
    )

    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('switches slot content from activeItemId and autoSwitchSeconds', async () => {
    vi.useFakeTimers()
    const items: readonly ContentSwitchItem[] = [
      {
        id: 'overview',
        label: { 'zh-CN': '总览', 'en-US': 'Overview' },
        children: ['overview-card'],
      },
      { id: 'quality', label: { 'zh-CN': '质量', 'en-US': 'Quality' }, children: ['quality-card'] },
    ]
    const wrapper = mount(DashboardMediaVisual, {
      props: { kind: 'ContentSwitch', items, activeItemId: 'overview', autoSwitchSeconds: 3 },
      slots: {
        default: ({ item }: { item: ContentSwitchItem }) =>
          h('span', { class: 'slot-content' }, item.id),
      },
    })

    expect(wrapper.get('.slot-content').text()).toBe('overview')
    await vi.advanceTimersByTimeAsync(3000)
    await nextTick()
    expect(wrapper.get('.slot-content').text()).toBe('quality')
    expect(wrapper.get('[aria-selected="true"]').text()).toBe('质量')

    await wrapper.get('button').trigger('click')
    expect(wrapper.get('.slot-content').text()).toBe('overview')
    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('renders only code-registered embedded pages with their fixed sandbox policy', () => {
    const RegisteredPage = defineComponent({
      props: {
        parameters: { type: Object, required: false },
        sandboxPolicyKey: { type: String, required: true },
      },
      setup(props) {
        return () =>
          h(
            'div',
            { class: 'registered-page' },
            `${props.parameters?.lineId ?? ''}:${props.sandboxPolicyKey}`,
          )
      },
    })
    const registry: readonly EmbeddedPageDefinition[] = [
      {
        key: 'line-summary',
        title: { 'zh-CN': '产线摘要', 'en-US': 'Line summary' },
        sandboxPolicyKey: 'studio-readonly',
        component: RegisteredPage,
      },
    ]
    const registered = mount(DashboardMediaVisual, {
      props: {
        kind: 'EmbeddedPage',
        embeddedPages: registry,
        registeredPageKey: 'line-summary',
        parameterMap: { lineId: 'L02' },
      },
    })
    expect(registered.get('.registered-page').text()).toBe('L02:studio-readonly')
    expect(registered.get('.dashboard-media-embedded-page').text()).toContain('沙盒策略')

    const unknown = mount(DashboardMediaVisual, {
      props: { kind: 'EmbeddedPage', registeredPageKey: 'https://example.invalid/page' },
    })
    expect(unknown.get('[role="alert"]').text()).toContain('嵌入页面未登记')
    expect(unknown.find('.registered-page').exists()).toBe(false)

    const mismatch = mount(DashboardMediaVisual, {
      props: {
        kind: 'EmbeddedPage',
        embeddedPages: registry,
        registeredPageKey: 'line-summary',
        sandboxPolicyKey: 'studio-isolated',
      },
    })
    expect(mismatch.get('[role="alert"]').text()).toContain('固定沙盒策略不匹配')
    expect(mismatch.find('.registered-page').exists()).toBe(false)
  })
})
