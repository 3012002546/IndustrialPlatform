import { describe, expect, it } from 'vitest'
import { Avatar, House, Lock, Menu, Tools, UserFilled } from '@element-plus/icons-vue'

import {
  applyNavigationPolicy,
  applyPermissionPolicy,
  mapRuntimeNavigation,
  themePolicyToTenantDefaults,
} from '@/systemData/runtime/navigation'
import type { NavigationRuntimeNodeDto, ThemePolicyDto } from '@/api/systemData/types'
import type { NavigationGroup } from '@/components/navigation/types'
import { getDefaultPcNavigationGroups } from '@/components/navigation/navigation'
import { enUS } from '@/locales/en-US'
import { zhCN } from '@/locales/zh-CN'

const node = (overrides: Partial<NavigationRuntimeNodeDto>): NavigationRuntimeNodeDto => ({
  nodeNId: 'group-1',
  kind: 'Group',
  label: '系统',
  resourceNId: null,
  routeName: null,
  requiredPermissionNId: null,
  featureNId: null,
  iconKey: 'setting',
  displayOrder: 1,
  children: [],
  ...overrides,
})

type LegacyDeclaration = readonly [
  parentNodeNId: string | null,
  nodeNId: string,
  kind: 'Group' | 'Link',
  label: string,
  routeName: string | null,
  requiredPermissionNId: string | null,
  displayOrder: number,
]

// prettier-ignore
const legacyDeclarations: readonly LegacyDeclaration[] = [
  [null, 'navigation.group.workspace', 'Group', '工作台', null, null, 0],
  ['navigation.group.workspace', 'navigation.link.pc-home', 'Link', '首页', 'pc-home', 'platform.home.view', 0],
  ['navigation.group.workspace', 'navigation.link.terminal-preview', 'Link', '终端预览', 'terminal-preview', 'platform.pda.view', 1],
  [null, 'navigation.group.system', 'Group', '系统管理', null, null, 1],
  ['navigation.group.system', 'navigation.group.identity-access', 'Group', '身份与访问', null, null, 0],
  ['navigation.group.identity-access', 'navigation.link.identity-users', 'Link', '用户管理', 'identity-users', 'identity.user.view', 0],
  ['navigation.group.identity-access', 'navigation.link.identity-user-groups', 'Link', '用户组管理', 'identity-user-groups', 'identity.user-group.view', 1],
  ['navigation.group.identity-access', 'navigation.link.identity-roles', 'Link', '角色权限', 'identity-roles', 'identity.role.view', 2],
  ['navigation.group.identity-access', 'navigation.link.identity-permissions', 'Link', '权限目录', 'identity-permissions', 'identity.permission.view', 3],
  ['navigation.group.identity-access', 'navigation.link.identity-audits', 'Link', '登录审计', 'identity-audits', 'identity.audit.login.view', 4],
  ['navigation.group.identity-access', 'navigation.link.identity-sso-providers', 'Link', '企业登录源', 'sso-providers', 'identity.sso.view', 5],
  ['navigation.group.identity-access', 'navigation.link.identity-sso-clients', 'Link', 'SSO Client', 'sso-clients', 'identity.sso.view', 6],
  ['navigation.group.system', 'navigation.group.organization-people', 'Group', '组织与人员', null, null, 1],
  ['navigation.group.organization-people', 'navigation.link.systemdata-organizations', 'Link', '行政组织与岗位', 'systemdata-organizations', 'systemdata.organization.view', 0],
  ['navigation.group.organization-people', 'navigation.link.systemdata-assignments', 'Link', '用户任职', 'systemdata-assignments', 'systemdata.assignment.view', 1],
  ['navigation.group.system', 'navigation.group.menu-platform', 'Group', '菜单与平台配置', null, null, 2],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-navigation', 'Link', '菜单管理', 'systemdata-navigation', 'systemdata.navigation.view', 0],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-features', 'Link', '功能开关', 'systemdata-features', 'systemdata.feature.view', 1],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-themes', 'Link', '租户主题策略', 'systemdata-themes', 'systemdata.theme-policy.view', 2],
  ['navigation.group.system', 'navigation.group.service-operations', 'Group', '服务与运维', null, null, 3],
  ['navigation.group.service-operations', 'navigation.link.systemdata-services', 'Link', '服务目录', 'systemdata-services', 'systemdata.service-catalog.view', 0],
  ['navigation.group.service-operations', 'navigation.link.systemdata-service-initialization', 'Link', '服务初始化编排', 'systemdata-service-initialization', 'systemdata.service-initialization.view', 1],
]

