import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
const manifest = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'))
const approved = {
  '@univerjs/core': '1.3.1',
  '@univerjs/presets': '1.3.1',
  '@univerjs/preset-sheets-core': '1.3.1',
  '@univerjs/preset-sheets-filter': '1.3.1',
  '@univerjs/preset-sheets-sort': '1.3.1',
  exceljs: '4.4.0',
  jszip: '3.10.2',
}
for (const [name, version] of Object.entries(approved)) {
  assert.equal(
    manifest.dependencies[name],
    version,
    `${name} must retain the approved exact version`,
  )
  const installed = JSON.parse(
    await readFile(path.join(root, 'node_modules', name, 'package.json'), 'utf8'),
  )
  assert.equal(installed.version, version)
  assert.ok(
    ['Apache-2.0', 'MIT', '(MIT OR GPL-3.0-or-later)'].includes(installed.license),
    `${name}: review license ${installed.license}`,
  )
}
const lock = await readFile(path.join(root, 'pnpm-lock.yaml'), 'utf8')
assert.ok(
  !/@univerjs(?:-pro|\/[^\s:'"]*(?:-pro|pro-))/.test(lock),
  'Univer Pro packages require separate approval',
)
for (const section of ['dependencies', 'devDependencies', 'optionalDependencies']) {
  for (const name of Object.keys(manifest[section] ?? {})) {
    if (name.startsWith('@univerjs'))
      assert.ok(name in approved, `${name} is not an approved direct dependency`)
  }
}
const checked = new Set()
for (const folder of await readdir(path.join(root, 'node_modules/.pnpm'))) {
  if (!folder.startsWith('@univerjs+')) continue
  const scope = path.join(root, 'node_modules/.pnpm', folder, 'node_modules/@univerjs')
  for (const name of await readdir(scope)) {
    const pkg = JSON.parse(await readFile(path.join(scope, name, 'package.json'), 'utf8'))
    assert.equal(
      pkg.version,
      name === 'icons' ? '1.46.0' : '1.3.1',
      `${pkg.name}: unapproved transitive version`,
    )
    assert.equal(
      pkg.license,
      name === 'icons' ? 'MIT' : 'Apache-2.0',
      `${pkg.name}: license changed`,
    )
    checked.add(pkg.name)
  }
}
assert.ok(checked.size >= 5, 'Installed Univer packages missing')
console.log(
  `Approved exact versions verified; ${checked.size} installed Univer packages checked; no Pro packages.`,
)
