import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it } from 'vitest'
import ReportMeasuredCanvas from '@/components/studio/report/ReportMeasuredCanvas.vue'
import {
  reportMillimetres,
  reportUnitValue,
  type ReportUnit,
} from '@/components/studio/report/geometry'

const sections = [
  'PageHeader',
  'ReportHeader',
  'GroupHeader',
  'Detail',
  'GroupFooter',
  'ReportFooter',
  'PageFooter',
].map((kind, index) => ({
  id: kind,
  kind,
  label: kind,
  name: kind,
  startMm: 15 + index * 10,
  heightMm: 10,
}))
const makeCanvas = () =>
  mount(ReportMeasuredCanvas, {
    props: {
      widthMm: 210,
      heightMm: 100,
      margin: { top: 15, right: 15, bottom: 15, left: 15 },
      unit: 'mm',
      zoom: 1,
      sections,
    },
    global: { plugins: [createPinia()] },
  })

it('identifies the selected band and exposes a resize handle for every band and margin', async () => {
  const wrapper = makeCanvas()
  try {
    for (const section of sections) {
      const rail = wrapper.get(`.report-section-rail[data-section-id="${section.id}"]`)
      await rail.trigger('click')
      expect(wrapper.emitted('selectSection')?.at(-1)).toEqual([section.id])
      await wrapper.setProps({ selectedSection: section.id })
      expect(rail.attributes('aria-pressed')).toBe('true')
      expect(wrapper.findAll('.report-section-rail.selected')).toHaveLength(1)
      const region = wrapper.get('.report-section-selection')
      expect(region.attributes('data-section-id')).toBe(section.id)
      expect(region.attributes('style')).toContain('width: 762px')
      expect(region.attributes('style')).toContain('height: 30px')
      await wrapper
        .get(`.report-section-resize[data-section-id="${section.id}"]`)
        .trigger('pointerdown')
      expect(wrapper.emitted('sectionPointer')?.at(-1)?.[0]).toBe(section.id)
    }
    for (const side of ['top', 'bottom']) {
      await wrapper
        .get(`.report-section-resize[data-section-id="${side}-margin"]`)
        .trigger('pointerdown')
      expect(wrapper.emitted('marginPointer')?.at(-1)?.[0]).toBe(side)
    }
    expect(wrapper.findAll('.report-section-resize')).toHaveLength(9)
  } finally {
    wrapper.unmount()
  }
})

it('converts all four margin labels and handles with the ruler without changing physical paper', async () => {
  const wrapper = makeCanvas()
  try {
    const before = wrapper.get('[data-margin="left"]').attributes('width')
    for (const unit of ['cm', 'in', 'px', 'mm'] as ReportUnit[]) {
      await wrapper.setProps({ unit })
      for (const side of ['top', 'right', 'bottom', 'left']) {
        expect(wrapper.get(`.report-margin-label[data-side="${side}"]`).text()).toContain(unit)
        const handle = wrapper.get(`.report-margin-handle[data-side="${side}"]`)
        expect(handle.attributes('aria-label')).toMatch(new RegExp(`${unit}$`))
        if (unit !== 'mm') expect(handle.attributes('title')).not.toContain('mm')
      }
      expect(wrapper.get('[data-margin="left"]').attributes('width')).toBe(before)
      expect(reportMillimetres(reportUnitValue(15, unit), unit)).toBeCloseTo(15, 2)
    }
    expect(reportMillimetres(1, 'in')).toBe(25.4)
    expect(reportMillimetres(96, 'px')).toBe(25.4)
  } finally {
    wrapper.unmount()
  }
})
