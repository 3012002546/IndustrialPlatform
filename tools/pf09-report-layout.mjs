// Browser-side measurement in unscaled millimetres. Geometry is converted to
// ordinary table flow so long cells can still use the shared row fragmenter.
export function reportPaperGeometry(paper = {}) {
  const dimensions = { A4: [210, 297], A3: [297, 420], Letter: [215.9, 279.4], Custom: [Number(paper.widthMm), Number(paper.heightMm)] }[paper.size || 'A4']
  if (!dimensions) throw new Error('REPORT_PAPER_INVALID')
  const [widthMm, heightMm] = String(paper.orientation).toLowerCase() === 'landscape' ? [...dimensions].reverse() : dimensions
  const margin = Object.fromEntries(['top', 'right', 'bottom', 'left'].map((key) => [key, Number(paper.margin?.[key] ?? paper.marginMm ?? 15)]))
  const headerHeight = Number(paper.headerHeightMm ?? 15); const footerHeight = Number(paper.footerHeightMm ?? 10)
  const bodyHeight = heightMm - margin.top - margin.bottom - headerHeight - footerHeight
  const bodyWidth = widthMm - margin.left - margin.right
  if (![widthMm, heightMm, ...Object.values(margin), headerHeight, footerHeight].every((value) => Number.isFinite(value) && value >= 0) || bodyHeight <= 10 || bodyWidth <= 10 || widthMm > 2000 || heightMm > 2000) throw new Error('REPORT_PAPER_INVALID')
  return { widthMm, heightMm, margin, headerHeight, footerHeight, bodyHeight, bodyWidth }
}

