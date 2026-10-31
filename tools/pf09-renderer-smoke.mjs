// Integration checks for the real renderer process (uses operator-owned assets).
import { readFile, writeFile, mkdir, rm } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { resolve, join } from 'node:path'

const manifest = process.env.PF09_RENDERER_MANIFEST
if (!manifest) throw new Error('Set PF09_RENDERER_MANIFEST')
const manifestHash = createHash('sha256').update(await readFile(manifest)).digest('hex')
const root = resolve(process.argv[2] || '.run/pf09-c3-preflight/renderer-smoke')
await mkdir(root, { recursive: true })
const control = (id, y, content, after = []) => `<div data-report-marker="${id}" data-report-control="${JSON.stringify({ id, type: 'Text', xMm: 0, yMm: y, widthMm: 170, heightMm: 5, canGrow: true, flow: { mode: 'Follow', afterControlIds: after } }).replaceAll('"', '&quot;')}">${content}</div>`
const band = (content) => `<section data-report-band="{&quot;heightMm&quot;:20}">${content}</section>`
const fixedImage = (display) => band(`<div data-report-marker="m1" data-report-control="${JSON.stringify({ id: 'image', type: 'Image', xMm: 0, yMm: 0, widthMm: 20, heightMm: 10, canGrow: false }).replaceAll('"', '&quot;')}"><img style="display:${display};width:100%;height:10mm;object-fit:contain" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="></div>`)
const simpleHtml = '<section data-report-part="first"><div data-report-marker="m1">' + Array.from({ length: 45 }, (_, i) => `<p>FIRST${i} 首份报告</p>`).join('') + '</div></section><section data-report-part="second"><div data-report-marker="m2">' + Array.from({ length: 45 }, (_, i) => `<p>SECOND${i} 第二份报告</p>`).join('') + '</div></section>'
const cases = [
  { name: 'static-checkbox-states', ok: true, html: '<p data-report-marker="m1"><span role="checkbox" aria-checked="true">✓</span><span role="checkbox" aria-checked="false"></span><span role="checkbox" aria-checked="mixed">−</span></p>', expectedMarkers: ['m1'] },
  { name: 'invalid-checkbox-state', ok: false, html: '<span role="checkbox" aria-checked="unknown">bad</span>', errorCode: 'REPORT_HTML_ATTRIBUTE_DENIED' },
  { name: 'interactive-role-denied', ok: false, html: '<span role="button">bad</span>', errorCode: 'REPORT_HTML_ATTRIBUTE_DENIED' },
  { name: 'single-huge-cell', ok: true, html: '<div data-report-marker="m1"><table><thead><tr><th>长记录</th><th>相邻值</th></tr></thead><tbody><tr><td><p>' + Array.from({ length: 600 }, (_, index) => `CELL${index} 已填写的连续正文 `).join('') + '</p></td><td>NEIGHBOR-ONCE</td></tr></tbody></table></div>', expectedMarkers: ['m1'] },
  { name: 'fixed-image-block', ok: true, html: fixedImage('block'), expectedMarkers: ['m1'] },
  { name: 'fixed-image-inline-overflow', ok: false, html: fixedImage('inline'), expectedMarkers: ['m1'], errorCode: 'REPORT_LAYOUT_FIXED_CONTROL_OVERFLOW' },
  { name: 'encoded-rich-record', ok: true, html: '<div data-report-marker="m1"><section data-report-html="true">&lt;p&gt;中文记录 &lt;input value=&quot;RICH-FILLED&quot;&gt;&lt;/p&gt;</section></div>', expectedMarkers: ['m1'] },
  { name: 'rich-wrapper-escape', ok: false, html: '<div data-report-marker="m1"><section data-report-html="true">&lt;/section&gt;&lt;/div&gt;&lt;p data-report-marker=&quot;forged&quot;&gt;forged&lt;/p&gt;</section></div>', expectedMarkers: ['m1'] },
  { name: 'filled-record-scopes', ok: true, html: '<!doctype html><html><head><meta charset="utf-8"></head><body><section data-report-form-values="{&quot;same&quot;:{&quot;value&quot;:&quot;FILLED-ONE&quot;}}"><p data-report-marker="m1"><input id="same" value="original"></p></section><section data-report-form-values="{&quot;same&quot;:{&quot;value&quot;:&quot;FILLED-TWO&quot;}}"><p data-report-marker="m2"><input id="same" value="original"></p></section></body></html>', expectedMarkers: ['m1', 'm2'] },
  { name: 'long-record', ok: true, html: `<div data-report-marker="m1"><table><tbody><tr><td>${Array.from({ length: 100 }, (_, i) => `<p>LINE${i} 中文实际填写内容跨页保存</p>`).join('')}</td></tr></tbody></table></div>`, expectedMarkers: ['m1'] },
  { name: 'follow-grown-control', ok: true, html: band(control('m1', 0, '<p>第一段</p><p>增长内容</p><p>第三段</p>') + control('m2', 10, '后续签名栏', ['m1'])), expectedMarkers: ['m1', 'm2'] },
  { name: 'xlsx-normalized-rows', ok: true, format: 'Xlsx', html: band(control('m1', 0, '已填写 123.450')), expectedMarkers: ['m1'] },
  { name: 'frozen-image-scaled', ok: true, html: '<div data-report-marker="m1"><img width="600" height="2000" alt="冻结图元" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="></div>', expectedMarkers: ['m1'] },
  { name: 'a3-landscape', ok: true, html: '<p data-report-marker="m1">A3 横向</p>', expectedMarkers: ['m1'], paper: { size: 'A3', orientation: 'Landscape', margin: { top: 10, right: 12, bottom: 14, left: 16 } } },
  { name: 'custom-paper', ok: true, html: '<p data-report-marker="m1">自定义纸张</p>', expectedMarkers: ['m1'], paper: { size: 'Custom', widthMm: 160, heightMm: 220 } },
  { name: 'repeated-page-bands', ok: true, html: `<section data-report-band="{&quot;heightMm&quot;:5,&quot;kind&quot;:&quot;PageHeader&quot;}">${control('head', 0, 'REPEATED-HEADER')}</section><section data-report-band="{&quot;heightMm&quot;:5,&quot;kind&quot;:&quot;PageFooter&quot;}">${control('foot', 0, 'REPEATED-FOOTER')}</section><div data-report-marker="m1">${Array.from({ length: 90 }, (_, i) => `<p>BODY${i} 实际记录</p>`).join('')}</div>`, expectedMarkers: ['m1'] },
  ...['Preserve', 'Global'].map((mode) => ({ name: `simple-${mode.toLowerCase()}`, ok: true, html: simpleHtml, expectedMarkers: ['m1', 'm2'], parts: [{ key: 'first', pageNumberMode: mode, paper: { size: 'A4' } }, { key: 'second', pageNumberMode: mode, paper: { size: 'A4', orientation: 'Landscape' } }] })),
  { name: 'overlap-rejected', ok: false, html: band(control('m1', 0, '<p>第一段</p><p>增长内容</p><p>第三段</p>') + control('m2', 1, '后续签名栏')), expectedMarkers: ['m1', 'm2'] },
  { name: 'active-script', ok: false, html: '<div><script>fetch("https://example.com")</script></div>' },
  { name: 'remote-image', ok: false, html: '<img src="https://example.com/a.png">' },
  { name: 'event-handler', ok: false, html: '<p onclick="alert(1)">bad</p>' },
  { name: 'unsaved-form-value', ok: false, html: '<p><input name="unsaved"></p>' },
  { name: 'external-style', ok: false, html: '<p>bad</p>', css: '@import "https://example.com/a.css"' },
  { name: 'manifest-tamper', ok: false, html: '<p>bad</p>', engineManifestHash: '0'.repeat(64) },
  { name: 'marker-loss', ok: false, html: '<p>missing marker</p>', expectedMarkers: ['m1'] },
  { name: 'confirmed-cancel', ok: false, cancel: true, html: Array.from({ length: 500 }, () => '<p>需要取消的实际排版任务</p>').join('') },
]
const results = []
for (const item of cases) {
  const directory = join(root, item.name); await mkdir(directory, { recursive: true })
  const input = join(directory, 'input.json')
  await rm(join(directory, 'stopped.json'), { force: true })
  if (item.cancel) await writeFile(join(directory, 'cancel.request'), 'cancel')
  await writeFile(input, JSON.stringify({ css: '', paper: { size: 'A4' }, engineManifestHash: manifestHash, ...item }))
  const processResult = spawnSync(process.execPath, [resolve('tools/pf09-render-report.mjs'), '--input', input, '--output', directory], { env: process.env, timeout: 60000, windowsHide: true, encoding: 'utf8' })
  let validOutput = true
  if (item.errorCode) { const receipt = JSON.parse(await readFile(join(directory, 'renderer-stopped.json'))); validOutput = receipt.errorCode === item.errorCode }
  if (item.cancel) {
    const receipt = JSON.parse(await readFile(join(directory, 'stopped.json')))
    validOutput = processResult.status === 2 && receipt.reason === 'Cancelled' && receipt.engineManifestHash === manifestHash
  }
  if (item.ok && processResult.status === 0) {
    const output = JSON.parse(await readFile(join(directory, 'result.json')))
    const ledger = output.pages.flatMap((page) => page.markers)
    validOutput = item.expectedMarkers.every((marker) => ledger.filter((entry) => entry.marker === marker).every((entry, index) => entry.fragmentIndex === index))
    if (item.name === 'filled-record-scopes') {
      const html = await readFile(join(directory, 'page-1.html'), 'utf8')
      validOutput &&= html.includes('FILLED-ONE') && html.includes('FILLED-TWO') && !html.includes('<input')
    }
    if (item.name === 'xlsx-normalized-rows') validOutput &&= output.workbookRows?.[0]?.[0] === '已填写 123.450'
    if (item.name === 'static-checkbox-states') { const html = await readFile(join(directory, 'page-1.html'), 'utf8'); validOutput &&= ['true', 'false', 'mixed'].every(state => html.includes(`aria-checked="${state}"`)) }
    if (item.name === 'single-huge-cell') validOutput &&= output.pages.length > 1 && output.sourceCells.length > 0 && output.cellFragmentLedger.some((fragment) => fragment.end > fragment.start)
    if (item.name === 'encoded-rich-record') { const html = await readFile(join(directory, 'page-1.html'), 'utf8'); validOutput &&= html.includes('RICH-FILLED') && !html.includes('<input') }
  }
  const passed = !processResult.error && (processResult.status === 0) === item.ok && validOutput
  results.push({ name: item.name, passed, exitCode: processResult.status })
  console.log(JSON.stringify(results.at(-1)))
}
await writeFile(join(root, 'summary.json'), JSON.stringify(results, null, 2))
if (results.some((result) => !result.passed)) process.exitCode = 1
