// Isolated report rendering command. Engine/browser assets are supplied by the
// operator; this repository does not redistribute Office, Chromium or fonts.
import { createRequire } from 'node:module'
import { readFile, writeFile, mkdir, access } from 'node:fs/promises'
import { resolve, join, isAbsolute } from 'node:path'
import { pathToFileURL } from 'node:url'
import { createHash } from 'node:crypto'
import { crc32 } from 'node:zlib'

const hash = (value) => createHash('sha256').update(value).digest('hex')
const option = (name) => {
  const index = process.argv.indexOf(name)
  if (index < 0 || !process.argv[index + 1]) throw new Error(`Missing ${name}`)
  return resolve(process.argv[index + 1])
}
const inputBytes = await readFile(option('--input'))
const input = JSON.parse(inputBytes.toString('utf8'))
const output = option('--output')
const manifestPath = process.env.PF09_RENDERER_MANIFEST
if (!manifestPath || !isAbsolute(manifestPath)) throw new Error('RENDERER_MANIFEST_REQUIRED')
const manifestBytes = await readFile(manifestPath)
const engineManifestHash = hash(manifestBytes)
if (engineManifestHash !== input.engineManifestHash) throw new Error('RENDERER_MANIFEST_MISMATCH')
const manifest = JSON.parse(manifestBytes)
if (hash(await readFile(new URL('./pf09-report-row-fragments.mjs', import.meta.url))) !== manifest.rowFragmentSha256) throw new Error('RENDERER_HELPER_MISMATCH')
const { fragmentReportRows, installReportTableHeaders } = await import('./pf09-report-row-fragments.mjs')
if (hash(await readFile(new URL('./pf09-report-layout.mjs', import.meta.url))) !== manifest.layoutSha256) throw new Error('RENDERER_LAYOUT_MISMATCH')
const { layoutReportBands, reportPaperGeometry } = await import('./pf09-report-layout.mjs')
if (hash(await readFile(new URL('./pf09-report-advanced.mjs', import.meta.url))) !== manifest.advancedSha256) throw new Error('RENDERER_ADVANCED_MISMATCH')
const { renderReportAdvanced } = await import('./pf09-report-advanced.mjs')
if (hash(await readFile(new URL('./pf09-report-documents.mjs', import.meta.url))) !== manifest.documentsSha256) throw new Error('RENDERER_DOCUMENTS_MISMATCH')
const { inspectPdf, prepareReportToc, resolveReportToc, appendPdfPages } = await import('./pf09-report-documents.mjs')
for (const asset of ['bundle', 'browser']) {
  if (!isAbsolute(manifest[`${asset}Path`] || '')) throw new Error('RENDERER_ASSET_PATH_REQUIRED')
  if (hash(await readFile(manifest[`${asset}Path`])) !== manifest[`${asset}Sha256`]) throw new Error(`RENDERER_${asset.toUpperCase()}_MISMATCH`)
}
if (typeof input.html !== 'string' || input.html.length > 20_000_000 || typeof input.css !== 'string' || input.css.length > 1_000_000) throw new Error('REPORT_INPUT_LIMIT')
if (/@import|url\s*\(|expression\s*\(|<\/style/i.test(input.css)) throw new Error('REPORT_EXTERNAL_STYLE_DENIED')
const require = createRequire(new URL('../src/frontend/package.json', import.meta.url))
const { chromium } = require('@playwright/test')
await mkdir(output, { recursive: true })
const browser = await chromium.launch({ executablePath: manifest.browserPath, headless: true, chromiumSandbox: true })
let page
let cancelled = false
let failed = false
let failureCode = null
let timedOut = false
let checkingCancellation = false
const cancellation = setInterval(async () => {
  if (checkingCancellation || cancelled) return
  checkingCancellation = true
  try {
    await access(join(output, 'cancel.request'))
    cancelled = true
    await browser.close()
  } catch (error) { if (error.code !== 'ENOENT' && !cancelled) process.exitCode = 1 }
  finally { checkingCancellation = false }
}, 250)
const timeout = setTimeout(() => { timedOut = true; void browser.close() }, Math.min(Number(manifest.timeoutMs) || 120000, 300000))
try {
  page = await browser.newPage({ deviceScaleFactor: ['Png', 'Jpeg'].includes(input.format) ? 150 / 96 : 1 })
  await page.route('**/*', (route) => route.abort())
  await page.setContent('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><body><main id="source"></main><div id="pages"></div></body></html>')
  const geometry = reportPaperGeometry(input.paper)
  const { bodyHeight, bodyWidth } = geometry
  const pageRule = ({ widthMm, heightMm, margin, headerHeight, footerHeight }, name = '', suffix = '') => `@page ${name} { size:${widthMm}mm ${heightMm}mm; margin:${margin.top + headerHeight}mm ${margin.right}mm ${margin.bottom + footerHeight}mm ${margin.left}mm; @top-center {content:element(reportPageHeader${suffix})} @bottom-center {content:element(reportPageFooter${suffix})} @bottom-right {content:counter(page) " / " counter(pages)} }`
  const parts = (input.parts || []).map((part, index) => ({ ...part, index, geometry: reportPaperGeometry(part.paper), pageCss: pageRule(reportPaperGeometry(part.paper)) }))
  if (parts.length > 200 || new Set(parts.map((part) => part.key)).size !== parts.length) throw new Error('REPORT_PARTS_INVALID')
  const css = pageRule(geometry) + `body {font-family:sans-serif;font-size:10pt;line-height:1.3} p {orphans:2;widows:2} table {width:100%;border-collapse:collapse;table-layout:fixed} td,th {vertical-align:top;overflow-wrap:anywhere} img {max-width:100%;max-height:${bodyHeight - 4}mm;object-fit:contain} [data-report-page-number]::after {content:counter(page)} ${input.css}`
    + '.report-page-header{position:running(reportPageHeader)}.report-page-footer{position:running(reportPageFooter)}'
    + parts.map((part) => `.report-part-${part.index}{width:${part.geometry.bodyWidth}mm}.report-part-${part.index} img{max-height:${part.geometry.bodyHeight - 4}mm}`).join('')
  await page.evaluate(({ html, css, widthMm, values }) => {
    const parsed = new DOMParser().parseFromString(html, 'text/html')
    for (const section of parsed.body.querySelectorAll('[data-report-html="true"]')) {
      const record = new DOMParser().parseFromString(section.textContent, 'text/html')
      if (record.head.querySelector('script,style,link,base') || [...record.head.querySelectorAll('meta')].some((meta) => !meta.hasAttribute('charset'))) throw new Error('REPORT_HTML_HEAD_DENIED')
      for (const element of record.body.querySelectorAll('*')) if ([...element.attributes].some((attribute) => attribute.name.startsWith('data-report-') || attribute.name === 'data-token')) throw new Error('REPORT_HTML_RESERVED_ATTRIBUTE')
      const resources = JSON.parse(section.dataset.reportResources || '{}')
      for (const img of record.body.querySelectorAll('img')) { const src = img.getAttribute('src'); if (Object.hasOwn(resources, src)) img.setAttribute('src', resources[src]) }
      section.replaceChildren(...record.body.childNodes)
    }
    const allowed = new Set('DIV SECTION ARTICLE HEADER FOOTER MAIN ASIDE P SPAN BR HR STRONG B EM I U S SUB SUP SMALL H1 H2 H3 H4 H5 H6 UL OL LI BLOCKQUOTE PRE CODE TABLE THEAD TBODY TFOOT TR TD TH COLGROUP COL IMG INPUT TEXTAREA SELECT OPTION LABEL'.split(' '))
    for (const node of parsed.body.querySelectorAll('*')) {
      if (!allowed.has(node.tagName)) throw new Error(`REPORT_HTML_ELEMENT_DENIED:${node.tagName}`)
      for (const attr of [...node.attributes]) {
        const name = attr.name.toLowerCase()
        if (name.startsWith('on') || ['srcdoc', 'href', 'action', 'formaction', 'background', 'srcset'].includes(name)) throw new Error('REPORT_HTML_ACTIVE_ATTRIBUTE_DENIED')
        if (name === 'src' && (node.tagName !== 'IMG' || !/^data:image\/(png|jpeg|gif|webp);base64,[a-z0-9+/=\s]+$/i.test(attr.value))) throw new Error('REPORT_IMAGE_NOT_FROZEN')
        if (name === 'style' && /url\s*\(|expression\s*\(|@import|behavior\s*:/i.test(attr.value)) throw new Error('REPORT_HTML_STYLE_DENIED')
        const staticCheckAttribute = node.tagName === 'SPAN' && ((name === 'role' && attr.value === 'checkbox') || (name === 'aria-checked' && ['true', 'false', 'mixed'].includes(attr.value)))
        if (!['id', 'class', 'style', 'title', 'lang', 'dir', 'src', 'alt', 'width', 'height', 'colspan', 'rowspan', 'span', 'scope', 'type', 'value', 'checked', 'selected', 'disabled', 'multiple', 'name', 'for'].includes(name) && !name.startsWith('data-report-') && name !== 'data-token' && !staticCheckAttribute) throw new Error(`REPORT_HTML_ATTRIBUTE_DENIED:${name}`)
      }
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(node.tagName)) {
        const scope = node.closest('[data-report-form-values]')
        const scopedValues = scope ? JSON.parse(scope.dataset.reportFormValues) : values
        const saved = scopedValues?.[node.id] ?? scopedValues?.[node.getAttribute('name')]
        const frozen = saved !== null && typeof saved === 'object' ? saved : typeof saved === 'boolean' ? { checked: saved } : { value: saved }
        const result = parsed.createElement('span')
        if (node.dataset.reportMarker) result.dataset.reportMarker = node.dataset.reportMarker
        if (node.tagName === 'INPUT' && ['checkbox', 'radio'].includes(node.type)) {
          if (frozen.checked === undefined && !node.hasAttribute('checked')) throw new Error('REPORT_FORM_VALUE_MISSING')
          result.textContent = (frozen.checked ?? node.checked) ? '☑' : '☐'
        } else if (node.tagName === 'SELECT') {
          if (frozen.value === undefined && ![...node.options].some((option) => option.hasAttribute('selected'))) throw new Error('REPORT_FORM_VALUE_MISSING')
          const selected = frozen.value === undefined ? [...node.options].filter((option) => option.selected) : [...node.options].filter((option) => (Array.isArray(frozen.value) ? frozen.value : [frozen.value]).map(String).includes(option.value))
          if (frozen.value !== undefined && !selected.length) throw new Error('REPORT_FORM_VALUE_INVALID')
          result.textContent = selected.map((option) => option.textContent).join('、')
        }
        else if (node.tagName === 'INPUT' && frozen.value === undefined && !node.hasAttribute('value')) throw new Error('REPORT_FORM_VALUE_MISSING')
        else result.textContent = String(frozen?.value ?? node.value ?? '')
        node.replaceWith(result)
      }
    }
    if (parsed.head.querySelector('script,style,link,base') || [...parsed.head.querySelectorAll('meta')].some((meta) => !meta.hasAttribute('charset'))) throw new Error('REPORT_HTML_HEAD_DENIED')
    const source = document.querySelector('#source')
    source.replaceChildren(...parsed.body.childNodes)
    source.style.width = `${widthMm}mm`
    const style = document.createElement('style'); style.dataset.pagedjsIgnore = 'true'; style.textContent = css; document.head.append(style)
    window.PagedConfig = { auto: false }
  }, { html: input.html, css, widthMm: bodyWidth, values: input.formValues })
  const pdfSources = await page.evaluate(() => [...document.querySelectorAll('#source [data-report-pdf]')].map((node, index) => { node.dataset.reportPdfIndex = String(index); return node.dataset.reportPdf }))
  let pdfEngine; const importedPdfs = []
  if (pdfSources.length) {
    if (input.format === 'Xlsx') throw new Error('REPORT_XLSX_DOCUMENT_UNSUPPORTED')
    for (const name of ['pdfBrowser', 'pdfNative', 'pdfFonts']) if (!isAbsolute(manifest[name + 'Path'] || '') || hash(await readFile(manifest[name + 'Path'])) !== manifest[name + 'Sha256']) throw new Error('REPORT_PDF_ENGINE_UNAVAILABLE')
    pdfEngine = await import(pathToFileURL(manifest.pdfNativePath).href)
    let total = 0, totalPages = 0
    for (const source of pdfSources) {
      if (!/^data:application\/pdf;base64,[A-Za-z0-9+/=]+$/.test(source)) throw new Error('REPORT_PDF_RESOURCE_INVALID')
      const bytes = Buffer.from(source.split(',')[1], 'base64'); total += bytes.length
      if (bytes.length > 5 * 1024 * 1024 || total > 20 * 1024 * 1024) throw new Error('REPORT_PDF_RESOURCE_LIMIT')
      const pdf = await inspectPdf(bytes, pdfEngine); totalPages += pdf.getPageCount()
      if (totalPages > 100) throw new Error('REPORT_PDF_PAGE_LIMIT')
      importedPdfs.push(pdf)
    }
    await page.addScriptTag({ content: await readFile(manifest.pdfBrowserPath, 'utf8') })
    await page.evaluate(async ({ sources, assets }) => {
      window.__reportPdfPages = []; let pixels = 0
      for (const source of sources) {
        const pages = await window.ReportPdf.renderPdf(source.split(',')[1], assets)
        for (const p of pages) pixels += p.widthMm * 150 / 25.4 * p.heightMm * 150 / 25.4
        if (pixels > 80000000) throw Error('REPORT_PDF_RASTER_LIMIT')
        window.__reportPdfPages.push(pages)
      }
    }, { sources: pdfSources, assets: JSON.parse(await readFile(manifest.pdfFontsPath, 'utf8')) })
  }
  await page.addScriptTag({ content: `window.prepareReportToc=${prepareReportToc.toString()};window.resolveReportToc=${resolveReportToc.toString()};window.appendPdfPages=${appendPdfPages.toString()}` })
  await page.addScriptTag({ content: await readFile(manifest.bundlePath, 'utf8') })
  await page.addScriptTag({ content: `window.renderReportAdvanced = ${renderReportAdvanced.toString()}; window.fragmentReportRows = ${fragmentReportRows.toString()}; window.layoutReportBands = ${layoutReportBands.toString()}; (${installReportTableHeaders.toString()})(window.Paged)` })
  const result = await page.evaluate(async ({ height, css, parts, format, preview }) => {
    await document.fonts.ready
    await Promise.all([...document.images].map((img) => img.decode().catch(() => { throw new Error('REPORT_IMAGE_DECODE_FAILED') })))
    const source = document.querySelector('#source')
    // Loading completion alone does not prove that a requested local font exists.
    // Probe each distinct text font stack once; keep CSS fallback behavior and report it.
    const fontStacks = new Set()
    const textWalker = document.createTreeWalker(source, NodeFilter.SHOW_TEXT)
    let textNode
    while ((textNode = textWalker.nextNode())) if (textNode.textContent.trim()) fontStacks.add(getComputedStyle(textNode.parentElement).fontFamily)
    for (const node of source.querySelectorAll('[data-report-advanced]')) fontStacks.add(getComputedStyle(node).fontFamily)
    if (fontStacks.size > 128) throw new Error('REPORT_FONT_LIMIT')
    const genericFonts = new Set('serif sans-serif monospace cursive fantasy system-ui ui-serif ui-sans-serif ui-monospace ui-rounded math fangsong'.split(' '))
    const availableFonts = new Map(), fontResolution = [], fontDiagnostics = []
    for (const stack of fontStacks) {
      const families = (stack.match(/"[^"]*"|'[^']*'|[^,]+/g) || []).map(value => value.trim().replace(/^['"]|['"]$/g, ''))
      const unavailable = []
      let selected = null
      for (const family of families) {
        if (genericFonts.has(family.toLowerCase())) { selected = family; break }
        if (!availableFonts.has(family)) {
          try { await new FontFace('report-font-probe', 'local(' + JSON.stringify(family) + ')').load(); availableFonts.set(family, true) }
          catch { availableFonts.set(family, false) }
        }
        if (availableFonts.get(family)) { selected = family; break }
        unavailable.push(family)
      }
      fontResolution.push({ requested: stack, selected, unavailable })
      if (unavailable.length) fontDiagnostics.push({ code: 'REPORT_FONT_SUBSTITUTED', requested: stack, unavailable, fallback: selected || 'system fallback' })
    }
    window.prepareReportToc(source, format)
    await window.renderReportAdvanced(source, format)
    const workbookRows = []
    const dataFormat = ['Xls', 'Xlsx', 'Text', 'Csv'].includes(format)
    const editableFormat = ['Docx', 'Rtf'].includes(format)
    const exportBlocks = [], tableHeaders = new Map()
    if (dataFormat || editableFormat) {
      for (const control of source.querySelectorAll('[data-report-control]')) {
        if (dataFormat && control.closest('[data-report-repeat="true"]')) continue
        const definition = JSON.parse(control.dataset.reportControl)
        if (['Line', 'Box', 'PageNumber'].includes(definition.type)) continue
        if (['Html', 'PdfContent', 'TableOfContents'].includes(definition.type)) throw new Error(format === 'Xlsx' ? 'REPORT_XLSX_RICH_LAYOUT_UNSUPPORTED' : 'REPORT_EXPORT_CONTROL_UNSUPPORTED: ' + definition.type + '/' + format)
        if (dataFormat && control.querySelector('img,svg,canvas')) throw new Error(format === 'Xlsx' ? 'REPORT_XLSX_RICH_LAYOUT_UNSUPPORTED' : 'REPORT_EXPORT_CONTROL_UNSUPPORTED: ' + definition.type + '/' + format)
        const table = control.querySelector('table')
        if (table) {
          // Expand merged cells into a rectangular grid; put content only in its origin.
          const rows = [], occupied = []
          for (const [r, row] of [...table.rows].entries()) {
            rows[r] ||= []; occupied[r] ||= []; let c = 0
            for (const cell of row.cells) {
              while (occupied[r][c]) c++
              for (let y = r; y < r + cell.rowSpan; y++) for (let x = c; x < c + cell.colSpan; x++) {
                occupied[y] ||= []; rows[y] ||= []; occupied[y][x] = true; rows[y][x] = y === r && x === c ? cell.innerText : ''
              }
              c += cell.colSpan
            }
          }
          const columns = Math.max(...rows.map(r => r.length))
          for (const row of rows) for (let c = 0; c < columns; c++) row[c] ??= ''
          workbookRows.push(...rows)
          const group = table.dataset.reportHeaderGroup, previous = exportBlocks.at(-1)
          const repeatHeader = table.dataset.reportRepeatHeader !== 'false'
          const headerRows = table.tHead?.rows.length || 0
          if (editableFormat && group && repeatHeader && headerRows) tableHeaders.set(group, rows.slice(0, headerRows))
          // Detail bands may emit one HTML table per record. Office needs one logical table
          // to repeat its header when that collection continues onto another page.
          if (editableFormat && group && previous?.type === 'table' && previous.group === group) previous.rows.push(...rows)
          else {
            // A paragraph splits an Office table. Restore the collection header on its continuation.
            const carried = editableFormat && group && repeatHeader && !headerRows ? tableHeaders.get(group) || [] : []
            exportBlocks.push({ type: 'table', rows: [...carried, ...rows], headerRows: repeatHeader ? headerRows + carried.length : 0, group })
          }
        } else if (control.querySelector('img')) {
          const img = control.querySelector('img'); await img.decode()
          const canvas = document.createElement('canvas'); canvas.width = img.naturalWidth; canvas.height = img.naturalHeight
          if (canvas.width * canvas.height > 16000000) throw Error('REPORT_EXPORT_IMAGE_LIMIT')
          canvas.getContext('2d').drawImage(img, 0, 0)
          exportBlocks.push({ type: 'image', data: canvas.toDataURL('image/png'), widthMm: definition.widthMm, heightMm: definition.heightMm })
        } else {
          const checkbox = control.querySelector('[role="checkbox"]')
          const text = checkbox ? ({true: '[x]', false: '[ ]', mixed: '[-]'})[checkbox.getAttribute('aria-checked')] : definition.type === 'CharacterComb' ? control.textContent : control.innerText
          workbookRows.push([text])
          exportBlocks.push({ type: 'paragraph', text, bold: Number(getComputedStyle(control).fontWeight) >= 600, heading: !!control.querySelector('[data-report-outline]') || !!JSON.parse(control.closest('[data-report-band]')?.dataset.reportBand || '{}').keepWithNext })
        }
      }
    }
    if (parts.length) {
      const watermark = source.querySelector(':scope > .report-preview'); watermark?.remove()
      const byKey = new Map(parts.map((part) => [String(part.key), part]))
      const consumed = new Set(); let current
      for (const child of [...source.children]) {
        const key = child.dataset.reportPart
        const part = byKey.get(key)
        if (!part) throw new Error('REPORT_PART_MISSING')
        if (!current || current.dataset.reportPart !== key) {
          if (consumed.has(key)) throw new Error('REPORT_PART_NOT_CONTIGUOUS')
          consumed.add(key); current = document.createElement('article')
          current.dataset.reportPart = key; current.dataset.reportPartIndex = String(part.index)
          current.className = `report-part-${part.index}`; current.style.width = `${part.geometry.bodyWidth}mm`; source.append(current)
        }
        delete child.dataset.reportPart; current.append(child)
      }
      if (watermark) source.firstElementChild?.prepend(watermark)
    }
    const contents = [], diagnostics = [...fontDiagnostics]; let paragraphSequence = 0, segment = 0
    for (const container of parts.length ? [...source.children] : [source]) {
      const part = parts.length ? parts[Number(container.dataset.reportPartIndex)] : null
      const heightPx = part ? part.geometry.bodyHeight * 96 / 25.4 : height
      const repeated = [...container.children].filter(n => n.querySelector('[data-report-repeat="true"]'))
      let pending = []
      const flush = () => {
        if (!pending.some(n => !repeated.includes(n))) return
        const section = document.createElement('article'); section.style.width = container.style.width || source.style.width
        if (part) section.className = `report-part-${part.index}`
        source.append(section); section.append(...repeated.map(n => n.cloneNode(true)), ...pending.filter(n => !repeated.includes(n))); pending = []
        diagnostics.push(...window.layoutReportBands(section, heightPx))
        for (const p of section.querySelectorAll('p,pre')) if (!p.closest('thead,[data-report-repeat="true"]')) p.dataset.reportParagraph = String(paragraphSequence++)
        diagnostics.push(...window.fragmentReportRows(section, heightPx, 'seg' + (segment++) + '-'))
        contents.push({ html: part ? section.outerHTML : section.innerHTML, part }); section.remove()
      }
      for (const child of [...container.children]) {
        const pdf = child.querySelector('[data-report-pdf]')
        if (pdf) {
          flush()
          contents.push({ pdf: { index: Number(pdf.dataset.reportPdfIndex), marker: pdf.closest('[data-report-marker]').dataset.reportMarker, outline: [...child.querySelectorAll('[data-report-outline]')].map(n => n.outerHTML).join('') }, part })
        } else pending.push(child)
      }
      flush()
    }
    source.remove()
    const importedStyles = document.createElement('style'); document.head.append(importedStyles)
    for (const item of contents) {
      const target = document.createElement('div'); document.querySelector('#pages').append(target)
      if (item.pdf) { window.appendPdfPages(target, window.__reportPdfPages[item.pdf.index], item.pdf, importedStyles, preview); continue }
      await new window.Paged.Previewer().preview(item.html, [{ 'https://renderer.invalid/report.css': css + (item.part?.pageCss || '') }], target)
      for (const rendered of target.querySelectorAll('.pagedjs_page')) {
        if (item.part) rendered.dataset.reportOutputPart = item.part.key
        // Each independent flow owns its paper. Freeze its computed variables
        // before the next Previewer updates shared root page variables.
        const style = getComputedStyle(rendered)
        for (const property of style) if (property.startsWith('--pagedjs-')) rendered.style.setProperty(property, style.getPropertyValue(property))
      }
    }
    await document.fonts.ready
    const pageElements = [...document.querySelectorAll('#pages .pagedjs_page')]
    const toc = window.resolveReportToc(pageElements)
    const partKeys = pageElements.map((page) => page.dataset.reportOutputPart)
    const printStyles = document.createElement('style')
    printStyles.textContent = parts.map((part) => `@page printpart${part.index}{size:${part.geometry.widthMm}mm ${part.geometry.heightMm}mm;margin:0}`).join('')
    document.head.append(printStyles)
    const localCounts = new Map()
    const pages = pageElements.map((page, index) => {
      if (page.dataset.reportPdfIndex !== undefined) {
        const box = page.getBoundingClientRect()
        return { pageNo: index + 1, widthMm: box.width * 25.4 / 96, heightMm: box.height * 25.4 / 96, html: page.outerHTML, markers: [{ marker: page.querySelector('[data-report-marker]').dataset.reportMarker, fragmentIndex: Number(page.dataset.reportPdfPage) }], pdfIndex: Number(page.dataset.reportPdfIndex), pdfPage: Number(page.dataset.reportPdfPage) }
      }
      const partKey = partKeys[index]
      const part = parts.find((item) => String(item.key) === partKey)
      if (part) page.style.page = `printpart${part.index}`
      const pageNo = part?.pageNumberMode === 'Preserve' ? (localCounts.get(partKey) || 0) + 1 : index + 1
      localCounts.set(partKey, (localCounts.get(partKey) || 0) + 1)
      const pageTotal = part?.pageNumberMode === 'Preserve' ? partKeys.filter((key) => key === partKey).length : pageElements.length
      for (const position of ['top', 'bottom']) {
        const center = page.querySelector(`.pagedjs_margin-${position}-center`)
        if (center.querySelector('.report-page-header,.report-page-footer')) {
          // Bands use the entire printable width, not one of three margin columns.
          center.style.gridColumn = '1 / -1'
          for (const box of center.parentElement.children) box.style.gridRow = '1'
        }
      }
      const pageNumbers = page.querySelectorAll('[data-report-page-number]')
      for (const number of pageNumbers) { number.textContent = `${pageNo} / ${pageTotal}`; number.classList.add('report-number-resolved') }
      const footer = page.querySelector('.pagedjs_margin-bottom-right .pagedjs_margin-content')
      if (pageNumbers.length) footer.style.display = 'none'
      else { footer.textContent = `${pageNo} / ${pageTotal}`; footer.classList.add('report-number-resolved') }
      const area = page.querySelector('.pagedjs_page_content').getBoundingClientRect()
      const markers = [...page.querySelectorAll('[data-report-marker]')].filter((element) => !element.closest('[data-report-repeat="true"]')).map((element) => {
        // Fragmented block boxes can span virtual columns. Check actual glyph
        // line boxes and image boxes so both clipping and false positives are
        // detected using the content that will be printed.
        const boxes = [...element.querySelectorAll('img')].map((image) => image.getBoundingClientRect())
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
        let text
        while ((text = walker.nextNode())) {
          if (!text.textContent.trim() || text.parentElement.closest('[data-report-repeat="true"]')) continue
          const range = document.createRange(); range.selectNodeContents(text)
          boxes.push(...range.getClientRects())
        }
        if (!boxes.length) boxes.push(element.getBoundingClientRect())
        for (const box of boxes) if (box.top < area.top - 2 || box.bottom > area.bottom + 2 || box.left < area.left - 2 || box.right > area.right + 2) throw new Error('REPORT_LAYOUT_OVERFLOW')
        return { marker: element.dataset.reportMarker, fragmentIndex: Number(element.closest('[data-fragment-index]')?.dataset.fragmentIndex || 0) }
      })
      const box = page.getBoundingClientRect()
      return { pageNo: index + 1, widthMm: box.width * 25.4 / 96, heightMm: box.height * 25.4 / 96, html: page.outerHTML, markers }
    })
    if (!pages.length) throw new Error('REPORT_LAYOUT_EMPTY')
    const paragraphPages = new Map()
    for (const [pageIndex, renderedPage] of pageElements.entries()) for (const paragraph of renderedPage.querySelectorAll('[data-report-paragraph]')) {
      if (paragraph.closest('[data-report-repeat="true"]')) continue
      const id = paragraph.dataset.reportParagraph, byPage = paragraphPages.get(id) || new Map(), tops = byPage.get(pageIndex) || new Set()
      const walker = document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT); let node
      while ((node = walker.nextNode())) { if (!node.textContent.trim()) continue; const range = document.createRange(); range.selectNodeContents(node); for (const rect of range.getClientRects()) if (rect.width > 0 && rect.height > 0) tops.add(Math.round(rect.top * 2) / 2) }
      if (tops.size) { byPage.set(pageIndex, tops); paragraphPages.set(id, byPage) }
    }
    for (const byPage of paragraphPages.values()) if (byPage.size > 1 && [...byPage.values()].some((lines) => lines.size < 2)) throw new Error('REPORT_LAYOUT_WIDOW_ORPHAN: fewer than two paragraph lines on a page')
    const cellFragmentLedger = [], sourceCells = [], cellOffsets = new Map()
    for (const [id, text] of window.__reportCellSources || []) sourceCells.push({ id, length: text.length, unit: 'UTF16' })
    for (const [pageIndex, renderedPage] of pageElements.entries()) for (const cell of renderedPage.querySelectorAll('[data-source-cell]')) {
      if (cell.closest('thead,[data-report-repeat="true"]')) continue
      const copy = cell.cloneNode(true); for (const header of copy.querySelectorAll('thead,[data-report-repeat="true"]')) header.remove()
      const id = cell.dataset.sourceCell, text = copy.textContent, start = cellOffsets.get(id) || 0, end = start + text.length
      const original = window.__reportCellSources.get(id)
      if (original === undefined || original.slice(start, end) !== text) throw new Error('REPORT_CELL_RANGE_MISMATCH: lost or repeated rendered text')
      cellFragmentLedger.push({ sourceCell: id, pageNo: pageIndex + 1, start, end, unit: 'UTF16' }); cellOffsets.set(id, end)
    }
    for (const { id, length } of sourceCells) if ((cellOffsets.get(id) || 0) !== length) throw new Error('REPORT_CELL_RANGE_MISMATCH: incomplete rendered cell')
    const resolved = document.createElement('style'); resolved.textContent = '.report-number-resolved::after{content:none!important}'; document.head.append(resolved)
    return { pages, toc, diagnostics, fontResolution, workbookRows, exportBlocks, sourceCells, cellFragmentLedger, styles: [...document.styleSheets].flatMap((sheet) => [...sheet.cssRules].map((rule) => rule.cssText)).join('\n') }
  }, { height: bodyHeight * 96 / 25.4, css, parts, format: input.format, preview: !!input.preview })
  if (['Xls', 'Xlsx', 'Text', 'Csv', 'Docx', 'Rtf'].includes(input.format)) {
    const exportBytes = Buffer.from(JSON.stringify({ blocks: result.exportBlocks, rows: result.workbookRows }))
    if (exportBytes.length > 50 * 1024 * 1024) throw Error('REPORT_EXPORT_SIZE_LIMIT')
    await writeFile(join(output, 'result.export.json'), exportBytes)
    result.exportSha256 = hash(exportBytes)
    result.exportContract = 'pf09-continuous-v1'
    result.diagnostics.push({ code: 'REPORT_EXPORT_REFLOW', message: '连续文档或正文数据；文字和表格可编辑，合并单元格展开，装饰线框与物理页码不导出。' })
  }
  delete result.exportBlocks
  if (input.format !== 'Xlsx') delete result.workbookRows
  if (['Png', 'Jpeg'].includes(input.format)) {
    result.rasterPages = []; let pixels = 0
    const elements = await page.$$('#pages .pagedjs_page')
    for (const [index, element] of elements.entries()) {
      const box = await element.boundingBox()
      pixels += box.width * box.height * (150 / 96) ** 2
      if (pixels > 80000000) throw Error('REPORT_EXPORT_IMAGE_LIMIT')
      const type = input.format === 'Png' ? 'png' : 'jpeg', file = 'page-' + String(index + 1).padStart(4, '0') + '.' + (type === 'png' ? 'png' : 'jpg')
      let bytes = await element.screenshot({ type, ...(type === 'jpeg' ? {quality: 95} : {}), animations: 'disabled' })
      // Chromium rounds CSS clip edges independently. Normalize to the physical paper pixel grid.
      const encoded = await page.evaluate(async ({ data, width, height, type }) => {
        const img = new Image(); img.src = data; await img.decode()
        const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height
        const context = canvas.getContext('2d'); context.fillStyle = '#fff'; context.fillRect(0, 0, width, height); context.drawImage(img, 0, 0, width, height)
        return canvas.toDataURL('image/' + type, .95).split(',')[1]
      }, { data: 'data:image/' + type + ';base64,' + bytes.toString('base64'), width: Math.round(result.pages[index].widthMm * 150 / 25.4), height: Math.round(result.pages[index].heightMm * 150 / 25.4), type })
      bytes = Buffer.from(encoded, 'base64')
      // Screenshot pixels use 150 DPI; encode the same density so Office/image viewers retain paper size.
      if (type === 'png') {
        const payload = Buffer.alloc(13); payload.write('pHYs'); payload.writeUInt32BE(5906, 4); payload.writeUInt32BE(5906, 8); payload[12] = 1
        const chunk = Buffer.alloc(21); chunk.writeUInt32BE(9); payload.copy(chunk, 4); chunk.writeUInt32BE(crc32(payload), 17)
        const chunks = [bytes.subarray(0, 8), chunk]
        for (let offset = 8; offset < bytes.length;) { const length = bytes.readUInt32BE(offset) + 12; if (bytes.toString('ascii', offset + 4, offset + 8) !== 'pHYs') chunks.push(bytes.subarray(offset, offset + length)); offset += length }
        // IHDR must remain the first PNG chunk.
        const ihdr = chunks.splice(2, 1)[0]; chunks.splice(1, 0, ihdr); bytes = Buffer.concat(chunks)
      } else if (bytes.toString('ascii', 6, 11) === 'JFIF\0') {
        bytes[13] = 1; bytes.writeUInt16BE(150, 14); bytes.writeUInt16BE(150, 16)
      } else {
        const jfif = Buffer.from([0xff, 0xe0, 0, 16, 74, 70, 73, 70, 0, 1, 1, 1, 0, 150, 0, 150, 0, 0])
        bytes = Buffer.concat([bytes.subarray(0, 2), jfif, bytes.subarray(2)])
      }
      await writeFile(join(output, file), bytes)
      result.rasterPages.push({ pageNo: index + 1, file, sha256: hash(bytes), dpi: 150, widthMm: result.pages[index].widthMm, heightMm: result.pages[index].heightMm })
    }
  }
  const actual = new Set(result.pages.flatMap((page) => page.markers.map((item) => item.marker)))
  const fragmentCounts = new Map()
  for (const item of result.pages.flatMap((page) => page.markers)) {
    item.fragmentIndex = fragmentCounts.get(item.marker) || 0
    fragmentCounts.set(item.marker, item.fragmentIndex + 1)
  }
  const expected = (input.expectedMarkers || []).map((marker) => typeof marker === 'string' ? marker : marker.marker)
  if (expected.some((marker) => !actual.has(marker)) || (expected.length && [...actual].some((marker) => !expected.includes(marker)))) throw new Error('REPORT_MARKER_COVERAGE_MISMATCH')
  let pdf = input.preview && input.format === 'Html' ? null : await page.pdf({ preferCSSPageSize: true, printBackground: true })
  if (pdf && importedPdfs.length && !input.preview) {
    const rendered = await pdfEngine.PDFDocument.load(pdf)
    if (rendered.getPageCount() !== result.pages.length) throw Error('REPORT_PDF_PAGE_COUNT_MISMATCH')
    const merged = await pdfEngine.PDFDocument.create()
    for (const item of result.pages) {
      const source = item.pdfIndex === undefined ? rendered : importedPdfs[item.pdfIndex]
      const index = item.pdfIndex === undefined ? item.pageNo - 1 : item.pdfPage
      merged.addPage((await merged.copyPages(source, [index]))[0])
    }
    pdf = Buffer.from(await merged.save())
  }
  if (pdf) await writeFile(join(output, 'result.pdf'), pdf)
  await writeFile(join(output, 'result.html'), `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><style>${result.styles.replace(/<\/style/gi, '<\\/style')}</style><body>${result.pages.map((item) => item.html).join('')}</body></html>`)
  for (const item of result.pages) {
    item.htmlFile = `page-${item.pageNo}.html`
    await writeFile(join(output, item.htmlFile), `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><style>${result.styles.replace(/<\/style/gi, '<\\/style')}</style><body>${item.html}</body></html>`)
    delete item.html
  }
  delete result.styles
  await writeFile(join(output, 'result.json'), JSON.stringify({ ...result, pdfSha256: pdf ? hash(pdf) : null, engineManifestHash }, null, 2))
} catch (error) {
  failed = true
  failureCode = /REPORT_[A-Z0-9_]{1,80}/.exec(String(error.message))?.[0] || 'REPORT_RENDER_FAILED'
  if (!cancelled) throw error
} finally {
  clearInterval(cancellation)
  clearTimeout(timeout)
  await browser.close()
  await writeFile(join(output, 'renderer-stopped.json'), JSON.stringify({ engineManifestHash, inputSha256: hash(inputBytes), processId: process.pid, reason: cancelled ? 'Cancelled' : timedOut ? 'TimedOut' : failed ? 'Failed' : 'Completed', errorCode: failureCode }))
  if (cancelled) {
    await writeFile(join(output, 'stopped.json'), JSON.stringify({ engineManifestHash, reason: 'Cancelled' }))
    process.exitCode = 2
  }
}
