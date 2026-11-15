import { createRequire } from 'node:module'
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const example = dirname(fileURLToPath(import.meta.url))
const frontend = resolve(example, '../../src/frontend')
const fromFrontend = createRequire(resolve(frontend, 'package.json'))
const { build } = await import(pathToFileURL(fromFrontend.resolve('vite')).href)
const { default: vue } = await import(pathToFileURL(fromFrontend.resolve('@vitejs/plugin-vue')).href)

const result = await build({
  root: frontend,
  configFile: false,
  envDir: false,
  publicDir: false,
  plugins: [vue()],
  resolve: {
    alias: { '@': resolve(frontend, 'src') },
    dedupe: ['vue', 'vue-i18n', 'element-plus', '@antv/x6'],
  },
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    write: false,
    lib: { entry: resolve(example, 'main.ts'), name: 'WorkflowSample', formats: ['iife'] },
  },
})

const output = (Array.isArray(result) ? result : [result]).flatMap((bundle) => bundle.output)
const scripts = output.filter((item) => item.type === 'chunk')
const styles = output.filter((item) => item.type === 'asset' && item.fileName.endsWith('.css'))
if (scripts.length !== 1 || output.length !== scripts.length + styles.length) {
  throw new Error('样例必须只生成一个脚本和内联样式，请检查新增加的静态资产或动态导入。')
}
const css = styles.map((asset) => asset.source).join('\n').replace(/<\/style/gi, '<\\/style')
const js = scripts[0].code.replace(/<\/script/gi, '<\\/script')
const html = `<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'">
<title>生产工作流测试样例</title><style>${css}</style></head>
<body><div id="app"></div><script>${js}</script></body></html>
`
const target = process.argv[2]
  ? resolve(process.argv[2])
  : resolve(example, '../../../visual-previews/production-workflow/index.html')
await mkdir(dirname(target), { recursive: true })
await writeFile(target, html, 'utf8')
console.log(`独立样例：${target}`)
