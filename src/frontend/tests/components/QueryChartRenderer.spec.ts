import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it } from 'vitest'
import ChartRenderer from '@/components/studio/shared/ChartRenderer.vue'
it('plots both signs within the chart bounds with nonzero negative bars', () => {
  const wrapper = mount(ChartRenderer, {
    props: {
      config: { chartType: 'Bar', xField: 'x', series: [{ valueField: 'y' }] },
      rows: [
        { x: 'positive', y: 10 },
        { x: 'negative', y: -10 },
      ],
    },
    global: { plugins: [createPinia()], stubs: { ElAlert: true } },
  })
  const bars = wrapper.findAll('rect')
  expect(bars).toHaveLength(2)
  for (const bar of bars) {
    expect(Number(bar.attributes('height'))).toBe(95)
    expect(Number(bar.attributes('y'))).toBeGreaterThanOrEqual(40)
  }
  expect(Number(bars[1]!.attributes('y'))).toBe(135)
  wrapper.unmount()
})
