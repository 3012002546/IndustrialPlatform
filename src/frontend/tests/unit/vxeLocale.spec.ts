import { VxeUI } from 'vxe-table'
import { describe, expect, it } from 'vitest'

import { setVxeLocale } from '@/localization/vxeLocale'

describe('vxeLocale', () => {
  it('uses the platform locale for VXE empty, sorting and pager copy', () => {
    setVxeLocale('en-US')
    expect(VxeUI.getLanguage()).toBe('en-US')
    expect(VxeUI.getI18n('vxe.table.emptyText')).toBe('No data yet')
    expect(VxeUI.getI18n('vxe.table.sortAsc')).toBe('Ascending order: lowest to highest')
    expect(VxeUI.getI18n('vxe.pager.total', [3])).toBe('Total 3 records')

    setVxeLocale('zh-CN')
    expect(VxeUI.getLanguage()).toBe('zh-CN')
    expect(VxeUI.getI18n('vxe.table.emptyText')).toBe('暂无数据')
    expect(VxeUI.getI18n('vxe.table.sortAsc')).toBe('升序：最低到最高')
    expect(VxeUI.getI18n('vxe.pager.total', [3])).toBe('共 3 条记录')
  })
})