export function layoutReportBands(root, maxBodyHeightPx = Infinity) {
  const px = 96 / 25.4
  const diagnostics = []
  for (const band of root.querySelectorAll('[data-report-band]')) {
    const metadata = JSON.parse(band.dataset.reportBand)
    // Paged collects explicit breaks from stylesheet declarations, not inline styles.
    if (metadata.breakBefore === 'Page') band.classList.add('report-break-before-page')
    const controls = [...band.children].filter((node) => node.hasAttribute('data-report-control')).map((node) => {
      const definition = JSON.parse(node.dataset.reportControl)
      const x = Number(definition.xMm || 0); const y = Number(definition.yMm || 0)
      const width = Number(definition.widthMm); const minimum = Number(definition.heightMm || 0)
      if (![x, y, width, minimum].every(Number.isFinite) || x < 0 || y < 0 || width <= 0 || minimum < 0) throw new Error('REPORT_LAYOUT_GEOMETRY_INVALID')
      node.style.boxSizing = 'border-box'; node.style.width = `${width}mm`; node.style.minHeight = '0'
      if (['Text', 'Field'].includes(definition.type)) { node.style.whiteSpace = 'pre-wrap'; node.style.overflowWrap = 'anywhere' }
      if (definition.type === 'Line') { node.style.borderTop ||= '0.2mm solid black'; node.style.height = `${minimum}mm` }
      if (definition.type === 'Box') { node.style.border ||= '0.2mm solid black'; node.style.height = `${minimum}mm` }
      const natural = node.getBoundingClientRect().height / px
      const vertical = definition.resolvedStyle?.verticalAlign
      if (minimum > natural && ['Middle','Bottom'].includes(vertical)) node.style.paddingTop = `${parseFloat(getComputedStyle(node).paddingTop) + (minimum - natural) * px * (vertical === 'Middle' ? .5 : 1)}px`
      node.style.minHeight = `${minimum}mm`
      const height = Math.max(minimum, natural)
      if (definition.keepTogether === true && definition.type !== 'Html') {
        if (height * px <= maxBodyHeightPx) node.style.breakInside = 'avoid'
        else diagnostics.push({ code: 'REPORT_KEEP_TOGETHER_RELEASED', controlId: definition.id })
      }
      if (definition.canGrow === false && height > minimum + 0.5) throw new Error('REPORT_LAYOUT_FIXED_CONTROL_OVERFLOW')
      return { node, definition, x, y, width, minimum, height, top: y, visited: false, visiting: false }
    })
    const byId = new Map(controls.map((control) => [control.definition.id, control]))
    const visit = (control) => {
      if (control.visited) return
      if (control.visiting) throw new Error('REPORT_LAYOUT_FLOW_CYCLE')
      control.visiting = true
      for (const id of control.definition.flow?.afterControlIds || []) {
        const before = byId.get(id)
        if (!before) throw new Error('REPORT_LAYOUT_FLOW_TARGET_MISSING')
        visit(before)
        const gap = control.y - before.y - before.minimum
        if (gap < -0.01) throw new Error('REPORT_LAYOUT_FLOW_NEGATIVE_GAP')
        control.top = Math.max(control.top, before.top + before.height + gap)
      }
      control.visiting = false; control.visited = true
    }
    controls.forEach(visit)
    const body = controls.filter((control) => control.definition.flow?.mode !== 'Overlay')
    for (let index = 0; index < body.length; index++) {
      const a = body[index]
      for (const b of body.slice(index + 1)) {
        if (Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x) > 0.1 && Math.min(a.top + a.height, b.top + b.height) - Math.max(a.top, b.top) > 0.1) throw new Error(`REPORT_LAYOUT_OVERLAP:${a.definition.id}:${b.definition.id}`)
      }
    }
    const width = band.getBoundingClientRect().width / px
    const table = document.createElement('table')
    table.className = 'report-layout-table'
    table.style.cssText = 'width:100%;table-layout:fixed;border-collapse:collapse;border:0;margin:0'
    const edges = [...new Set([0, width, ...body.flatMap((control) => [control.x, control.x + control.width])])].sort((a, b) => a - b)
    if (edges[0] < 0 || edges.at(-1) > width + 0.5) throw new Error('REPORT_LAYOUT_WIDTH_OVERFLOW')
    const colgroup = document.createElement('colgroup')
    for (let index = 1; index < edges.length; index++) {
      const col = document.createElement('col'); col.style.width = `${edges[index] - edges[index - 1]}mm`; colgroup.append(col)
    }
    table.append(colgroup)
    const tbody = document.createElement('tbody'); table.append(tbody)
    const rows = new Map()
    for (const control of body) {
      const key = Math.round(control.top * 100) / 100
      if (!rows.has(key)) rows.set(key, [])
      rows.get(key).push(control)
    }
    let bottom = 0
    const spacer = (height) => {
      if (height <= 0.1) return
      const tr = tbody.insertRow(); const td = tr.insertCell(); td.colSpan = edges.length - 1
      td.style.cssText = `height:${height}mm;padding:0;border:0;line-height:0`
    }
    for (const [top, items] of [...rows.entries()].sort((a, b) => a[0] - b[0])) {
      // An offset mosaic requires real row spans, rather than silently changing
      // coordinates. Fail explicitly until that geometry has its own gate.
      if (top < bottom - 0.1) throw new Error('REPORT_LAYOUT_OFFSET_MOSAIC_UNSUPPORTED')
      spacer(top - bottom)
      const tr = tbody.insertRow()
      let column = 0
      for (const control of items.sort((a, b) => a.x - b.x)) {
        const first = edges.indexOf(control.x); const last = edges.indexOf(control.x + control.width)
        if (first > column) { const gap = tr.insertCell(); gap.colSpan = first - column; gap.style.cssText = 'padding:0;border:0' }
        const td = tr.insertCell(); td.colSpan = last - first; td.style.cssText = 'padding:0;border:0;vertical-align:top'; td.append(control.node)
        column = last
      }
      if (column < edges.length - 1) { const gap = tr.insertCell(); gap.colSpan = edges.length - 1 - column; gap.style.cssText = 'padding:0;border:0' }
      bottom = Math.max(...items.map((control) => control.top + control.height))
    }
    spacer(Number(metadata.heightMm || 0) - bottom)
    for (const decoration of controls.filter((control) => control.definition.flow?.mode === 'Overlay')) {
      if (!['Line', 'Box'].includes(decoration.definition.type)) throw new Error('REPORT_LAYOUT_OVERLAY_CONTENT_DENIED')
      decoration.node.style.cssText += `;position:absolute;left:${decoration.x}mm;top:${decoration.y}mm`
    }
    band.append(table)
    if (metadata.kind === 'PageHeader' || metadata.kind === 'PageFooter') {
      // Running elements otherwise shrink in the margin box, shifting xMm offsets.
      band.style.width = `${width}mm`
      band.dataset.reportRepeat = 'true'
      band.classList.add(metadata.kind === 'PageHeader' ? 'report-page-header' : 'report-page-footer')
    }
  }
  // Measure the following band only after all of its controls have been laid out.
  // break-after alone can split between the nested layout table and its first row.
  for (const band of [...root.querySelectorAll('[data-report-band]')].reverse()) {
    const metadata = JSON.parse(band.dataset.reportBand)
    if (!metadata.keepWithNext || !['ReportHeader', 'GroupHeader'].includes(metadata.kind)) continue
    const next = band.nextElementSibling
    if (!next || next.classList.contains('report-break-before-page')) continue
    let first = next
    const controls = next.querySelectorAll('[data-report-control]')
    const table = controls.length === 1 ? controls[0].querySelector(':scope > table[data-report-template="true"]') : null
    const records = table ? [...table.tBodies].filter((body) => body.dataset.reportHeader !== 'true') : []
    if (records.length > 1) {
      // A Collection control may contain the entire report: retain only its
      // first record with the title, never move the whole table to a new page.
      first = next.cloneNode(true)
      const firstTable = first.querySelector('table[data-report-template="true"]')
      const firstRecords = [...firstTable.tBodies].filter((body) => body.dataset.reportHeader !== 'true')
      for (const body of firstRecords.slice(1)) body.remove()
      firstTable.tFoot?.remove()
      next.before(first)
    }
    const height = band.getBoundingClientRect().height + first.getBoundingClientRect().height
    if (height <= maxBodyHeightPx) {
      if (first !== next) {
        records[0].remove()
        table.tHead?.remove()
        table.querySelector(':scope > tbody[data-report-header="true"]')?.remove()
      }
      const group = document.createElement('div')
      group.className = 'report-keep-group'; group.style.breakInside = 'avoid'
      band.before(group); group.append(band, first)
    } else {
      if (first !== next) first.remove()
      band.classList.add('report-keep-with-next')
      diagnostics.push({ code: 'REPORT_KEEP_WITH_NEXT_RELEASED', instanceKey: metadata.instanceKey })
    }
  }
  return diagnostics
}