function legacyDefaultNavigation(): NavigationRuntimeNodeDto[] {
  function build(declaration: LegacyDeclaration): NavigationRuntimeNodeDto {
    const [, nodeNId, kind, label, routeName, requiredPermissionNId, displayOrder] = declaration
    return node({
      nodeNId,
      kind,
      label,
      resourceNId:
        kind === 'Link'
          ? `systemdata.navigation.${nodeNId.slice('navigation.link.'.length)}`
          : null,
      routeName,
      requiredPermissionNId,
      iconKey: null,
      displayOrder,
      children: legacyDeclarations.filter(([parent]) => parent === nodeNId).map(build),
    })
  }

  return legacyDeclarations.filter(([parent]) => parent === null).map(build)
}

function localizedMessage(locale: typeof enUS, key: string): string | undefined {
  let value: unknown = locale
  for (const part of key.split('.')) {
    if (typeof value !== 'object' || value === null || !(part in value)) return undefined
    value = (value as Record<string, unknown>)[part]
  }
  return typeof value === 'string' ? value : undefined
}

function runtimeDefaults(): NavigationRuntimeNodeDto[] {
  return getDefaultPcNavigationGroups().map((group) =>
    node({
      nodeNId: `navigation.group.${group.id}`,
      label: group.label,
      kind: 'Group',
      children: [
        ...(group.sections ?? []).map((section) =>
          node({
            nodeNId: `navigation.group.${section.id}`,
            label: section.label,
            kind: 'Group',
            children: group.items
              .filter((item) => item.sectionId === section.id)
              .map((item) =>
                node({
                  nodeNId: `navigation.link.${item.id}`,
                  kind: 'Link',
                  label: item.label,
                  routeName: item.routeName,
                  children: [],
                }),
              ),
          }),
        ),
        ...group.items
          .filter((item) => item.sectionId === undefined)
          .map((item) =>
            node({
              nodeNId: `navigation.link.${item.id}`,
              kind: 'Link',
              label: item.label,
              routeName: item.routeName,
              children: [],
            }),
          ),
      ],
    }),
  )
}

function flattenLabels(groups: NavigationGroup[]): Array<{ labelKey: string; fallbackLabel: string }> {
  return groups.flatMap((group) => [
      { labelKey: group.labelKey ?? '', fallbackLabel: group.fallbackLabel ?? group.label },
    ...(group.sections ?? []).map((section) => ({
      labelKey: section.labelKey ?? '',
      fallbackLabel: section.fallbackLabel ?? section.label,
    })),
    ...group.items.flatMap((item) => [
      { labelKey: item.labelKey ?? '', fallbackLabel: item.fallbackLabel ?? item.label },
      ...(item.children ?? []).map((child) => ({
        labelKey: child.labelKey ?? '',
        fallbackLabel: child.fallbackLabel ?? child.label,
      })),
    ]),
  ])
}

