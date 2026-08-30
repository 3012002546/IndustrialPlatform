import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import { localeMessages } from '@/localization/i18n'

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const repositoryRoot = resolve(frontendRoot, '..', '..')

const publicShellFiles = [
  'src/components/base/AppPage.vue',
  'src/components/management/AppQueryPanel.vue',
  'src/components/management/AppDataTable.vue',
  'src/components/shell/AppLockOverlay.vue',
  'src/components/shell/PcExperienceModeControl.vue',
  'src/components/shell/PcWorkspaceTabs.vue',
  'src/components/shell/PlatformCommandSearch.vue',
  'src/components/shell/PlatformContextSwitcher.vue',
  'src/components/shell/PlatformFunctionTree.vue',
  'src/components/shell/PlatformServiceStatus.vue',
  'src/components/shell/PlatformToolRail.vue',
  'src/components/shell/WorkspaceTabLimitDialog.vue',
  'src/components/shell/PlatformSessionControls.vue',
  'src/components/systemData/SystemDataAdminFrame.vue',
  'src/components/systemData/SystemDataRuntimeStatus.vue',
  'src/layouts/OperationLayout.vue',
  'src/layouts/PcLayout.vue',
  'src/pages/pc/ProfilePage.vue',
  'src/pages/pc/PcOperationHomePage.vue',
]

const forbiddenPlatformCopy = [
  '工作区已锁定',
  '当前密码',
  '搜索菜单',
  '全局搜索',
  '工作台标签',
  '查询与操作',
  '管理接口不可用',
  '正在保存或读取，请勿重复提交。',
  'SystemData 运行策略暂处于降级状态',
  '当前快照不可用',
  '列设置',
  '调整列显隐、顺序、固定与宽度',
]

function readRepositoryFile(relativePath: string): string {
  return readFileSync(resolve(repositoryRoot, relativePath), 'utf8')
}

function readLocalePath(locale: keyof typeof localeMessages, path: string): unknown {
  return path.split('.').reduce<unknown>((value, part) => {
    if (typeof value !== 'object' || value === null) return undefined
    return (value as Record<string, unknown>)[part]
  }, localeMessages[locale])
}

describe('platform copy resource boundary', () => {
  it('keeps shared shell and list copy in locale resources', () => {
    for (const relativePath of publicShellFiles) {
      const source = readRepositoryFile(`src/frontend/${relativePath}`)
      for (const copy of forbiddenPlatformCopy) {
        expect(source, `${relativePath} contains public copy: ${copy}`).not.toContain(copy)
      }
    }
  })

  it('keeps production operation shell copy in locale resources', () => {
    for (const relativePath of ['src/layouts/OperationLayout.vue', 'src/pages/pc/PcOperationHomePage.vue']) {
      const source = readRepositoryFile(`src/frontend/${relativePath}`)
      expect(source, `${relativePath} contains hardcoded Chinese copy`).not.toMatch(/[\u3400-\u9fff]/)
    }
  })

  it('keeps directional platform keys available in both supported locales', () => {
    const requiredKeys = [
      'common.query.title',
      'common.table.columnSettings',
      'common.table.selectionSummary',
      'shell.commandSearch.placeholder',
      'shell.copy.tabLimitTitle',
      'identity.user.copy.dialogDetail',
      'systemData.copy.degraded',
    ]
    for (const locale of ['zh-CN', 'en-US'] as const) {
      for (const key of requiredKeys) {
        expect(readLocalePath(locale, key), `${locale}.${key}`).toEqual(expect.any(String))
      }
    }
  })

  it('keeps the reusable platform documentation links present', () => {
    const standard = readRepositoryFile('docs/frontend/platform-shell-and-list-page-standard.md')
    const boundaries = readRepositoryFile('docs/architecture/capability-delivery-boundaries.md')
    const gate = readRepositoryFile('docs/architecture/pf04-redecision-gate.md')
    expect(standard).toContain('AppDataTable')
    expect(boundaries).toContain('独立启动验收')
    expect(gate).toContain('Build')
    expect(gate).toContain('Skip')
  })
})
