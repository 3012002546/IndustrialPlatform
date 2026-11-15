// Exercises the production renderer with one frozen input. No database or MES access.
import assert from 'node:assert/strict'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'

const out = resolve(process.argv[2] || '.run/r5/exports')
await mkdir(out, { recursive: true })
const manifest = process.env.PF09_RENDERER_MANIFEST
if (!manifest) throw Error('Set PF09_RENDERER_MANIFEST')
const engineManifestHash = createHash('sha256').update(await readFile(manifest)).digest('hex')
const escape = s => s.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;')
const control = (id, type, html, heightMm = 10) => `<section data-report-band="${escape(JSON.stringify({ heightMm, kind: id.startsWith('row-') ? 'Detail' : 'ReportHeader', keepWithNext: id === 'title' }))}"><div data-report-marker="${id}" data-report-control="${escape(JSON.stringify({ id, type, widthMm: 170, heightMm, xMm: 0, yMm: 0, canGrow: true, keepTogether: id.startsWith('row-') }))}">${html}</div></section>`
const table = '<table><thead><tr><th>工序</th><th>数据</th></tr></thead><tbody>' + Array.from({ length: 70 }, (_, i) => `<tr><td>ROW-${String(i).padStart(3, '0')}</td><td>${i === 0 ? '001' : i === 1 ? '9007199254740993.001' : i === 2 ? '光刻胶,&quot;测试&quot;' : '值-' + i}</td></tr>`).join('') + '</tbody></table>'
const html = control('title', 'Text', '光刻胶导出测试') + control('table', 'Table', table)
const results = []
async function render(name, format, body, expectedMarkers, expectedError) {
  if (process.env.PF09_EXPORT_CASE && !name.includes(process.env.PF09_EXPORT_CASE)) return
  const folder = join(out, name); await mkdir(folder, { recursive: true })
  await writeFile(join(folder, 'input.json'), JSON.stringify({ html: '<html><body>' + body + '</body></html>', css: 'table{border-collapse:collapse;width:100%}td,th{border:1px solid;padding:2mm}body{font-family:sans-serif}', paper: { size: 'A4', orientation: 'Portrait' }, format, expectedMarkers, engineManifestHash }))
  const result = spawnSync(process.execPath, ['tools/pf09-render-report.mjs', '--input', join(folder, 'input.json'), '--output', folder], { windowsHide: true, encoding: 'utf8', timeout: 120000, env: { ...process.env, PF09_RENDERER_MANIFEST: manifest } })
  await writeFile(join(folder, 'process.log'), result.stdout + result.stderr)
  const receipt = JSON.parse(await readFile(join(folder, 'renderer-stopped.json'), 'utf8'))
  if (expectedError) assert.equal(receipt.errorCode, expectedError)
  else {
    assert.equal(result.status, 0, result.stderr.slice(-1500))
    const rendered = JSON.parse(await readFile(join(folder, 'result.json'), 'utf8'))
    assert.ok(rendered.pages.length >= 2)
    if (rendered.exportSha256) {
      const bytes = await readFile(join(folder, 'result.export.json'))
      assert.equal(createHash('sha256').update(bytes).digest('hex'), rendered.exportSha256)
      const model = JSON.parse(bytes)
      assert.equal(model.rows.length, name.startsWith('interrupted-') ? 73 : 72)
      assert.equal(model.rows[2][1], '001')
      assert.equal(model.rows[3][1], '9007199254740993.001')
      assert.equal(model.rows.at(-1)[0], 'ROW-069')
      const records = model.rows.filter(row => /^ROW-/.test(row[0]))
      assert.deepEqual(records.map(row => row[0]), Array.from({length:70}, (_, i) => 'ROW-' + String(i).padStart(3, '0')))
      if (name.startsWith('interrupted-')) {
        const tables = model.blocks.filter(block => block.type === 'table')
        assert.equal(tables.length, 2)
        assert.deepEqual(tables.map(table => [table.rows.length, table.headerRows]), name.includes('no-repeat') ? [[36, 0], [35, 0]] : [[36, 1], [36, 1]])
      }
      if (name.startsWith('detail-bands-')) {
        const tables = model.blocks.filter(block => block.type === 'table')
        assert.equal(model.blocks[0].heading, true); assert.equal(tables.length, 1); assert.equal(tables[0].headerRows, 1); assert.equal(tables[0].rows.length, 71)
      }
    }
    if (rendered.rasterPages) assert.equal(rendered.rasterPages.length, rendered.pages.length)
  }
  results.push({ name, exit: result.status, errorCode: receipt.errorCode }); console.log(JSON.stringify(results.at(-1)))
}
for (const format of ['Pdf', 'Html', 'Xls', 'Xlsx', 'Docx', 'Rtf', 'Mht', 'Text', 'Csv', 'Png', 'Jpeg']) await render(format, format, html, ['title', 'table'])
const records = [...table.matchAll(/<tr><td>[\s\S]*?<\/tr>/g)].map(match => match[0])
const detailBands = control('title', 'Text', '光刻胶导出测试') + records.map((row, i) => control('row-' + i, 'Table', '<table data-report-header-group="logical-collection" data-report-template="true" data-report-repeat-header="true">' + (i === 0 ? '<thead><tr><th>工序</th><th>数据</th></tr></thead>' : '') + '<tbody style="break-inside:avoid">' + row + '</tbody></table>')).join('')
for (const format of ['Docx', 'Rtf']) await render('detail-bands-' + format, format, detailBands, ['title', ...records.map((_, i) => 'row-' + i)])
const interrupted = detailBands.replace('<section data-report-band="' + escape(JSON.stringify({ heightMm:10, kind:'Detail', keepWithNext:false })) + '"><div data-report-marker="row-35"', control('interruption', 'Text', '工序说明：继续原明细集合') + '<section data-report-band="' + escape(JSON.stringify({ heightMm:10, kind:'Detail', keepWithNext:false })) + '"><div data-report-marker="row-35"')
assert.notEqual(interrupted, detailBands)
for (const format of ['Docx', 'Rtf']) await render('interrupted-' + format, format, interrupted, ['title', 'interruption', ...records.map((_, i) => 'row-' + i)])
for (const format of ['Docx', 'Rtf']) await render('interrupted-no-repeat-' + format, format, interrupted.replaceAll('data-report-repeat-header="true"', 'data-report-repeat-header="false"'), ['title', 'interruption', ...records.map((_, i) => 'row-' + i)])
for (const format of ['Xls', 'Csv', 'Text', 'Docx', 'Rtf']) await render('reject-html-' + format, format, control('html', 'Html', '<div>不可静默丢失的 HTML</div>'), ['html'], 'REPORT_EXPORT_CONTROL_UNSUPPORTED')
await writeFile(join(out, 'summary.json'), JSON.stringify(results, null, 2))
