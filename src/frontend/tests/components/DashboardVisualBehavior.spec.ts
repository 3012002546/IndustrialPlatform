import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import type { Json } from '@/api/studio'
import type { DashboardComponent } from '@/pages/pc/studio/dashboard/model'
import photoresist from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist.dashboard.json'
import batchDetail from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist-batch-detail.dashboard.json'

const mounted: VueWrapper[] = []

function component(id: string, type: string, props: Json = {}): DashboardComponent {
  return {
    id,
    type,
    name: id,
    registryVersion: '1',
    x: 0,
    y: 0,
    width: 600,
    height: 300,
    rotation: 0,
    zIndex: 0,
    visible: true,
    locked: false,
    props,
    bindings: [],
    events: [],
    children: [],
  }
}

function runtime(components: DashboardComponent[], data: Json = {}): Json {
  return {
    document: {
      schemaVersion: 1,
      canvas: { width: 1920, height: 1080, fit: 'Actual', background: '#081525' },
      components,
      instances: [{ instanceId: 'shared', staleAfterSeconds: 30 }],
      parameters: [],
      navigation: [],
    },
    instances: { shared: { data } },
    transport: 'Connected',
  }
}

function mountRuntime(components: DashboardComponent[], data: Json = {}) {
  const wrapper = mount(DashboardRuntimeView, {
    props: { runtime: runtime(components, data) },
    global: { plugins: [createPinia()], stubs: { 'el-empty': true } },
  })
  mounted.push(wrapper)
  return wrapper
}

