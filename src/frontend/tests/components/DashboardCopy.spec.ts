import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import {
  dashboardCategoryLabels,
  dashboardComponentLabels,
  dashboardMessages,
  useDashboardText,
} from '@/components/studio/dashboard/dashboardCopy'
import { useLocalizationStore } from '@/stores/localizationStore'

describe('dashboard copy', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('responds to zh-CN and en-US locale changes through the existing store', () => {
    const localization = useLocalizationStore()
    const text = useDashboardText()

    localization.setLocale('zh-CN', null)
    expect(text.value('save')).toBe('保存')
    expect(text.value.componentLabel('Chart.Line')).toBe('折线 / 多序列')
    expect(text.value.categoryLabel('图表')).toBe('图表')
    expect(text.value.propertyLabel('wrap')).toBe('自动换行')
    expect(text.value.propertyLabel('fontSizePx')).toBe('字号（像素）')
    expect(text.value.optionLabel('RowClick')).toBe('行点击')

    localization.setLocale('en-US', null)
    expect(text.value('save')).toBe('Save')
    expect(text.value.componentLabel('Chart.Line')).toBe('Line / multi-series')
    expect(text.value.categoryLabel('图表')).toBe('Charts')
    expect(text.value.propertyLabel('wrap')).toBe('Wrap text')
    expect(text.value.propertyLabel('fontSizePx')).toBe('Font size (px)')
    expect(text.value.optionLabel('RowClick')).toBe('Row click')
  })

  it('keeps unknown user text, NIds, business data and contract enums unchanged', () => {
    const text = useDashboardText()
    useLocalizationStore().setLocale('en-US', null)

    expect(text.value('L01')).toBe('L01')
    expect(text.value('Contain')).toBe('Contain')
    expect(text.value('Running')).toBe('Running')
    expect(text.value('工单 2026-001')).toBe('工单 2026-001')
    expect(text.value('previewNavigation', { target: 'target-dashboard-01' })).toBe(
      'Preview navigation: target-dashboard-01',
    )
    expect(text.value.componentLabel('user-defined-component')).toBe('user-defined-component')
    expect(text.value.categoryLabel('custom-category')).toBe('custom-category')
    expect(text.value.propertyLabel('custom-property')).toBe('custom-property')
    expect(text.value.optionLabel('CUSTOM-STATUS')).toBe('CUSTOM-STATUS')
  })

  it('covers critical error and runtime quality messages in both locales', () => {
    const text = useDashboardText()
    const localization = useLocalizationStore()

    localization.setLocale('zh-CN', null)
    expect(text.value('requiredParameter')).toBe('请补齐必填参数')
    expect(text.value('operationFailed')).toContain('输入已保留')
    expect(text.value('sourceDataStale')).toBe('来源数据 Stale')
    expect(text.value('sourceTimeUnknown')).toBe('来源时间未知')
    expect(text.value('transportDisconnectedLastValue')).toContain('最后成功值')

    localization.setLocale('en-US', null)
    expect(text.value('requiredParameter')).toBe('Complete the required parameters')
    expect(text.value('operationFailed')).toContain('Input retained')
    expect(text.value('sourceDataStale')).toBe('Source data is stale')
    expect(text.value('sourceTimeUnknown')).toBe('Source time unknown')
    expect(text.value('transportDisconnectedLastValue')).toContain('last successful value')
  })

  it('maps all 35 registered component entry labels and four categories', () => {
    expect(Object.keys(dashboardComponentLabels)).toHaveLength(35)
    expect(Object.keys(dashboardCategoryLabels)).toEqual(['basic', 'chart', 'industrial', 'data'])
    expect(Object.values(dashboardComponentLabels).every((entry) => entry.zh && entry.en)).toBe(
      true,
    )
    expect(Object.values(dashboardComponentLabels).map((entry) => entry.category)).toEqual([
      ...Array(12).fill('basic'),
      ...Array(10).fill('chart'),
      ...Array(10).fill('industrial'),
      ...Array(3).fill('data'),
    ])
    expect(Object.keys(dashboardMessages['zh-CN'])).toEqual(
      expect.arrayContaining(['back', 'preview', 'requiredParameter', 'sourceDataStale']),
    )
    expect(Object.keys(dashboardMessages['en-US']).sort()).toEqual(
      Object.keys(dashboardMessages['zh-CN']).sort(),
    )
  })
})
