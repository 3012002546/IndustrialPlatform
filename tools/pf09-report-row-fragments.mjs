// Browser-side bounded preprocessing for rows taller than the printable body.
// Paged.js remains responsible for final page placement and repeated headers.
export function fragmentReportRows(root, maxHeightPx, prefix = '') {
  const diagnostics = []
  window.__reportCellSources ||= new Map()
  const cellText = (node) => { const copy = node.cloneNode(true); for (const header of copy.querySelectorAll('thead,[data-report-repeat="true"]')) header.remove(); return copy.textContent }
  let sequence = 0
  const headers = new Map()
  // Explicit visual continuation cells avoid the engine dropping a spanning
  // column when a nested table crosses a page. The logical span stays recorded.
  for (const table of [...root.querySelectorAll('table')].reverse()) {
    for (const section of [table.tHead, ...table.tBodies, table.tFoot].filter(Boolean)) {
      // A designed record is one template group. Keep native spans and vertical
      // alignment when that whole group fits a page; oversized groups still use
      // the existing continuation path below.
      if (table.dataset.reportTemplate === 'true' && section.getBoundingClientRect().height <= maxHeightPx) continue
      const active = new Map()
      for (const row of section.rows) {
        const originals = [...row.cells]
        const expanded = []
        let index = 0; let column = 0
        while (index < originals.length || [...active.keys()].some((key) => key >= column)) {
          if (column > 1000) throw new Error('REPORT_LIMIT: excessive table columns')
          const span = active.get(column)
          if (span) {
            const cell = span.template.cloneNode(false)
            cell.rowSpan = 1; cell.dataset.rowspanContinuation = 'true'
            cell.style.borderTopWidth = '0'
            if (span.remaining > 1) cell.style.borderBottomWidth = '0'
            expanded.push(cell)
            if (--span.remaining === 0) active.delete(column)
            column += cell.colSpan
          } else if (index < originals.length) {
            const cell = originals[index++]
            if (cell.rowSpan > 1) {
              const template = cell.cloneNode(false)
              active.set(column, { template, remaining: cell.rowSpan - 1 })
              cell.dataset.logicalRowspan = String(cell.rowSpan)
              cell.rowSpan = 1; cell.style.borderBottomWidth = '0'
            }
            expanded.push(cell); column += cell.colSpan
          } else throw new Error('REPORT_LAYOUT_INVALID: sparse spanning row')
        }
        row.replaceChildren(...expanded)
      }
    }
  }
  for (const table of root.querySelectorAll('table')) {
    table.dataset.reportTable = prefix + String(sequence++)
    if (table.tHead && table.dataset.reportRepeatHeader !== 'false') headers.set(table.dataset.reportTable, table.tHead.cloneNode(true))
  }
  const repeatHeaders = (fragment) => {
    for (const table of fragment.querySelectorAll('table[data-report-table]')) {
      if (!table.tHead && headers.has(table.dataset.reportTable)) {
        const header = headers.get(table.dataset.reportTable).cloneNode(true)
        header.dataset.reportRepeat = 'true'
        table.prepend(header)
      }
    }
  }
  const splitCell = (cell, limit) => {
    const width = cell.getBoundingClientRect().width
    const table = document.createElement('table')
    table.style.cssText = `position:absolute;left:0;top:0;width:${width}px;table-layout:fixed;visibility:hidden`
    const tr = table.insertRow()
    const probe = cell.cloneNode(false)
    probe.removeAttribute('rowspan'); probe.removeAttribute('colspan')
    tr.append(probe); root.append(table)
    const remainder = cell.cloneNode(true)
    remainder.removeAttribute('rowspan'); remainder.removeAttribute('colspan')
    const output = []
    const fits = (contents) => {
      probe.replaceChildren(contents.cloneNode(true))
      repeatHeaders(probe)
      return probe.getBoundingClientRect().height <= limit
    }
    const lines = (contents) => {
      probe.replaceChildren(contents.cloneNode(true)); const tops = new Set()
      const walker = document.createTreeWalker(probe, NodeFilter.SHOW_TEXT); let node
      while ((node = walker.nextNode())) { if (!node.textContent.trim() || node.parentElement.closest('thead')) continue; const range = document.createRange(); range.selectNodeContents(node); for (const rect of range.getClientRects()) if (rect.width > 0 && rect.height > 0) tops.add(Math.round(rect.top * 2) / 2) }
      return tops.size
    }
    try {
      // Whole legal blocks let the pager use the space left on the current
      // page. Page-sized row fragments would otherwise move as one large row.
      const children = [...remainder.children]
      if (children.length > 1 && [...remainder.childNodes].every((node) => node.nodeType !== Node.TEXT_NODE || !node.textContent.trim()) && children.every((child) => fits(child))) {
        return children.map((child) => child.outerHTML)
      }
      // Designed tables sit inside a control wrapper. Split the outer layout
      // cell at complete record groups before falling back to character ranges;
      // partial nested tables can escape Paged's continuation columns.
      const control = children.length === 1 && children[0].hasAttribute('data-report-control') ? children[0] : null
      const designed = control?.children.length === 1 && control.firstElementChild.matches('table[data-report-template="true"]') ? control.firstElementChild : null
      if (designed) {
        const header = designed.tHead || designed.querySelector(':scope > tbody[data-report-header="true"]')
        const groups = [...designed.tBodies].filter((body) => body !== header)
        const pieces = groups.map((body, index) => {
          const wrapper = control.cloneNode(false); wrapper.style.minHeight = '0'
          const table = designed.cloneNode(false)
          for (const colgroup of designed.querySelectorAll(':scope > colgroup')) table.append(colgroup.cloneNode(true))
          if (index === 0 && header) table.append(header.cloneNode(true))
          table.append(body.cloneNode(true)); wrapper.append(table)
          return wrapper
        })
        if (pieces.length > 1 && pieces.every((part) => fits(part))) return pieces.map((part) => part.outerHTML)
      }
      if (children.length === 1 && children[0].tagName === 'TABLE') {
        const nested = children[0]
        const rows = [...nested.tBodies].flatMap((body) => [...body.rows])
        const pieces = rows.map((row) => {
          const part = nested.cloneNode(false)
          for (const colgroup of nested.querySelectorAll(':scope > colgroup')) part.append(colgroup.cloneNode(true))
          if (nested.tHead) part.append(nested.tHead.cloneNode(true))
          const body = document.createElement('tbody'); body.append(row.cloneNode(true)); part.append(body)
          return part
        })
        if (pieces.length > 1 && pieces.every((part) => fits(part))) return pieces.map((part) => part.outerHTML)
      }
      for (let iteration = 0; iteration < 2000; iteration++) {
        const full = document.createRange(); full.selectNodeContents(remainder)
        if (fits(full.cloneContents())) { output.push(remainder.innerHTML); return output }
        const walker = document.createTreeWalker(remainder, NodeFilter.SHOW_TEXT)
        const positions = []
        let text
        while ((text = walker.nextNode())) {
          if (text.parentElement.closest('thead')) continue
          if (text.parentElement.closest('[data-token]')) {
            positions.push([text, text.length])
          } else {
            for (const segment of new Intl.Segmenter('zh', { granularity: 'grapheme' }).segment(text.data)) {
              positions.push([text, segment.index + segment.segment.length])
            }
          }
        }
        if (!positions.length) throw new Error('REPORT_LAYOUT_UNSPLITTABLE: non-text block exceeds body')
        let low = 0; let high = positions.length - 1; let best = -1
        while (low <= high) {
          const mid = (low + high) >>> 1
          const range = document.createRange()
          range.selectNodeContents(remainder)
          range.setEnd(...positions[mid])
          if (fits(range.cloneContents())) { best = mid; low = mid + 1 } else { high = mid - 1 }
        }
        if (best < 0) throw new Error('REPORT_LAYOUT_UNSPLITTABLE: no legal fragment fits body')
        const paragraph = positions[best][0].parentElement.closest('p,li,pre,blockquote')
        if (paragraph && remainder.contains(paragraph)) {
          const piece = (index, before) => { const range = document.createRange(); range.selectNodeContents(paragraph); if (before) range.setEnd(...positions[index]); else range.setStart(...positions[index]); const wrapper = paragraph.cloneNode(false); wrapper.append(range.cloneContents()); return wrapper }
          if (piece(best, true).textContent.trim() && piece(best, false).textContent.trim()) {
            const first = positions.findIndex(([node]) => paragraph.contains(node)); let low = first, high = best, legal = -1
            while (low <= high) { const middle = (low + high) >>> 1; if (lines(piece(middle, false)) >= 2) { if (lines(piece(middle, true)) >= 2) legal = middle; low = middle + 1 } else high = middle - 1 }
            if (legal < 0) legal = first - 1 // Move the entire short paragraph when no 2 + 2 split is legal.
            if (legal < 0) throw new Error('REPORT_LAYOUT_UNSPLITTABLE: paragraph cannot keep two lines on each side')
            if (legal !== best) diagnostics.push({ code: 'REPORT_WIDOW_ORPHAN_ADJUSTED', minimumLines: 2 })
            best = legal
          }
        }
        const prefix = document.createRange(); prefix.selectNodeContents(remainder); prefix.setEnd(...positions[best])
        const suffix = document.createRange(); suffix.selectNodeContents(remainder); suffix.setStart(...positions[best])
        const first = document.createElement('div'); first.append(prefix.cloneContents()); repeatHeaders(first)
        const next = suffix.cloneContents()
        if (first.textContent === '' || first.innerHTML === remainder.innerHTML) throw new Error('REPORT_LAYOUT_UNSTABLE: no progress')
        output.push(first.innerHTML)
        remainder.replaceChildren(next)
        repeatHeaders(remainder)
      }
      throw new Error('REPORT_LIMIT: excessive cell fragments')
    } finally { table.remove() }
  }
  // Innermost tables first; a cloned nested table keeps its source identity.
  for (const table of [...root.querySelectorAll('table')].reverse()) {
    if (!table.isConnected) continue
    const headerHeight = table.tHead?.getBoundingClientRect().height || 0
    const limit = maxHeightPx - headerHeight - 4
    if (limit <= 0) throw new Error('REPORT_LAYOUT_UNSPLITTABLE: header consumes page')
    for (const row of [...table.rows]) {
      if (row.closest('table') !== table || row.parentElement.tagName === 'THEAD' || row.getBoundingClientRect().height <= limit) continue
      const cells = [...row.cells]
      const fragments = cells.map((cell) => splitCell(cell, limit))
      const count = Math.max(...fragments.map((items) => items.length))
      if (count < 2) throw new Error('REPORT_LAYOUT_UNSPLITTABLE: row cannot be safely fragmented')
      const rows = document.createDocumentFragment()
      const rowKey = `${prefix}row-${sequence++}`
      cells.forEach((cell, index) => {
        const combined = document.createElement('div'); combined.innerHTML = fragments[index].join('')
        if (cellText(combined) !== cellText(cell)) throw new Error('REPORT_CELL_RANGE_MISMATCH: preprocessing changed cell body')
        window.__reportCellSources.set(`${rowKey}-${index}`, cellText(cell))
      })
      for (let index = 0; index < count; index++) {
        const part = row.cloneNode(false)
        part.dataset.sourceRow = rowKey; part.dataset.fragmentIndex = String(index)
        cells.forEach((cell, cellIndex) => {
          const parts = fragments[cellIndex]
          if (cell.rowSpan > 1 && parts.length === 1 && index > 0) return
          const continued = cell.cloneNode(false)
          if (cell.rowSpan > 1) continued.rowSpan = parts.length === 1 ? cell.rowSpan + count - 1 : index === count - 1 ? cell.rowSpan : 1
          continued.innerHTML = parts[index] || ''
          continued.dataset.sourceCell = `${rowKey}-${cellIndex}`
          if (index > 0) { continued.style.borderTopWidth = '0'; continued.style.paddingTop = '0' }
          if (index < count - 1) { continued.style.borderBottomWidth = '0'; continued.style.paddingBottom = '0' }
          part.append(continued)
        })
        rows.append(part)
      }
      row.replaceWith(rows)
      diagnostics.push({ code: 'REPORT_ROW_FRAGMENTED', sourceRow: rowKey, fragments: count })
    }
  }
  return diagnostics
}

export function installReportTableHeaders(Paged) {
  class ReportTableHeaders extends Paged.Handler {
    afterParsed(parsed) {
      this.headers = new Map([...parsed.querySelectorAll('table[data-report-table]')]
        .filter((table) => table.tHead && table.dataset.reportRepeatHeader !== 'false')
        .map((table) => [table.dataset.reportTable, table.tHead.cloneNode(true)]))
    }
    layout(rendered) {
      const seen = new Set()
      for (const table of rendered.querySelectorAll('table[data-report-table]')) {
        if (seen.has(table.dataset.reportTable)) continue
        seen.add(table.dataset.reportTable)
        if (table.tHead || !this.headers.has(table.dataset.reportTable)) continue
        const header = this.headers.get(table.dataset.reportTable).cloneNode(true)
        header.dataset.reportRepeat = 'true'
        for (const element of [header, ...header.querySelectorAll('*')]) {
          element.removeAttribute('data-ref'); element.removeAttribute('id')
        }
        table.prepend(header)
      }
    }
  }
  Paged.registerHandlers(ReportTableHeaders)
}
