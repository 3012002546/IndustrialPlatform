import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import DashboardStructuredEditor from '@/components/studio/dashboard/DashboardStructuredEditor.vue'
import {
  newStructuredItem,
  structuredError,
  structuredFields,
  structuredKinds,
} from '@/components/studio/dashboard/structuredFields'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { Json } from '@/api/studio'

let view: VueWrapper, value: unknown[]
function open(kind: string, initial: unknown[], extra: Json = {}) {
  value = structuredClone(initial)
  view = mount(DashboardStructuredEditor, {
    attachTo: document.body,
    props: {
      kind,
      modelValue: value,
      ...extra,
      'onUpdate:modelValue': (next: unknown[]) => {
        value = next
        void view.setProps({ modelValue: next })
      },
    },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  return view
}
async function click(text: string) {
  const button = view
    .findAll('button')
    .find((item) => item.text().trim() === text && item.isVisible())!
  expect(button, text).toBeDefined()
  await button.trigger('click')
  await flushPromises()
}
beforeEach(() => setActivePinia(createPinia()))
afterEach(() => {
  view?.unmount()
  document.body.innerHTML = ''
})
describe('structured dashboard configuration', () => {
  it('edits percentage columns, retains extensions and prevents invalid JSON or totals', async () => {
    open('columns', [
      {
        field: 'code',
        label: { 'zh-CN': '编号', 'en-US': 'Code' },
        widthPercent: 40,
        extension: 0,
      },
      { field: 'value', label: '值', widthPercent: 60 },
    ])
    const width = view.get('input[aria-label="列宽（%）"]')
    await width.setValue('50')
    expect(view.text()).toContain('合计不能超过100')
    expect((value[0] as Json).widthPercent).toBe(40)
    await width.setValue('30')
    await view.get('input[aria-label="列标题"]').setValue('新编号')
    expect((value[0] as Json).label).toEqual({ 'zh-CN': '新编号', 'en-US': 'Code' })
    await click('下移')
    expect((value[1] as Json).field).toBe('code')
    expect((value[1] as Json).extension).toBe(0)
    await view.get('input[value="json"]').setValue(true)
    await view.get('textarea').setValue('[{"field":"bad","widthPercent":101}]')
    expect(value).toHaveLength(2)
    await view.get('input[value="visual"]').setValue(true)
    expect(view.find('textarea').exists()).toBe(true)
    await view.get('textarea').setValue('[{"field":"valid","label":"有效","widthPercent":100}]')
    await view.get('input[value="visual"]').setValue(true)
    expect(view.find('textarea').exists()).toBe(false)
    await click('移除')
    expect(value).toEqual([])
    await click('添加项')
    expect(value).toHaveLength(1)
    await view.get('input[value="json"]').setValue(true)
    await view.get('textarea').setValue('[{"field":"field1"},{"field":"field3"}]')
    await view.get('input[value="visual"]').setValue(true)
    await click('添加项')
    expect((value[2] as Json).field).toBe('field4')
  })
  it('preserves typed constants and supports adding rows and fields without writing JSON', async () => {
    open('rows', [{ batch: 'R1', volume: 0, passed: false, meta: { nested: 1 } }], {
      columns: [
        { field: 'batch', label: '批次' },
        { field: 'volume', label: '体积' },
      ],
    })
    expect(view.text()).toContain('1 · R1')
    expect(view.findAll('.el-collapse-item')).toHaveLength(1)
    expect((view.get('input[aria-label="体积"]').element as HTMLInputElement).value).toBe('0')
    await view.get('[role="switch"]').trigger('click')
    expect((value[0] as Json).passed).toBe(true)
    expect((value[0] as Json).meta).toEqual({ nested: 1 })
    await click('添加项')
    expect((value[1] as Json).volume).toBe(0)
    expect(view.findAll('.el-collapse-item')).toHaveLength(1)
    expect((value[1] as Json).passed).toBe(false)
    await view.get('input[aria-label="新字段名"]').setValue('note')
    await click('添加字段')
    expect(value.every((item) => (item as Json).note === '')).toBe(true)
  })
  it.each(structuredKinds.filter((kind) => kind !== 'rows'))(
    'round trips %s and retains its object shape',
    async (kind) => {
      const initial = [newStructuredItem(kind, 0, kind === 'cases' ? 'StateImage' : 'Text')]
      open(kind, initial, { componentType: kind === 'cases' ? 'StateImage' : 'Text' })
      expect(structuredError(kind, value)).toBe('')
      await view.get('input[value="json"]').setValue(true)
      expect(JSON.parse((view.get('textarea').element as HTMLTextAreaElement).value)).toEqual(
        initial,
      )
      await view.get('input[value="visual"]').setValue(true)
      expect(value).toEqual(initial)
      await click('添加项')
      expect(value).toHaveLength(2)
      expect(structuredFields(kind, 'StateImage').length).toBeGreaterThan(0)
    },
  )
  it('retains boolean state values and follows system language', async () => {
    open('cases', [{ value: false, color: '#ff0000', label: '停机', custom: 'keep' }], {
      componentType: 'StatusLamp',
    })
    await view.get('[role="switch"]').trigger('click')
    expect((value[0] as Json).value).toBe(true)
    expect((value[0] as Json).custom).toBe('keep')
    useLocalizationStore().setLocale('en-US', null)
    await flushPromises()
    expect(view.text()).toContain('Visual')
    expect(view.text()).toContain('Add item')
    expect((value[0] as Json).label).toBe('停机')
  })
})
