/**
 * Stable route-name catalog shared by the static route table and SystemData's
 * runtime navigation adapter. New pages add a name here and a normal static or
 * lazy route record in routes.ts; menu configuration never loads code itself.
 */
export const ROUTE_NAMES = {
  root: 'root',
  login: 'login',
  changePassword: 'change-password',
  ssoLogin: 'sso-login',
  ssoCallback: 'sso-callback',
  forbidden: 'forbidden',
  pcHome: 'pc-home',
  pcOperation: 'pc-operation',
  profile: 'profile',
  terminalPreview: 'terminal-preview',
  identityUsers: 'identity-users',
  identityUserGroups: 'identity-user-groups',
  identityRoles: 'identity-roles',
  identityPermissions: 'identity-permissions',
  identityAudits: 'identity-audits',
  ssoProviders: 'sso-providers',
  ssoClients: 'sso-clients',
  systemDataOrganizations: 'systemdata-organizations',
  systemDataAssignments: 'systemdata-assignments',
  systemDataNavigation: 'systemdata-navigation',
  systemDataFeatures: 'systemdata-features',
  systemDataServices: 'systemdata-services',
  systemDataThemes: 'systemdata-themes',
  systemDataServiceInitialization: 'systemdata-service-initialization',
  workspaceTabsSandbox: 'workspace-tabs-sandbox',
  uiBaseline: 'ui-baseline',
  pdaHome: 'pda-home',
  mobileHome: 'mobile-home',
  mobileMy: 'mobile-my',
  notFound: 'not-found',
} as const

const registeredRouteNames = new Set<string>([
  ...Object.values(ROUTE_NAMES).filter(
    (routeName) =>
      routeName !== ROUTE_NAMES.workspaceTabsSandbox && routeName !== ROUTE_NAMES.uiBaseline,
  ),
  ...(import.meta.env.DEV ? [ROUTE_NAMES.workspaceTabsSandbox, ROUTE_NAMES.uiBaseline] : []),
])

export function isRegisteredRouteName(routeName: string | null | undefined): boolean {
  return routeName !== null && routeName !== undefined && registeredRouteNames.has(routeName)
}
