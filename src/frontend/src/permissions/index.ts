/** 权限目录与交互控制统一出口(§13.2)。 */

export { GENERATED_PERMISSION_NIDS, PERMISSIONS, type PermissionNId } from './catalog'
export { usePermission, type PermissionChecks } from './usePermission'
export { default as PermissionGate } from './PermissionGate.vue'
