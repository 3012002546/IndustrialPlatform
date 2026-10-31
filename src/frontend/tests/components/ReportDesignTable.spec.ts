import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent } from 'vue'
import { expect, it } from 'vitest'
import ReportDesignTable from '@/components/studio/report/ReportDesignTable.vue'

it('edits cell text on double click, commits once, and cancels without changing bindings', async () => {
  const wrapper = mount(ReportDesignTable, {
    attachTo: document.body,
    props: {
      table: {
        columns: [{ id: 'a', label: 'Header', widthMm: 40, value: { from: 'Row', path: ['a'] } }],
      },
      active: true,
      row: 0,
      columnId: 'a',
      caption: () => '[Row:a]',
      columnStyle: () => ({}),
    },
    global: { plugins: [createPinia()] },
  })
  try {
    await wrapper.get('td').trigger('dblclick')
    expect(document.activeElement).toBe(wrapper.get('input').element)
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('edit-content')).toBeUndefined()
    await wrapper.get('td').trigger('dblclick')
    await wrapper.get('input').setValue('cancelled')
    await wrapper.get('input').trigger('keydown', { key: 'Escape' })
    expect(wrapper.emitted('edit-content')).toBeUndefined()
    await wrapper.get('td').trigger('dblclick')
    await wrapper.get('input').setValue('Note')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('edit-content')).toEqual([[0, 'a', 'Note']])
    await wrapper.get('th').trigger('dblclick')
    await wrapper.get('input').setValue('New header')
    await wrapper.get('input').trigger('blur')
    expect(wrapper.emitted('edit-content')!.at(-1)).toEqual([-1, 'a', 'New header'])
  } finally {
    wrapper.unmount()
  }
})

it('renders independent cell styles and spans and exposes range and resize actions', async () => {
  const wrapper = mount(ReportDesignTable, {
    attachTo: document.body,
    props: {
      table: {
        columns: [
          { id: 'a', label: 'A', widthMm: 30, header: { colSpan: 2 } },
          { id: 'b', label: 'B', widthMm: 20 },
        ],
        rows: [
          { id: 'one', heightMm: 12, cells: [{ columnId: 'a', rowSpan: 2 }, { columnId: 'b' }] },
          { id: 'two', heightMm: 8, cells: [{ columnId: 'a' }, { columnId: 'b' }] },
        ],
      },
      active: true,
      row: 0,
      columnId: 'a',
      caption: () => 'value',
      columnStyle: () => ({}),
      cellStyle: (row, column) =>
        row === 0 && column.id === 'a'
          ? { backgroundColor: '#ddeeff', textAlign: 'center', border: '1px dashed black' }
          : {},
      range: { top: 0, bottom: 1, left: 0, right: 1 },
    },
    global: { plugins: [createPinia()] },
  })
  try {
    expect(wrapper.findAll('th')).toHaveLength(1)
    expect(wrapper.get('th').attributes('colspan')).toBe('2')
    expect(wrapper.findAll('td')).toHaveLength(3)
    expect(wrapper.findAll('td')[0]!.attributes('rowspan')).toBe('2')
    expect(wrapper.findAll('td')[0]!.attributes('style')).toContain(
      'background-color: rgb(221, 238, 255)',
    )
    expect(wrapper.findAll('td')[1]!.attributes('style') ?? '').not.toContain('background-color')
    await wrapper.findAll('td')[1]!.trigger('click', { shiftKey: true })
    expect(wrapper.emitted('select')!.at(-1)).toEqual([0, 'b', true])
    await wrapper.get('.report-column-resize').trigger('pointerdown')
    expect(wrapper.emitted('resize-column')![0]![1]).toBe(0)
    await wrapper.findAll('.report-row-resize')[1]!.trigger('pointerdown')
    expect(wrapper.emitted('resize-row')![0]![1]).toBe(0)
    await wrapper.findAll('td')[0]!.trigger('contextmenu')
    const buttons = document.querySelectorAll<HTMLButtonElement>('.report-cell-menu button')
    expect(buttons[6]!.disabled).toBe(false)
    expect(buttons[7]!.disabled).toBe(false)
    buttons[7]!.click()
    expect(wrapper.emitted('split')).toHaveLength(1)
  } finally {
    wrapper.unmount()
  }
})

it('selects a cell, opens the context menu, emits insertion and closes on Escape', async () => {
  const wrapper = mount(ReportDesignTable, {
    attachTo: document.body,
    props: {
      table: {
        headerMode: 'OncePerParent',
        columns: [{ id: 'a', label: 'A', widthMm: 40, value: { from: 'Row' } }],
      },
      active: true,
      row: 0,
      columnId: 'a',
      caption: () => 'value',
      columnStyle: () => ({}),
    },
    global: { plugins: [createPinia()] },
  })
  try {
    await wrapper.get('td').trigger('click')
    expect(wrapper.emitted('select')![0]).toEqual([0, 'a', false])
    expect(wrapper.findAll('.cell-selected')).toHaveLength(1)
    await wrapper.get('td').trigger('contextmenu', { clientX: 100, clientY: 100 })
    const buttons = document.querySelectorAll<HTMLButtonElement>('.report-cell-menu button')
    expect(buttons).toHaveLength(8)
    expect(buttons[2]!.disabled).toBe(true)
    expect(buttons[5]!.disabled).toBe(true)
    buttons[1]!.click()
    expect(wrapper.emitted('action')![0]).toEqual(['rowAfter'])
    await wrapper.vm.$nextTick()
    expect(document.querySelector('.report-cell-menu')).toBeNull()
    await wrapper.get('th').trigger('contextmenu', { clientX: 100, clientY: 100 })
    expect(wrapper.emitted('select')!.at(-1)).toEqual([-1, 'a'])
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await wrapper.vm.$nextTick()
    expect(document.querySelector('.report-cell-menu')).toBeNull()
  } finally {
    wrapper.unmount()
  }
})

it('keeps cell selection separate from table dragging and whole-table selection', async () => {
  const wrapper = mount(
    defineComponent({
      components: { ReportDesignTable },
      data: () => ({ pointers: 0, activeCell: false, selectedCells: 0 }),
      template: `<div @pointerdown="pointers++"><ReportDesignTable
      :table="{columns:[{id:'a',label:'A',widthMm:40,value:{from:'Row'}}]}"
      :active="activeCell" :row="0" column-id="a" :caption="() => 'value'" :column-style="() => ({})"
      @select="activeCell = true; selectedCells++" @select-table="activeCell = false" /></div>`,
    }),
    { global: { plugins: [createPinia()] } },
  )
  try {
    await wrapper.get('td').trigger('pointerdown')
    await wrapper.get('td').trigger('click')
    expect(wrapper.vm.pointers).toBe(0)
    expect(wrapper.vm.selectedCells).toBe(1)
    expect(wrapper.findAll('.cell-selected')).toHaveLength(1)
    const table = wrapper.getComponent(ReportDesignTable)
    await wrapper.get('.report-table-handle').trigger('pointerdown')
    expect(table.emitted('move-table')).toHaveLength(1)
    await wrapper.get('.report-table-handle').trigger('click')
    expect(wrapper.findAll('.cell-selected')).toHaveLength(0)
    await wrapper.get('td').trigger('keydown', { key: ' ' })
    expect(wrapper.vm.selectedCells).toBe(2)
    await wrapper.get('td').trigger('keydown', { key: 'Escape' })
    expect(wrapper.findAll('.cell-selected')).toHaveLength(0)
  } finally {
    wrapper.unmount()
  }
})
