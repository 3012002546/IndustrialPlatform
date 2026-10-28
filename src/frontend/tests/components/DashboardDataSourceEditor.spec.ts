import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import { describe, expect, it } from 'vitest'
import DashboardConstantEditor from '@/components/studio/dashboard/DashboardConstantEditor.vue'
import DashboardDataSourceEditor from '@/components/studio/dashboard/DashboardDataSourceEditor.vue'
import type { StudioResource } from '@/api/studio'

describe('dashboard source configuration', () => {
  it('preserves typed constants including zero, false and null and rejects malformed JSON', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(DashboardConstantEditor, {
      props: { modelValue: { value: 0, enabled: false, optional: null } },
      global: { plugins: [ElementPlus] },
    })
    await wrapper.get('.el-switch').trigger('click')
    await flushPromises()
    await wrapper.get('textarea').setValue('{"value":0,"enabled":false,"optional":null}')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认')!
      .trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([
      { value: 0, enabled: false, optional: null },
    ])
    await wrapper.get('textarea').setValue('{broken')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认')!
      .trigger('click')
    expect(wrapper.emitted('update:modelValue')).toHaveLength(1)
    expect(wrapper.get('[role="alert"]').text()).toContain('常量格式无效')
    wrapper.unmount()
  })

  it('offers only API datasets for a component API and exposes an independent interval', async () => {
    setActivePinia(createPinia())
    const datasets = [
      { nId: 'sql', name: 'SQL', publishedRevisionNId: 's1' },
      { nId: 'api', name: '接口数据', publishedRevisionNId: 'a1' },
    ] as StudioResource[]
    const wrapper = mount(DashboardDataSourceEditor, {
      props: {
        modelValue: { instanceId: 'own-api', sourceType: 'Api', refreshSeconds: 7 },
        datasets,
        contracts: { 'sql/s1': { query: { kind: 'Sql' } }, 'api/a1': { query: { kind: 'Api' } } },
        instances: [],
        pageParameters: [],
        apiOnly: true,
        canManageDatasets: true,
      },
      global: { plugins: [ElementPlus] },
    })
    // The API-only list uses the same registered contract as runtime execution.
    const options = wrapper.findAllComponents({ name: 'ElOption' })
    expect(options.filter((option) => option.props('value') === 'sql')).toHaveLength(0)
    expect(options.some((option) => option.props('value') === 'api')).toBe(true)
    expect(wrapper.get<HTMLInputElement>('input[aria-label="读取间隔（秒）"]').element.value).toBe(
      '7',
    )
    expect(wrapper.text()).toContain('工业应用 → 数据集')
    expect(wrapper.text()).toContain('独立参数和读取周期')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '打开数据集管理')!
      .trigger('click')
    expect(wrapper.emitted('manageDatasets')).toHaveLength(1)
    wrapper.unmount()
  })

  it('explains that a shared API response serves multiple components with one instance request', () => {
    setActivePinia(createPinia())
    const wrapper = mount(DashboardDataSourceEditor, {
      props: {
        modelValue: { instanceId: 'shared', sourceType: 'Api', refreshSeconds: 5 },
        datasets: [],
        contracts: {},
        instances: [],
        pageParameters: [],
      },
      global: { plugins: [ElementPlus] },
    })
    expect(wrapper.text()).toContain('多个组件绑定同一实例的不同字段，每轮只请求一次')
    expect(wrapper.text()).toContain('发布')
    expect(wrapper.text()).not.toContain('打开数据集管理')
    wrapper.unmount()
  })
})
