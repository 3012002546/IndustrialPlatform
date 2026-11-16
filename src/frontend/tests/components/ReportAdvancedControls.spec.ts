import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { describe, expect, it } from 'vitest'
import type { Json } from '@/api/studio'
import ReportAdvancedEditor from '@/components/studio/report/ReportAdvancedEditor.vue'
import ReportAdvancedDesign from '@/components/studio/report/ReportAdvancedDesign.vue'
import {
  advancedControlDefaults,
  advancedControlTypes,
  advancedOptions,
  patchAdvancedControl,
  advancedPath,
} from '@/components/studio/report/advancedControls'
import { reportControls } from '@/components/studio/report/catalogue'
import { useLocalizationStore } from '@/stores/localizationStore'

const control = (type: string): Json => ({
  id: 'selected',
  type,
  xMm: 0,
  yMm: 0,
  widthMm: 80,
  heightMm: 10,
  styleId: 'body',
  flow: { mode: 'Follow', afterControlIds: [] },
  ...advancedControlDefaults(type, '文本'),
})
const global = () => ({ plugins: [createPinia(), ElementPlus] })

describe('advanced report control contracts', () => {
  it('registers every implemented control with independent editable defaults', () => {
    for (const type of advancedControlTypes) {
      expect(reportControls.some((c) => c.type === type)).toBe(true)
      const first = control(type),
        second = control(type)
      expect(first.advanced).toEqual(advancedOptions(type))
      first.advanced.changed = true
      expect(second.advanced).not.toHaveProperty('changed')
      if (type === 'Chart') {
        first.value.value[0].value = 900
        expect(second.value.value[0].value).toBe(1)
      }
    }
    expect(advancedControlDefaults('Table', '文本')).toEqual({})
    expect(advancedControlDefaults('Box', '文本')).toEqual({})
    expect(advancedControlDefaults('CharacterComb', 'Character comb').value.value).toBe('ABC123')
  })
  it('preserves binding, layout, and unrelated saved fields while editing advanced options', () => {
    const original: Json = {
      ...control('Chart'),
      value: { from: 'Row', path: ['series'], nullPolicy: 'Error' },
      custom: { retained: true },
    }
    const saved = JSON.stringify(original)
    const next = patchAdvancedControl(original, { kind: 'Line' })
    expect(JSON.stringify(original)).toBe(saved)
    expect(next.value).toEqual(original.value)
    expect(next.flow).toEqual(original.flow)
    expect(next.custom).toEqual({ retained: true })
    expect(next.advanced.series).toEqual(original.advanced.series)
    expect(next.advanced.kind).toBe('Line')
    expect(advancedPath(' measure . viscosity ')).toEqual(['measure', 'viscosity'])
  })
  it('does not upgrade an old line merely by displaying its editor', () => {
    const old = { id: 'old', type: 'Line', widthMm: 80, heightMm: 2 }
    const wrapper = mount(ReportAdvancedEditor, { props: { modelValue: old }, global: global() })
    try {
      expect(old).not.toHaveProperty('advanced')
      expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    } finally {
      wrapper.unmount()
    }
  })
  it('emits immutable chart field and series edits with Chinese choices', async () => {
    const original = control('Chart')
    const wrapper = mount(ReportAdvancedEditor, {
      props: { modelValue: original },
      global: global(),
    })
    try {
      await wrapper.get('input[aria-label="类别字段路径"]').setValue('operation.name')
      const next = wrapper.emitted('update:modelValue')!.at(-1)![0] as Json
      expect(next.advanced.categoryPath).toEqual(['operation', 'name'])
      expect(original.advanced.categoryPath).toEqual(['label'])
      await wrapper.setProps({ modelValue: next })
      await wrapper
        .findAll('button')
        .find((b) => b.text() === '添加系列')!
        .trigger('click')
      const withSeries = wrapper.emitted('update:modelValue')!.at(-1)![0] as Json
      expect(withSeries.advanced.series).toHaveLength(2)
      expect(withSeries.advanced.categoryPath).toEqual(['operation', 'name'])
      expect(wrapper.text()).toContain('XLSX 暂不支持')
      const select = wrapper.findComponent({ name: 'ElSelect' })
      select.vm.$emit('update:modelValue', 'Line')
      await flushPromises()
      expect((wrapper.emitted('update:modelValue')!.at(-1)![0] as Json).advanced.kind).toBe('Line')
    } finally {
      wrapper.unmount()
    }
  })
  it('switches property language and edits pivot totals without changing aggregation', async () => {
    const pinia = createPinia()
    const original = control('PivotTable')
    const wrapper = mount(ReportAdvancedEditor, {
      props: { modelValue: original },
      global: { plugins: [pinia, ElementPlus] },
    })
    try {
      expect(wrapper.text()).toContain('不是 Excel 原生透视对象')
      wrapper.findComponent({ name: 'ElCheckbox' }).vm.$emit('update:modelValue', false)
      const next = wrapper.emitted('update:modelValue')!.at(-1)![0] as Json
      expect(next.advanced.totals).toBe(false)
      expect(next.advanced.aggregate).toBe('Sum')
      expect(original.advanced.totals).toBe(true)
      useLocalizationStore(pinia).setLocale('en-US', null)
      await flushPromises()
      expect(wrapper.text()).toContain('Control settings')
      expect(wrapper.text()).toContain('not a native Excel pivot object')
    } finally {
      wrapper.unmount()
    }
  })
  it('keeps a grapheme in one character cell and never draws a fake barcode', () => {
    const comb = mount(ReportAdvancedDesign, {
      props: {
        control: { ...control('CharacterComb'), advanced: { cells: 3 } },
        caption: 'A👩‍🔬中',
        label: '字符格',
      },
      global: global(),
    })
    const barcode = mount(ReportAdvancedDesign, {
      props: { control: control('Barcode'), caption: 'CODE39', label: '条形码' },
      global: global(),
    })
    try {
      expect(comb.findAll('.design-comb-cell').map((c) => c.text())).toEqual(['A', '👩‍🔬', '中'])
      expect(barcode.text()).toContain('预览生成条码')
      expect(barcode.find('svg').exists()).toBe(false)
      expect(barcode.find('canvas').exists()).toBe(false)
    } finally {
      comb.unmount()
      barcode.unmount()
    }
  })
})
