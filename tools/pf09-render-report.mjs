// Isolated report rendering command. Engine/browser assets are supplied by the
// operator; this repository does not redistribute Office, Chromium or fonts.
import { createRequire } from 'node:module'
import { readFile, writeFile, mkdir, access } from 'node:fs/promises'
import { resolve, join, isAbsolute } from 'node:path'
import { createHash } from 'node:crypto'

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
  page = await browser.newPage()
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
  await page.addScriptTag({ content: await readFile(manifest.bundlePath, 'utf8') })
  await page.addScriptTag({ content: `window.fragmentReportRows = ${fragmentReportRows.toString()}; window.layoutReportBands = ${layoutReportBands.toString()}; (${installReportTableHeaders.toString()})(window.Paged)` })
  const result = await page.evaluate(async ({ height, css, parts, format }) => {
    await document.fonts.ready
    await Promise.all([...document.images].map((img) => img.decode().catch(() => { throw new Error('REPORT_IMAGE_DECODE_FAILED') })))
    const source = document.querySelector('#source')
    const workbookRows = []
    if (format === 'Xlsx') {
      for (const control of source.querySelectorAll('[data-report-control]')) {
        if (control.closest('[data-report-repeat="true"]')) continue
        const definition = JSON.parse(control.dataset.reportControl)
        if (['Line', 'Box', 'PageNumber'].includes(definition.type)) continue
        if (definition.type === 'Html' || control.querySelector('img')) throw new Error('REPORT_XLSX_RICH_LAYOUT_UNSUPPORTED')
        const table = control.querySelector('table')
        if (table) for (const row of table.rows) workbookRows.push([...row.cells].map((cell) => cell.textContent))
        else workbookRows.push([control.textContent])
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
    const layoutDiagnostics = parts.length ? [...source.children].flatMap((part) => window.layoutReportBands(part, parts[Number(part.dataset.reportPartIndex)].geometry.bodyHeight * 96 / 25.4)) : window.layoutReportBands(source, height)
    let paragraphSequence = 0
    for (const paragraph of source.querySelectorAll('p,pre')) if (!paragraph.closest('thead,[data-report-repeat="true"]')) paragraph.dataset.reportParagraph = String(paragraphSequence++)
    const diagnostics = [...layoutDiagnostics, ...(parts.length ? [...source.children].flatMap((part) => window.fragmentReportRows(part, parts[Number(part.dataset.reportPartIndex)].geometry.bodyHeight * 96 / 25.4, `part${part.dataset.reportPartIndex}-`)) : window.fragmentReportRows(source, height))]
    const contents = parts.length ? [...source.children].map((part) => ({ html: part.outerHTML, part: parts[Number(part.dataset.reportPartIndex)] })) : [{ html: source.innerHTML }]
    source.remove()
    for (const item of contents) {
      const target = document.createElement('div'); document.querySelector('#pages').append(target)
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
    const partKeys = pageElements.map((page) => page.dataset.reportOutputPart)
    const printStyles = document.createElement('style')
    printStyles.textContent = parts.map((part) => `@page printpart${part.index}{size:${part.geometry.widthMm}mm ${part.geometry.heightMm}mm;margin:0}`).join('')
    document.head.append(printStyles)
    const localCounts = new Map()
    const pages = pageElements.map((page, index) => {
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
      else if (part) { footer.textContent = `${pageNo} / ${pageTotal}`; footer.classList.add('report-number-resolved') }
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
    return { pages, diagnostics, workbookRows, sourceCells, cellFragmentLedger, styles: [...document.styleSheets].flatMap((sheet) => [...sheet.cssRules].map((rule) => rule.cssText)).join('\n') }
  }, { height: bodyHeight * 96 / 25.4, css, parts, format: input.format })
  const actual = new Set(result.pages.flatMap((page) => page.markers.map((item) => item.marker)))
  const fragmentCounts = new Map()
  for (const item of result.pages.flatMap((page) => page.markers)) {
    item.fragmentIndex = fragmentCounts.get(item.marker) || 0
    fragmentCounts.set(item.marker, item.fragmentIndex + 1)
  }
  const expected = (input.expectedMarkers || []).map((marker) => typeof marker === 'string' ? marker : marker.marker)
  if (expected.some((marker) => !actual.has(marker)) || (expected.length && [...actual].some((marker) => !expected.includes(marker)))) throw new Error('REPORT_MARKER_COVERAGE_MISMATCH')
  const pdf = input.preview && input.format === 'Html' ? null : await page.pdf({ preferCSSPageSize: true, printBackground: true })
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