function withRows(item: DashboardComponent): DashboardComponent {
  return {
    ...item,
    bindings: [
      {
        property: 'rows',
        source: { from: 'Instance', instanceId: 'shared', path: ['rows'] },
      },
    ],
  }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-10-22T08:09:10Z'))
  setActivePinia(createPinia())
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('Dashboard runtime visual behavior', () => {
  it('keeps all rows in continuous mode, preserves paging and emits one row navigation', async () => {
    const table = withRows(
      component('scrolling', 'Table', {
        scrollMode: 'Continuous',
        pageSeconds: 8,
        rowHeight: 40,
        rowKeyPath: ['code'],
        columns: [{ field: 'code', label: 'Batch' }],
      }),
    )
    table.events = ['detail']
    const data = { rows: Array.from({ length: 30 }, (_, code) => ({ code })) }
    const wrapper = mountRuntime([table], data)
    expect(wrapper.findAll('tbody tr')).toHaveLength(30)
    await wrapper.findAll('tbody td')[29]!.trigger('click')
    expect(wrapper.emitted('navigate')).toHaveLength(1)
    expect(wrapper.emitted('navigate')![0]![1]).toEqual({ code: 29 })
    table.props.scrollMode = 'Page'
    await wrapper.setProps({ runtime: runtime([table], data) })
    expect(wrapper.findAll('tbody tr')).toHaveLength(5)
    table.props.scrollMode = 'None'
    await wrapper.setProps({ runtime: runtime([table], data) })
    expect(wrapper.findAll('tbody tr')).toHaveLength(30)
  })
  it('offers only visible bound rows for opt-in screen preloading', async () => {
    const table = withRows(component('batches', 'Table', { rowHeight: 40, rowKeyPath: ['code'] }))
    const data = runtime([table], { rows: Array.from({ length: 30 }, (_, code) => ({ code })) })
    data.document.navigation = [{ id: 'detail', componentId: 'batches', event: 'RowClick' }]
    const page = mount(DashboardRuntimeView, {
      props: { runtime: data },
      global: { stubs: { 'el-empty': true } },
    })
    mounted.push(page)
    expect(page.emitted('preload')).toBeUndefined()
    await page.setProps({ preloadNavigation: true })
    const offered = page.emitted('preload')!.at(-1)![0] as { row: Json }[]
    expect(offered.map((item) => item.row.code)).toEqual([0, 1, 2, 3, 4])
    expect(page.findAll('tbody tr')).toHaveLength(offered.length)
  })
  it('selects batch details from the sole batch number parameter and clears unmatched details', async () => {
    const wrapper = mount(DashboardRuntimeView, {
      props: {
        runtime: { document: batchDetail.document, instances: {} },
        parameters: { batchNo: 'PR-DEMO-104' },
      },
      global: { plugins: [createPinia()], stubs: { 'el-empty': true } },
    })
    mounted.push(wrapper)
    expect(batchDetail.document.parameters.map((parameter) => parameter.name)).toEqual(['batchNo'])
    expect(wrapper.text()).toContain('PR-DEMO-104')
    expect(wrapper.text()).toContain('LAB-01')
    expect(wrapper.text()).toContain('待检')
    expect(wrapper.findAll('tbody tr')).toHaveLength(8)
    expect(wrapper.text()).not.toContain('HOLD-101')
    await wrapper.setProps({ parameters: { batchNo: 'PR-DEMO-105' } })
    expect(wrapper.text()).toContain('PR-DEMO-105')
    expect(wrapper.text()).toContain('HOLD-101')
    expect(wrapper.text()).toContain('复核中')
    expect(wrapper.text()).not.toContain('PR-DEMO-104')
    await wrapper.setProps({ parameters: { batchNo: 'unknown' } })
    expect(wrapper.findAll('tbody tr')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('HOLD-101')
    await wrapper.setProps({ parameters: {} })
    expect(wrapper.findAll('tbody tr')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('PR-DEMO-104')
  })
  it('applies percentage column widths, row height and alignment to the shared table renderer', () => {
    const table = withRows(
      component('table', 'Table', {
        presentation: 'Table',
        rowHeight: 40,
        columns: [
          { field: 'code', label: '编号', widthPercent: 25, align: 'center' },
          { field: 'value', label: '数值', widthPercent: 75 },
        ],
      }),
    )
    const wrapper = mountRuntime([table], { rows: [{ code: 'A', value: 0 }] })
    expect(wrapper.get('table').attributes('style')).toContain('table-layout: fixed')
    expect(wrapper.findAll('col').map((col) => col.attributes('style'))).toEqual([
      'width: 25%; text-align: center;',
      'width: 75%;',
    ])
    expect(wrapper.get('tbody td span').attributes('style')).toContain('height: 39px')
    expect(wrapper.get('tbody td').attributes('style')).toContain('text-align: center')
    expect(wrapper.get('tbody tr').text()).toContain('0')
  })
  it('uses configured series, threshold and heatmap colors instead of fixed placeholders', () => {
    const series = Array.from({ length: 5 }, (_, index) => ({
      name: `S${index}`,
      valueField: 'value',
      color: '#123456',
    }))
    const line = withRows(component('line', 'Chart', { kind: 'Line', series }))
    const heat = withRows(
      component('heat', 'Chart', {
        kind: 'Heatmap',
        colorStops: [
          { value: 0, color: '#00ff00' },
          { value: 50, color: '#ff0000' },
        ],
      }),
    )
    const thermometer = component('temp', 'Thermometer', {
      min: 0,
      max: 100,
      thresholds: [{ min: 0, max: 100, color: '#ff0000' }],
    })
    thermometer.bindings = [{ property: 'value', source: { from: 'Constant', value: 75 } }]
    const wrapper = mountRuntime([line, heat, thermometer], { rows: [{ value: 75 }] })
    expect(wrapper.findAll('.dashboard-chart-svg polyline')).toHaveLength(5)
    expect(wrapper.get('.dashboard-chart-svg polyline').attributes('stroke')).toBe('#123456')
    expect(wrapper.get('.dashboard-heatmap span').attributes('style')).toContain('rgb(255, 0, 0)')
    expect(wrapper.get('.dashboard-meter-fill').attributes('style')).toContain('rgb(255, 0, 0)')
  })
  it('renders the portable photoresist sample with constant table/chart rows and zero tank level', () => {
    const wrapper = mountRuntime(photoresist.document.components as DashboardComponent[])
    expect(wrapper.findAll('.dashboard-table-wrap tbody tr')).toHaveLength(10)
    expect(wrapper.text()).toContain('PR-DEMO-101')
    expect(wrapper.text()).toContain('颗粒与黏度')
    expect(wrapper.text()).toContain('0%')
    expect(wrapper.find('.dashboard-chart-svg polyline').exists()).toBe(true)
    expect(wrapper.get('.dashboard-chart-svg').text()).toContain('实际')
    expect(wrapper.get('.dashboard-chart-svg').text()).toContain('计划')
    expect(
      wrapper.get('.dashboard-component-Clock .dashboard-number').attributes('style'),
    ).toContain('24px')
    expect(wrapper.find('.dashboard-text').attributes('style')).toContain('font-size: 32px')
  })
  it('positions scatter points by configured xField and yField, including equal x values', async () => {
    const chart = withRows(
      component('scatter', 'Chart', { kind: 'Scatter', xField: 'pressure', yField: 'temperature' }),
    )
    const wrapper = mountRuntime([chart], {
      rows: [
        { pressure: 10, temperature: 20 },
        { pressure: 10, temperature: 80 },
        { pressure: 90, temperature: 20 },
      ],
    })
    const points = wrapper.findAll('.dashboard-chart-svg circle')
    expect(points, 'Scatter must render points, not grouped bars').toHaveLength(3)
    const x = points.map((point) => Number(point.attributes('cx')))
    const y = points.map((point) => Number(point.attributes('cy')))
    expect(x[0]).toBeCloseTo(x[1]!)
    expect(x[2]).toBeGreaterThan(x[0]!)
    expect(y[1]).toBeLessThan(y[0]!)
    expect(y[2]).toBeCloseTo(y[0]!)

    await wrapper.setProps({
      runtime: runtime([chart], {
        rows: [
          { pressure: 90, temperature: 20 },
          { pressure: 10, temperature: 80 },
          { pressure: 10, temperature: 20 },
        ],
      }),
    })
    const reordered = wrapper.findAll('.dashboard-chart-svg circle')
    expect(Number(reordered[0]!.attributes('cx'))).toBeCloseTo(x[2]!)
    expect(Number(reordered[2]!.attributes('cx'))).toBeCloseTo(x[0]!)
  })

  it('stacks each category series vertically with a shared x coordinate and proportional heights', () => {
    const chart = withRows(
      component('stack', 'Chart', {
        kind: 'StackedBar',
        categoryField: 'line',
        xField: 'line',
        series: [
          { name: 'Good', valueField: 'good' },
          { name: 'Rejected', valueField: 'rejected' },
        ],
      }),
    )
    const wrapper = mountRuntime([chart], {
      rows: [
        { line: 'L01', good: 20, rejected: 10 },
        { line: 'L02', good: 10, rejected: 5 },
      ],
    })
    const rectangles = wrapper.findAll('.dashboard-chart-svg rect')
    expect(rectangles).toHaveLength(4)
    const bars = rectangles.map((rectangle) => ({
      x: Number(rectangle.attributes('x')),
      y: Number(rectangle.attributes('y')),
      width: Number(rectangle.attributes('width')),
      height: Number(rectangle.attributes('height')),
    }))
    for (const offset of [0, 2]) {
      const segments = bars.slice(offset, offset + 2).sort((a, b) => a.y - b.y)
      expect(segments[0]!.x, 'Stacked series must not become side-by-side bars').toBeCloseTo(
        segments[1]!.x,
      )
      expect(segments[0]!.width).toBeCloseTo(segments[1]!.width)
      expect(segments[0]!.y + segments[0]!.height).toBeCloseTo(segments[1]!.y)
    }
    expect(bars[0]!.height / bars[1]!.height).toBeCloseTo(2)
    expect(bars[0]!.x).not.toBe(bars[2]!.x)
  })

  it.each(['Table', 'List'])(
    '%s Page mode advances actual rows at pageSeconds and clears its timer',
    async (presentation) => {
      const table = withRows(
        component('paged-rows', 'Table', {
          presentation,
          columns: [{ field: 'label', label: 'Line' }],
          rowKeyPath: ['id'],
          scrollMode: 'Page',
          pageSeconds: 3,
        }),
      )
      const wrapper = mountRuntime([table], {
        rows: Array.from({ length: 24 }, (_, index) => ({
          id: index + 1,
          label: `LINE-${String(index + 1).padStart(2, '0')}`,
        })),
      })
      const renderedRows = () => wrapper.findAll('tbody tr').map((row) => row.text())
      const initialRows = renderedRows()
      expect(initialRows.length).toBeGreaterThan(0)
      expect(initialRows.length).toBeLessThan(24)
      expect(initialRows).toContain('LINE-01')

      await vi.advanceTimersByTimeAsync(2999)
      expect(renderedRows()).toEqual(initialRows)
      await vi.advanceTimersByTimeAsync(1)
      await nextTick()
      expect(renderedRows(), 'Page mode must advance beyond the first fixed slice').not.toEqual(
        initialRows,
      )
      expect(renderedRows()).not.toContain('LINE-01')

      wrapper.unmount()
      expect(vi.getTimerCount()).toBe(0)
    },
  )

  it('updates Clock every second and applies its configured time zone and pattern', async () => {
    const clock = component('clock', 'Clock', {
      pattern: 'HH:mm:ss',
      timeZone: 'Asia/Taipei',
      showDate: false,
    })
    const wrapper = mountRuntime([clock])
    expect(wrapper.get('.dashboard-component-Clock').text()).toContain('16:09:10')

    await vi.advanceTimersByTimeAsync(2000)
    await nextTick()
    expect(wrapper.get('.dashboard-component-Clock').text()).toContain('16:09:12')
    expect(wrapper.get('.dashboard-component-Clock').text()).not.toContain('16:09:10')

    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('switches configured child content within the current dashboard instead of only changing tab labels', async () => {
    const switcher = component('switch', 'ContentSwitch', {
      items: [
        { id: 'overview', label: 'Overview', children: ['overview-text'] },
        { id: 'detail', label: 'Detail', children: ['detail-text'] },
      ],
      activeItemId: 'overview',
      autoSwitchSeconds: 3,
    })
    const overview = component('overview-text', 'Text', { text: 'ACTUAL OVERVIEW CONTENT' })
    const detail = component('detail-text', 'Text', { text: 'ACTUAL DETAIL CONTENT' })
    const wrapper = mountRuntime([switcher, overview, detail])
    expect(wrapper.get('[role="tabpanel"]').text()).toContain('ACTUAL OVERVIEW CONTENT')
    expect(wrapper.get('[role="tabpanel"]').text()).not.toContain('ACTUAL DETAIL CONTENT')
    expect(
      wrapper
        .findAll('.dashboard-text')
        .filter((text) => text.isVisible())
        .map((text) => text.text()),
    ).toEqual(['ACTUAL OVERVIEW CONTENT'])

    await wrapper.get('[role="tablist"]').findAll('[role="tab"]')[1]!.trigger('click')
    expect(wrapper.get('[role="tabpanel"]').text()).toContain('ACTUAL DETAIL CONTENT')
    expect(wrapper.get('[role="tabpanel"]').text()).not.toContain('ACTUAL OVERVIEW CONTENT')

    await vi.advanceTimersByTimeAsync(3000)
    await nextTick()
    expect(wrapper.get('[role="tabpanel"]').text()).toContain('ACTUAL OVERVIEW CONTENT')
    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('resolves Tank level, temperature and state independently from one instance and preserves zero', async () => {
    const tank = component('tank', 'Tank', { min: 0, max: 100, unit: '%' })
    tank.bindings = ['level', 'temperature', 'state'].map((property) => ({
      property,
      source: { from: 'Instance', instanceId: 'shared', path: ['tank', property] },
    }))
    const wrapper = mountRuntime([tank], {
      tank: { level: 0, temperature: 42.5, state: 'Stopped' },
    })
    const tankView = () => wrapper.get('.dashboard-component-Tank')
    expect(tankView().text()).toContain('0%')
    expect(tankView().text()).toContain('42.5')
    expect(tankView().text()).toContain('Stopped')
    const fills = () =>
      tankView()
        .findAll('rect')
        .filter((rectangle) => rectangle.attributes('opacity') === '0.78')
    expect(fills()).toHaveLength(1)
    expect(Number(fills()[0]!.attributes('height'))).toBe(0)

    await wrapper.setProps({
      runtime: runtime([tank], { tank: { level: 75, temperature: 0, state: 'Running' } }),
    })
    expect(tankView().text()).toContain('75%')
    expect(tankView().text()).toMatch(/(?:温度|Temperature)\s*0/)
    expect(tankView().text()).toContain('Running')
    expect(tankView().text()).not.toContain('42.5')
    expect(Number(fills()[0]!.attributes('height'))).toBeGreaterThan(0)
  })
})