describe('SystemData runtime navigation adapter', () => {
  it('localizes every platform-owned runtime group, section, and item in both locales', () => {
    const groups = mapRuntimeNavigation(runtimeDefaults())
    const labels = flattenLabels(groups)

    expect(labels.length).toBeGreaterThan(20)
    for (const entry of labels) {
      expect(entry.labelKey).toMatch(/^shell\.navigation\./)
      expect(localizedMessage(enUS, entry.labelKey)).toBeTruthy()
      expect(localizedMessage(zhCN, entry.labelKey)).toBeTruthy()
    }
  })

  it('uses route metadata for legacy node ids but preserves customized labels', () => {
    const groups = mapRuntimeNavigation([
      node({
        nodeNId: 'tenant.custom.system',
        label: '自定义系统',
        children: [
          node({
            nodeNId: 'tenant.custom.users',
            kind: 'Link',
            label: '自定义用户入口',
            routeName: 'identity-users',
          }),
        ],
      }),
      node({
        nodeNId: 'legacy.users',
        kind: 'Group',
        label: '系统管理',
        children: [
          node({
            nodeNId: 'legacy.identity-users',
            kind: 'Link',
            label: '用户管理',
            routeName: 'identity-users',
          }),
        ],
      }),
    ])

    const customGroup = groups.find((group) => group.id === 'tenant.custom.system')
    const builtInGroup = groups.find((group) => group.id === 'legacy.users')
    expect(customGroup?.labelKey).toBe('')
    expect(customGroup?.items[0]?.labelKey).toBe('')
    expect(builtInGroup?.items[0]).toMatchObject({
      labelKey: 'shell.navigation.item.identity-users',
      fallbackLabel: '用户管理',
    })
  })

  it('maps the authoritative ReferenceData root and preserves bilingual built-in labels', () => {
    const groups = mapRuntimeNavigation([
      node({
        nodeNId: 'navigation.group.reference-data',
        label: '基础配置',
        children: [
          node({
            nodeNId: 'navigation.link.reference-data-dictionaries',
            kind: 'Link',
            label: '字典管理',
            resourceNId: 'referencedata.navigation.dictionaries',
            routeName: 'reference-data-dictionaries',
            requiredPermissionNId: 'referencedata.dictionary.view',
            displayOrder: 0,
          }),
        ],
      }),
    ])

    expect(groups[0]).toMatchObject({
      id: 'navigation.group.reference-data',
      labelKey: 'shell.navigation.group.reference-data',
      fallbackLabel: '基础配置',
    })
    expect(groups[0]?.items[0]).toMatchObject({
      routeName: 'reference-data-dictionaries',
      labelKey: 'shell.navigation.item.reference-data-dictionaries',
      fallbackLabel: '字典管理',
    })
  })

  it('does not synthesize ReferenceData registrations into an old authoritative snapshot', () => {
    const groups = mapRuntimeNavigation(legacyDefaultNavigation())
    expect(groups.find((group) => group.id === 'navigation.group.reference-data')).toBeUndefined()
    expect(groups.flatMap((group) => group.items)).not.toEqual(
      expect.arrayContaining([
        expect.objectContaining({ routeName: 'reference-data-dictionaries' }),
      ]),
    )
  })

  it('does not add ReferenceData registrations to a customized legacy-shaped snapshot', () => {
    const renamed = legacyDefaultNavigation()
    renamed[1]!.label = '自定义系统'
    const hidden = legacyDefaultNavigation()
    hidden[1]!.children = hidden[1]!.children.filter(
      (child) => child.nodeNId !== 'navigation.group.service-operations',
    )

    for (const groups of [mapRuntimeNavigation(renamed), mapRuntimeNavigation(hidden)]) {
      expect(groups.flatMap((group) => group.items)).not.toEqual(
        expect.arrayContaining([
          expect.objectContaining({ routeName: 'reference-data-dictionaries' }),
        ]),
      )
    }
  })

  it('keeps the existing platform icons when published defaults have no icon key', () => {
    const defaults = getDefaultPcNavigationGroups()
    const groups = mapRuntimeNavigation(
      defaults.map((group) =>
        node({
          nodeNId: `navigation.group.${group.id}`,
          label: `Renamed ${group.id}`,
          iconKey: null,
          children: group.items.map((item) =>
            node({
              nodeNId: `custom.${item.id}`,
              kind: 'Link',
              label: `Renamed ${item.id}`,
              routeName: item.routeName,
              iconKey: ' ',
            }),
          ),
        }),
      ),
    )

    for (const group of defaults) {
      const mapped = groups.find((item) => item.id === `navigation.group.${group.id}`)
      expect(mapped?.icon).toBe(group.icon)
      for (const item of group.items) {
        expect(mapped?.items.find((entry) => entry.routeName === item.routeName)?.icon).toBe(
          item.icon,
        )
      }
    }
  })

  it.each([
    [' UserFilled ', UserFilled],
    ['user-filled', UserFilled],
    ['Avatar', Avatar],
    ['Lock', Lock],
    ['tools', Tools],
    ['unknown-icon', Menu],
    ['constructor', Menu],
  ])('uses an explicit icon key before the route default: %s', (iconKey, icon) => {
    const groups = mapRuntimeNavigation([
      node({
        iconKey,
        children: [node({ kind: 'Link', routeName: 'identity-users', iconKey })],
      }),
    ])

    expect(groups[0]?.icon).toBe(icon)
    expect(groups[0]?.items[0]?.icon).toBe(icon)
  })

  it('only fills icons and preserves a moved menu without restoring default nodes', () => {
    const groups = mapRuntimeNavigation([
      node({
        nodeNId: 'custom-root',
        iconKey: null,
        children: [
          node({
            nodeNId: 'service-operations',
            children: [
              node({
                nodeNId: 'moved-theme',
                kind: 'Link',
                label: 'Custom theme',
                routeName: 'systemdata-themes',
                iconKey: null,
                requiredPermissionNId: 'systemdata.theme-policy.view',
              }),
            ],
          }),
        ],
      }),
    ])
    const themeIcon = getDefaultPcNavigationGroups()
      .flatMap((group) => group.items)
      .find((item) => item.routeName === 'systemdata-themes')?.icon

    expect(groups).toHaveLength(1)
    expect(groups[0]?.icon).toBe(Menu)
    expect(groups[0]?.items).toHaveLength(1)
    expect(groups[0]?.items[0]).toMatchObject({
      id: 'moved-theme',
      label: 'Custom theme',
      sectionId: 'service-operations',
      permission: 'systemdata.theme-policy.view',
    })
    expect(groups[0]?.items[0]?.icon).toBe(themeIcon)
  })

  it('intersects permissions, removes disabled features, and prunes empty groups', () => {
    const groups = mapRuntimeNavigation([
      node({
        children: [
          node({
            nodeNId: 'allowed',
            kind: 'Link',
            label: '可见',
            routeName: 'systemdata-services',
            requiredPermissionNId: 'systemdata.service-catalog.view',
            featureNId: 'feature-on',
          }),
          node({
            nodeNId: 'denied',
            kind: 'Link',
            label: '无权限',
            routeName: 'forbidden',
            requiredPermissionNId: 'systemdata.feature.view',
          }),
        ],
      }),
      node({ nodeNId: 'empty', label: '空分组', children: [] }),
    ])

    expect(
      applyNavigationPolicy(groups, ['systemdata.service-catalog.view'], new Set(['feature-on'])),
    ).toEqual([
      expect.objectContaining({
        id: 'group-1',
        items: [expect.objectContaining({ id: 'allowed', routeName: 'systemdata-services' })],
      }),
    ])
    const systemGroup = groups.find((group) => group.id === 'group-1')
    expect(systemGroup?.labelKey).toBe('')
    expect(systemGroup?.items[0]?.labelKey).toBe('')
  })

  it('does not create a runtime RouterLink for an unregistered future page route', () => {
    const groups = mapRuntimeNavigation([
      node({
        children: [
          node({
            nodeNId: 'future-page',
            kind: 'Link',
            label: '未来页面',
            routeName: 'future-page-not-registered',
          }),
        ],
      }),
    ])

    expect(groups[0]?.items).toEqual([])
  })

  it('filters anyPermissions and does not leak filtered children or empty groups', () => {
    const groups: NavigationGroup[] = [
      {
        id: 'platform',
        label: '平台',
        icon: House,
        items: [
          {
            id: 'terminal',
            label: '终端',
            routeName: 'terminal-preview',
            anyPermissions: ['platform.pda.view', 'platform.mobile.view'],
          },
          {
            id: 'platform-parent',
            label: '容器',
            routeName: 'pc-home',
            children: [
              {
                id: 'users',
                label: '用户',
                routeName: 'identity-users',
                permission: 'identity.user.view',
              },
            ],
          },
        ],
      },
    ]

    const filtered = applyNavigationPolicy(groups, ['platform.home.view'], new Set())

    expect(filtered).toEqual([
      expect.objectContaining({
        items: [expect.objectContaining({ id: 'platform-parent', children: [] })],
      }),
    ])
  })

  it('permission-only filtering preserves an already enabled runtime feature', () => {
    const groups: NavigationGroup[] = [
      {
        id: 'system',
        label: '系统',
        icon: House,
        items: [
          {
            id: 'enabled',
            label: '已启用',
            routeName: 'systemdata-services',
            permission: 'systemdata.service-catalog.view',
            featureNId: 'f1',
          },
          {
            id: 'denied',
            label: '无权限',
            routeName: 'systemdata-features',
            permission: 'systemdata.feature.view',
            featureNId: 'f1',
          },
        ],
      },
    ]

    expect(applyPermissionPolicy(groups, ['systemdata.service-catalog.view'])).toEqual([
      expect.objectContaining({
        items: [expect.objectContaining({ id: 'enabled', featureNId: 'f1' })],
      }),
    ])
  })
})

describe('SystemData theme adapter', () => {
  it('maps only the PF-01 allowed theme values into tenant defaults', () => {
    const policy: ThemePolicyDto = {
      policyRevision: 4,
      degraded: false,
      allowedPalettes: ['technology-blue'],
      allowedModes: ['dark'],
      allowedPcDensities: ['compact'],
      defaultPalette: 'technology-blue',
      defaultMode: 'dark',
      defaultPcDensity: 'compact',
    }

    expect(themePolicyToTenantDefaults(policy)).toEqual({
      palette: 'technology-blue',
      mode: 'dark',
      density: 'compact',
    })
  })
})
