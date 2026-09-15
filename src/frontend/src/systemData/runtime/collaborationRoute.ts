import { ROUTE_NAMES } from '@/router/routeNames'

/** Routes whose page owns a collaboration connection in embedded mode. */
export const COLLABORATION_ROUTE_NAMES = new Set<string>([
  ROUTE_NAMES.collaborationChat,
  ROUTE_NAMES.collaborationConversation,
  ROUTE_NAMES.collaborationCompliance,
  ROUTE_NAMES.collaborationComplianceSearch,
  ROUTE_NAMES.collaborationLegalHolds,
  ROUTE_NAMES.collaborationExports,
  ROUTE_NAMES.collaborationRetention,
  ROUTE_NAMES.pdaCollaborationChat,
  ROUTE_NAMES.pdaCollaborationConversation,
  ROUTE_NAMES.mobileCollaborationChat,
  ROUTE_NAMES.mobileCollaborationConversation,
])

export function isCollaborationRoute(routeName: unknown): boolean {
  return typeof routeName === 'string' && COLLABORATION_ROUTE_NAMES.has(routeName)
}
