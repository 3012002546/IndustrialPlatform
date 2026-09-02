import { describe, expect, it } from 'vitest'

import { localeMessages } from '@/localization/i18n'

const sources = import.meta.glob('../../src/pages/pc/identity/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>

function pageSource(path: string): string {
  const source = sources[`../../src/pages/pc/identity/${path}`]
  if (source === undefined) throw new Error(`Missing page source: ${path}`)
  return source
}

describe('Identity access page composition', () => {
  it.each([
    ['IdentityUserGroupsPage.vue', 'groups-page'],
    ['IdentityRolesPage.vue', 'roles-page'],
    ['IdentityAuditsPage.vue', 'audits-page'],
  ])('%s uses the shared page and query surfaces', (file, pageClass) => {
    const source = pageSource(file)
    expect(source).toContain("import AppPage from '@/components/base/AppPage.vue'")
    expect(source).toContain("import AppQueryPanel from '@/components/management/AppQueryPanel.vue'")
    expect(source).toContain('<AppPage')
    expect(source).toContain('<AppQueryPanel')
    expect(source).toContain(`class="${pageClass}"`)
    expect(source).not.toMatch(/<AppDataTable[\s\S]*toolbar-title=/)
  })

  it.each([
    ['IdentityUserGroupsPage.vue', 'groups-page'],
    ['IdentityRolesPage.vue', 'roles-page'],
    ['sso/SsoProvidersPage.vue', 'sso-providers-page'],
    ['sso/SsoClientsPage.vue', 'sso-clients-page'],
  ])('%s uses the shared form drawer for structured forms', (file, pageClass) => {
    const source = pageSource(file)
    expect(source).toContain("import AppFormDrawer from '@/components/management/AppFormDrawer.vue'")
    expect(source).toContain('<AppFormDrawer')
    expect(source).toContain(`class="${pageClass}"`)
  })

  it.each([
    ['IdentityPermissionsPage.vue', 'permissions-page'],
    ['sso/SsoProvidersPage.vue', 'sso-providers-page'],
    ['sso/SsoClientsPage.vue', 'sso-clients-page'],
  ])('%s is represented by a shared page surface', (file, pageClass) => {
    const source = pageSource(file)
    expect(source).toContain("import AppPage from '@/components/base/AppPage.vue'")
    expect(source).toContain('<AppPage')
    expect(source).toContain(`class="${pageClass}"`)
  })

  it('keeps the permission catalog as a read-only tree rather than a second table', () => {
    const source = pageSource('IdentityPermissionsPage.vue')
    expect(source).not.toContain('AppDataTable')
    expect(source).toContain('<el-tree')
  })

  it('provides page copy for both supported locales', () => {
    for (const locale of ['zh-CN', 'en-US'] as const) {
      const management = localeMessages[locale].identity.management
      expect(management.userGroups.title).toEqual(expect.any(String))
      expect(management.roles.title).toEqual(expect.any(String))
      expect(management.permissions.title).toEqual(expect.any(String))
      expect(management.audits.title).toEqual(expect.any(String))
      expect(management.ssoProviders.title).toEqual(expect.any(String))
      expect(management.ssoClients.title).toEqual(expect.any(String))
    }
  })

  it.each([
    'IdentityUserGroupsPage.vue',
    'IdentityRolesPage.vue',
    'IdentityPermissionsPage.vue',
    'IdentityAuditsPage.vue',
    'sso/SsoProvidersPage.vue',
    'sso/SsoClientsPage.vue',
  ])('%s does not retain the legacy structured overlay containers', (file) => {
    const source = pageSource(file)
    expect(source).not.toContain('<el-dialog')
    expect(source).not.toContain('<el-drawer')
  })
})
