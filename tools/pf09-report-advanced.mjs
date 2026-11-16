// Trusted browser-side drawing, injected only after the inert HTML validation.
// Code 39 encoding table: Copyright 2007 ZXing authors, Apache-2.0.
// Adapted table only; drawing and report integration are independent implementations.
// Source: https://github.com/zxing/zxing/blob/master/core/src/main/java/com/google/zxing/oned/Code39Reader.java
// License and attribution: third-party/zxing-LICENSE.txt, third-party/zxing-NOTICE.txt.
export async function renderReportAdvanced(source, format) {
  const fail = (code) => { throw new Error(code) }
  const element = (tag, text) => { const node = document.createElement(tag); if (text !== undefined) node.textContent = String(text ?? ''); return node }
  const color = (value, fallback) => /^#[\da-f]{6}$/i.test(value || '') ? value : fallback
  const palette = ['#2563eb', '#dc2626', '#15803d', '#9333ea', '#c2410c', '#0e7490', '#a21caf', '#4d7c0f']
  const read = (row, path) => path.reduce((value, key) => value?.[key], row)
  const number = (value) => {
    if (value === null) return null
    const parsed = typeof value === 'string' && /^[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(value.trim()) ? Number(value) : value
    return typeof parsed === 'number' && Number.isFinite(parsed) ? parsed : fail('REPORT_GRAPHIC_VALUE_INVALID')
  }
  let pixels = 0
  for (const holder of source.querySelectorAll('[data-report-advanced]')) {
    const { type, options: o, value, widthMm, heightMm } = JSON.parse(holder.dataset.reportAdvanced)
    const width = Number(widthMm), height = Number(heightMm)
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) fail('REPORT_GRAPHIC_SIZE_INVALID')
    if (format === 'Xlsx' && ['Shape', 'Barcode', 'Chart', 'Gauge', 'Sparkline'].includes(type)) fail('REPORT_XLSX_GRAPHIC_UNSUPPORTED')
    delete holder.dataset.reportAdvanced
    holder.replaceChildren()
    holder.style.width = width + 'mm'
    if (type === 'CharacterComb') {
      const cells = Number(o.cells)
      const characters = [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(String(value ?? ''))].map(part => part.segment)
      if (!Number.isInteger(cells) || cells < 1 || cells > 128 || characters.length > cells) fail('REPORT_CHARACTER_OVERFLOW')
      holder.style.cssText += `;display:flex;height:${height}mm;box-sizing:border-box`
      const canvas = document.createElement('canvas'), context = canvas.getContext('2d'), style = getComputedStyle(holder)
      context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
      if (characters.some(text => context.measureText(text).width > (width / cells - .6) * 96 / 25.4) || parseFloat(style.fontSize) > (height - .6) * 96 / 25.4) fail('REPORT_CHARACTER_OVERFLOW')
      for (let i = 0; i < cells; i++) {
        const cell = element('span', characters[i] ?? '')
        cell.style.cssText = 'flex:1;min-width:0;border:.2mm solid currentColor;display:flex;align-items:center;justify-content:center;box-sizing:border-box;white-space:pre'
        holder.append(cell)
      }
      continue
    }
    if (type === 'PivotTable') {
      if (!Array.isArray(value?.columns) || !Array.isArray(value?.rows)) fail('REPORT_PIVOT_VALUE_INVALID')
      const table = element('table'); table.dataset.reportTemplate = 'true'; table.dataset.reportRepeatHeader = 'true'
      table.dataset.reportHeaderGroup = (holder.closest('[data-report-marker]')?.dataset.reportMarker || '') + '-pivot'
      const head = table.createTHead().insertRow()
      const header = (text) => head.append(element('th', text))
      header(''); value.columns.forEach(header); if (o.totals) header('合计 / Total')
      const record = () => { const body = table.createTBody(); body.style.breakInside = 'avoid'; return body.insertRow() }
      for (const row of value.rows) {
        const tr = record(); tr.append(element('td', row.key)); row.values.forEach(v => tr.append(element('td', v)))
        if (o.totals) tr.append(element('td', row.total))
      }
      if (o.totals && value.rows.length) {
        // Keep the total as a single ordinary final row: tfoot is not repeated on every page.
        const tr = record(); tr.append(element('td', '合计 / Total')); value.totals.forEach(v => tr.append(element('td', v))); tr.append(element('td', value.grandTotal))
      }
      table.style.borderCollapse = 'collapse'
      for (const cell of table.querySelectorAll('th,td')) cell.style.cssText = 'border:.2mm solid currentColor;padding:1mm;overflow-wrap:anywhere'
      holder.replaceWith(table)
      continue
    }
    const dpi = 300, canvas = document.createElement('canvas')
    canvas.width = Math.ceil(width * dpi / 25.4); canvas.height = Math.ceil(height * dpi / 25.4)
    pixels += canvas.width * canvas.height
    if (canvas.width * canvas.height > 8_000_000 || pixels > 80_000_000) fail('REPORT_GRAPHIC_PIXEL_LIMIT')
    const c = canvas.getContext('2d'), scale = dpi / 25.4
    c.scale(scale, scale)
    const computed = getComputedStyle(holder)
    const font = (mm = 2.7) => { c.font = `${computed.fontWeight} ${mm}px ${computed.fontFamily}` }
    font(); c.fillStyle = '#000000'; c.strokeStyle = '#000000'; c.lineWidth = .3
    const text = (value, x, y, maxWidth, align = 'left') => {
      c.textAlign = align; c.textBaseline = 'middle'; font()
      // Long labels wrap/ellipsis in charts only; underlying records are not truncated.
      let label = String(value ?? '')
      if (maxWidth && c.measureText(label).width > maxWidth) {
        while (label.length && c.measureText(label + '…').width > maxWidth) label = label.slice(0, -1)
        label += '…'
      }
      c.fillText(label, x, y)
    }
    const line = (x1, y1, x2, y2) => { c.beginPath(); c.moveTo(x1, y1); c.lineTo(x2, y2); c.stroke() }
    if (type === 'Shape' || type === 'Line') {
      c.strokeStyle = color(o.stroke, '#000000'); c.lineWidth = Number(o.strokeWidthMm ?? .2)
      const pad = c.lineWidth / 2
      if (type === 'Line') {
        c.setLineDash(o.dash === 'Dashed' ? [2, 1] : o.dash === 'Dotted' ? [.3, .7] : [])
        const points = { Horizontal: [0, height / 2, width, height / 2], Vertical: [width / 2, 0, width / 2, height], DiagonalDown: [pad, pad, width - pad, height - pad], DiagonalUp: [pad, height - pad, width - pad, pad] }
        if (!points[o.direction]) fail('REPORT_GRAPHIC_OPTION_INVALID')
        line(...points[o.direction])
        // Do not retain the legacy line's extra top border when a drawn line is present.
        const control = holder.closest('[data-report-control]'); if (control) control.style.borderTop = '0'
      } else {
        c.beginPath()
        if (o.kind === 'Ellipse') c.ellipse(width / 2, height / 2, width / 2 - pad, height / 2 - pad, 0, 0, Math.PI * 2)
        else if (o.kind === 'Diamond') { c.moveTo(width / 2, pad); c.lineTo(width - pad, height / 2); c.lineTo(width / 2, height - pad); c.lineTo(pad, height / 2); c.closePath() }
        else if (o.kind === 'Rectangle') c.rect(pad, pad, width - 2 * pad, height - 2 * pad)
        else fail('REPORT_GRAPHIC_OPTION_INVALID')
        if (o.fill !== 'none') { c.fillStyle = color(o.fill, '#ffffff'); c.fill() }
        c.stroke()
      }
    } else if (type === 'Barcode') {
      const alphabet = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%'
      const encodings = [0x034,0x121,0x061,0x160,0x031,0x130,0x070,0x025,0x124,0x064,0x109,0x049,0x148,0x019,0x118,0x058,0x00D,0x10C,0x04C,0x01C,0x103,0x043,0x142,0x013,0x112,0x052,0x007,0x106,0x046,0x016,0x181,0x0C1,0x1C0,0x091,0x190,0x0D0,0x085,0x184,0x0C4,0x0A8,0x0A2,0x08A,0x02A]
      if (o.symbology !== 'Code39' || typeof value !== 'string' || !value.length || value.length > 40 || [...value].some(ch => !alphabet.includes(ch))) fail('REPORT_BARCODE_VALUE_INVALID')
      const symbols = [0x094, ...[...value].map(ch => encodings[alphabet.indexOf(ch)]), 0x094]
      const modules = 16 * symbols.length - 1 + 20, unit = width / modules
      if (unit < .25 || height < (o.showText ? 14 : 10)) fail('REPORT_BARCODE_SIZE_INVALID')
      c.fillStyle = '#ffffff'; c.fillRect(0, 0, width, height); c.fillStyle = '#000000'
      let x = 10 * unit
      for (const symbol of symbols) {
        for (let bit = 8; bit >= 0; bit--) {
          const segment = ((symbol >> bit) & 1 ? 3 : 1) * unit
          if (bit % 2 === 0) c.fillRect(x, 1, segment, height - (o.showText ? 6 : 2))
          x += segment
        }
        x += unit
      }
      if (o.showText) text(value, width / 2, height - 2.5, width - 2, 'center')
    } else if (type === 'Gauge') {
      const n = number(value), min = Number(o.min), max = Number(o.max)
      if (n === null || !(max > min) || n < min || n > max) fail('REPORT_GRAPHIC_VALUE_INVALID')
      c.fillStyle = '#e5e7eb'; c.fillRect(1, height * .42, width - 2, height * .2)
      c.fillStyle = palette[0]; c.fillRect(1, height * .42, (width - 2) * (n - min) / (max - min), height * .2)
      c.fillStyle = '#000000'; text(o.label, width / 2, height * .17, width - 2, 'center')
      text(min, 1, height * .82, width / 4); text(n, width / 2, height * .82, width / 3, 'center'); text(max, width - 1, height * .82, width / 4, 'right')
    } else if (type === 'Chart' || type === 'Sparkline') {
      const isChart = type === 'Chart'
      const rows = value
      if (!Array.isArray(rows) || rows.length > 200) fail('REPORT_GRAPHIC_VALUE_INVALID')
      const series = isChart ? o.series.map(s => ({ name: s.name, values: rows.map(row => number(read(row, s.valuePath))) })) : [{ name: '', values: rows.map(number) }]
      const numbers = series.flatMap(s => s.values).filter(n => n !== null)
      if (!series.length || series.length > 8) fail('REPORT_GRAPHIC_VALUE_INVALID')
      let low = Math.min(0, ...numbers), high = Math.max(0, ...numbers)
      if (low === high) { low -= 1; high += 1 }
      const left = isChart ? 10 : 1, right = width - 2, top = isChart ? 3 + Math.ceil(series.length / 2) * 4 : 1, bottom = height - (isChart ? 8 : 1)
      if (bottom <= top || right <= left) fail('REPORT_GRAPHIC_SIZE_INVALID')
      const step = (right - left) / Math.max(1, rows.length), x = i => left + (i + .5) * step, y = n => bottom - (n - low) / (high - low) * (bottom - top)
      if (isChart) {
        c.strokeStyle = '#9ca3af'; c.lineWidth = .15
        for (let i = 0; i <= 4; i++) { const n = low + (high - low) * i / 4; line(left, y(n), right, y(n)); c.fillStyle = '#000000'; text(Number(n.toPrecision(4)), left - 1, y(n), left - 2, 'right') }
        const tick = Math.max(1, Math.ceil(rows.length / Math.max(1, Math.floor((right - left) / 15))))
        rows.forEach((row, i) => { if (i % tick === 0) text(read(row, o.categoryPath), x(i), height - 4, Math.max(10, step * tick - 1), 'center') })
      }
      series.forEach((s, index) => {
        c.strokeStyle = palette[index]; c.fillStyle = palette[index]; c.lineWidth = .5
        if (isChart) { c.fillRect(1 + (index % 2) * width / 2, 1 + Math.floor(index / 2) * 4, 2, 2); text(s.name, 4 + (index % 2) * width / 2, 2 + Math.floor(index / 2) * 4, width / 2 - 5) }
        let previous = null
        s.values.forEach((n, i) => {
          if (n === null) { previous = null; return }
          if (isChart && o.kind === 'Bar') { const bar = step * .8 / series.length; c.fillRect(left + i * step + step * .1 + index * bar, Math.min(y(n), y(0)), bar, Math.max(.12, Math.abs(y(n) - y(0)))) }
          else { if (previous) line(previous[0], previous[1], x(i), y(n)); c.beginPath(); c.arc(x(i), y(n), .55, 0, Math.PI * 2); c.fill(); previous = [x(i), y(n)] }
        })
      })
      if (!numbers.length) { c.fillStyle = '#666666'; text('无数值 / No values', width / 2, height / 2, width - 2, 'center') }
    } else fail('REPORT_GRAPHIC_TYPE_INVALID')
    const image = element('img'); image.alt = type
    image.style.cssText = `display:block;width:${width}mm;height:${height}mm;max-width:none;max-height:none`
    image.src = canvas.toDataURL('image/png'); await image.decode(); holder.append(image)
    delete holder.dataset.reportAdvanced
  }
}
