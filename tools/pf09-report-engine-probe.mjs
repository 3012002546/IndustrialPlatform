// Isolated candidate probe: this is not the production report renderer.
// node tools/pf09-report-engine-probe.mjs <paged.polyfill.js> [output-folder]
import { createRequire } from 'node:module'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { createHash } from 'node:crypto'
import { fragmentReportRows, installReportTableHeaders } from './pf09-report-row-fragments.mjs'

const require = createRequire(new URL('../src/frontend/package.json', import.meta.url))
const { chromium } = require('@playwright/test')
const bundlePath = resolve(process.argv[2] || '.run/pf09-c3-preflight/package/dist/paged.polyfill.js')
const output = resolve(process.argv[3] || '.run/pf09-c3-preflight/results')
const preprocess = process.argv.includes('--fragment-rows')
await mkdir(output, { recursive: true })
const bundle = await readFile(bundlePath, 'utf8')
const hash = (value) => createHash('sha256').update(value).digest('hex')
const paragraph = (id) => `<p><span data-token="${id}">${id}</span> 已填写的中文生产记录，包含工序参数、操作说明以及实际执行结果，用于验证自动换行和跨页连续排版。</p>`
const tokens = (prefix, count) => Array.from({ length: count }, (_, index) => `${prefix}${String(index).padStart(4, '0')}TOKEN`)
const fixtures = [
  { name: 'paragraphs', tokens: tokens('P', 150), html: (ids) => ids.map(paragraph).join('') },
  { name: 'normal-table', repeatHeaderText: '重复表头', tokens: tokens('R', 90), html: (ids) => `<table><thead><tr><th>重复表头</th><th>填写值</th></tr></thead><tbody>${ids.map((id) => `<tr><td><span data-token="${id}">${id}</span></td><td>123.450</td></tr>`).join('')}</tbody></table>` },
  { name: 'oversize-cell', tokens: tokens('C', 140), html: (ids) => `<table><thead><tr><th>超单页长单元格</th><th>相邻短单元格</th></tr></thead><tbody><tr><td>${ids.map(paragraph).join('')}</td><td>只出现一次的填写值</td></tr></tbody></table>` },
  { name: 'rowspan-nested', minTokenLeftRatio: 0.4, repeatHeaderText: '合并', tokens: tokens('N', 65), html: (ids) => `<table><thead><tr><th>合并</th><th>内层表</th></tr></thead><tbody><tr><td rowspan="2">跨行填写状态：已确认</td><td><table><tbody>${ids.map((id) => `<tr><td><span data-token="${id}">${id}</span>嵌套明细</td></tr>`).join('')}</tbody></table></td></tr><tr><td>尾部实际值</td></tr></tbody></table>` },
  { name: 'continuous-blocks', tokens: tokens('B', 45), html: (ids) => `<section>${ids.slice(0, 12).map(paragraph).join('')}</section><section>${ids.slice(12).map(paragraph).join('')}</section>` },
  { name: 'single-long-paragraph', tokens: tokens('L', 100), html: (ids) => `<table><thead><tr><th>单段超长填写内容</th></tr></thead><tbody><tr><td><p>${ids.map((id) => `<span data-token="${id}">${id}</span> 单个段落保留中文和实际填写内容，连续分页不得遗漏、重复或者裁切。`).join('')}</p></td></tr></tbody></table>` },
  { name: 'remaining-space', firstTokenMaxPage: 1, tokens: tokens('F', 70), html: (ids) => `<p>前置说明，占用第一页的一部分。</p><p>后续长表必须从本页剩余空间开始。</p><table><thead><tr><th>继续填写记录</th><th>状态</th></tr></thead><tbody><tr><td>${ids.map(paragraph).join('')}</td><td>首次填写值</td></tr></tbody></table>` },
]
const browser = await chromium.launch({ headless: true })
const manifest = { candidate: 'pagedjs@0.4.3', bundleSha256: hash(bundle), chromium: browser.version(), browserExecutable: chromium.executablePath(), fixtures: [] }
try {
  for (const orientation of ['portrait', 'landscape']) {
    for (const fixture of fixtures) {
      const page = await browser.newPage()
      const errors = []
      page.on('pageerror', (error) => errors.push(error.message))
      // No fixture or renderer may fetch external images/fonts/scripts.
      await page.route('**/*', (route) => route.abort())
      const timer = setTimeout(() => void page.close(), 25000)
      const name = `${fixture.name}-${orientation}`
      try {
        await page.setContent('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><body><main id="source"></main><div id="pages"></div></body></html>')
        await page.evaluate(() => { window.PagedConfig = { auto: false } })
        await page.addScriptTag({ content: bundle })
        const setup = await page.evaluate(async ({ html, orientation }) => {
          const source = document.querySelector('#source')
          source.innerHTML = html
          source.style.width = `${orientation === 'portrait' ? 180 : 267}mm`
          await document.fonts.ready
          const css = `@page { size:A4 ${orientation}; margin:15mm; @bottom-center { content:counter(page) " / " counter(pages); } } body {font-family:"Microsoft YaHei",sans-serif;font-size:10pt;line-height:1.3} p {margin:0 0 3mm;orphans:2;widows:2} table {width:100%;border-collapse:collapse;table-layout:fixed} td,th {border:0.2mm solid black;padding:2mm;overflow-wrap:anywhere;vertical-align:top} thead {display:table-header-group}`
          const measurementStyle = document.createElement('style'); measurementStyle.dataset.pagedjsIgnore = 'true'; measurementStyle.textContent = css; document.head.append(measurementStyle)
          await document.fonts.ready
          return { css, height: ((orientation === 'portrait' ? 297 : 210) - 30) * 96 / 25.4 }
        }, { html: fixture.html(fixture.tokens), ids: fixture.tokens, orientation })
        await page.addScriptTag({ content: `window.fragmentReportRows = ${fragmentReportRows.toString()}` })
        if (preprocess) await page.addScriptTag({ content: `(${installReportTableHeaders.toString()})(window.Paged)` })
        const preprocessing = preprocess ? await page.evaluate((height) => window.fragmentReportRows(document.querySelector('#source'), height), setup.height) : []
        const result = await page.evaluate(async ({ ids, css, minTokenLeftRatio, repeatHeaderText }) => {
          const source = document.querySelector('#source')
          const content = source.innerHTML
          source.remove()
          const flow = await new window.Paged.Previewer().preview(content, [{ 'https://renderer.invalid/fixture.css': css }], document.querySelector('#pages'))
          await document.fonts.ready
          const pages = [...document.querySelectorAll('#pages .pagedjs_page')]
          const text = pages.map((page) => page.textContent).join('\n')
          const missing = ids.filter((id) => !text.includes(id))
          const duplicates = ids.filter((id) => text.split(id).length !== 2)
          const overflow = []
          const geometryErrors = []
          const headerMissing = []
          const markers = []
          for (const [pageIndex, page] of pages.entries()) {
            const content = page.querySelector('.pagedjs_page_content').getBoundingClientRect()
            for (const element of page.querySelectorAll('[data-token]')) {
              const box = element.getBoundingClientRect()
              markers.push({ token: element.dataset.token, page: pageIndex + 1, left: box.left - content.left, top: box.top - content.top, bottom: box.bottom - content.top })
              if (minTokenLeftRatio && box.left - content.left < content.width * minTokenLeftRatio) geometryErrors.push({ token: element.dataset.token, page: pageIndex + 1 })
              if (box.bottom > content.bottom + 2 || box.top < content.top - 2 || box.right > content.right + 2) overflow.push({ token: element.dataset.token, page: pageIndex + 1 })
            }
            if (repeatHeaderText && page.querySelector('[data-token]') && !page.textContent.includes(repeatHeaderText)) headerMissing.push(pageIndex + 1)
          }
          const size = pages[0].getBoundingClientRect()
          return { pages: flow.total, pageWidthPx: size.width, pageHeightPx: size.height, missing, duplicates, overflow, geometryErrors, headerMissing, markers }
        }, { ids: fixture.tokens, css: setup.css, minTokenLeftRatio: fixture.minTokenLeftRatio, repeatHeaderText: fixture.repeatHeaderText })
        const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true })
        await writeFile(join(output, `${name}.pdf`), pdf)
        await page.screenshot({ path: join(output, `${name}.png`), fullPage: false })
        const firstMarker = result.markers.find((marker) => marker.token === fixture.tokens[0])
        const spaceUsed = !fixture.firstTokenMaxPage || firstMarker?.page <= fixture.firstTokenMaxPage
        const passed = result.pages > 0 && result.missing.length === 0 && result.duplicates.length === 0 && result.overflow.length === 0 && result.geometryErrors.length === 0 && result.headerMissing.length === 0 && spaceUsed && errors.length === 0
        manifest.fixtures.push({ name, passed, ...result, preprocessing, errors, pdfSha256: hash(pdf) })
        console.log(JSON.stringify({ name, passed, pages: result.pages, missing: result.missing.length, duplicates: result.duplicates.length, overflow: result.overflow.length }))
      } catch (error) {
        manifest.fixtures.push({ name, passed: false, error: error.message, errors })
        console.log(JSON.stringify({ name, passed: false, error: error.message }))
      } finally {
        clearTimeout(timer)
        await page.close()
      }
    }
  }
} finally {
  await browser.close()
  await writeFile(join(output, 'manifest.json'), JSON.stringify(manifest, null, 2))
}
if (manifest.fixtures.some((fixture) => !fixture.passed)) process.exitCode = 1
