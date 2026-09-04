<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableColumn } from '@/components/management/AppDataTable'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { usePermission } from '@/permissions/usePermission'
import { getManagementApi } from '@/api/identity/managementRegistry'
import type { PermissionTreeNodeDto } from '@/api/identity/management'
import type {
  CreateNavigationNodeRequest,
  NavigationDefaultImportItemDto,
  NavigationNodeDto,
} from '@/api/systemData/managementTypes'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { localeMessages } from '@/localization/i18n'
import { systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import { isRegisteredRouteName } from '@/router/routeNames'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'

interface NavigationConfigRow {
  rowKey: string
  nodeNId: string
  label: string
  kind: string
  target: string
  permission: string
  feature: string
  icon: string
  displayOrder: number | string
  status: string
  isAction: boolean
  children: NavigationConfigRow[]
  depth: number
  node?: NavigationNodeDto
}

type NavigationRowAction = 'edit' | 'add-child' | 'associate-permission' | 'status'

interface RuntimePreviewRow {
  nodeNId: string
  label: string
  kind: string
  depth: number
}

const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataNavigationView,
  },
)

const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const { has } = usePermission()
const copy = computed(() => systemDataPageCopy(localization.locale, 'navigation'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)

const terminal = ref('Pc')
const selected = ref<string | null>(null)
const editorOpen = ref(false)
const editorMode = ref<'node' | 'permissions'>('node')
const permissionTree = ref<PermissionTreeNodeDto[]>([])
const actionResourceNIds = ref<string[]>([])
const pendingNodeNId = ref('')
const draftKindLocked = ref(false)
const draftParentLocked = ref(false)
const defaultImportPreviewOpen = ref(false)
const defaultImportPreviewRevision = ref<number | null>(null)
const defaultImportConflict = ref('')
const runtimePreviewOpen = ref(false)

const draft = reactive({
  nodeNId: '',
  label: '',
  kind: 'Group',
  parentNodeNId: '',
  resourceNId: '',
  featureNId: '',
  iconKey: '',
  displayOrder: 0,
  visibleTerminals: ['Pc', 'Pda', 'Mobile'],
})

const nodes = computed(() => {
  const flatten = (items: readonly NavigationNodeDto[]): NavigationNodeDto[] =>
    items.flatMap((item) => [item, ...flatten(item.children ?? [])])
  return flatten(store.navigationDraft?.nodes ?? [])
})

const permissionNames = computed(() => {
  const names = new Map<string, string>()
  const visit = (items: readonly PermissionTreeNodeDto[]): void => {
    items.forEach((item) => {
      names.set(item.permissionNId, item.name)
      visit(item.children ?? [])
    })
  }
  visit(permissionTree.value)
  return names
})

const pageResources = computed(() =>
  store.resources.filter((resource) => resource.status === 'Active' && resource.type === 'Page'),
)
const actionResources = computed(() =>
  store.resources.filter(
    (resource) =>
      resource.status === 'Active' &&
      resource.type === 'Action' &&
      resource.requiredPermissionNId !== null,
  ),
)

function permissionLabel(permissionNId: string | null | undefined): string {
  if (!permissionNId) return copy.value.unassociated
  const name = permissionNames.value.get(permissionNId)
  return name ? `${name} (${permissionNId})` : permissionNId
}

function nodeKindLabel(node: NavigationNodeDto, depth: number): string {
  if (node.kind.toLowerCase() === 'group')
    return depth === 0 ? copy.value.firstLevel : copy.value.directoryGroup
  return copy.value.pageMenu
}

function buildRows(items: readonly NavigationNodeDto[], depth = 0): NavigationConfigRow[] {
  return items.flatMap((node) => {
    const resource = store.resources.find((item) => item.resourceNId === node.resourceNId)
    const actionRows: NavigationConfigRow[] =
      node.kind.toLowerCase() === 'link'
        ? (node.actionResourceNIds ?? []).map((resourceNId) => {
            const action = store.resources.find((item) => item.resourceNId === resourceNId)
            return {
              rowKey: `${node.nodeNId}::permission::${resourceNId}`,
              nodeNId: resourceNId,
              label: action?.name ?? resourceNId,
              kind: copy.value.actionPermission,
              target: resourceNId,
              permission: permissionLabel(action?.requiredPermissionNId),
              feature: '—',
              icon: '—',
              displayOrder: '—',
              status: action?.status ?? 'Missing',
              isAction: true,
              children: [],
              depth: depth + 1,
            }
          })
        : []
    const children = buildRows(node.children ?? [], depth + 1)
    return [
      {
        rowKey: node.nodeNId,
        nodeNId: node.nodeNId,
        label: node.label,
        kind: nodeKindLabel(node, depth),
        target: node.resourceNId
          ? `${resource?.name ?? node.resourceNId} (${node.resourceNId})`
          : '—',
        permission: permissionLabel(resource?.requiredPermissionNId),
        feature: node.featureNId ?? '—',
        icon: node.iconKey ?? '—',
        displayOrder: node.displayOrder,
        status: node.status,
        isAction: false,
        children: [...children, ...actionRows],
        depth,
        node,
      },
    ]
  })
}

const navigationRows = computed(() => buildRows(store.navigationDraft?.nodes ?? []))

const navigationColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'label', title: copy.value.label, minWidth: 180, sortable: true },
  { field: 'kind', title: copy.value.kind, width: 120, filter: false },
  { field: 'target', title: copy.value.target, minWidth: 220, filter: false },
  { field: 'permission', title: copy.value.permission, minWidth: 210, filter: false },
  { field: 'feature', title: copy.value.feature, minWidth: 150, filter: false },
  { field: 'displayOrder', title: copy.value.order, width: 90, filter: false },
  { field: 'status', title: copy.value.status, width: 100, filter: false },
])

const parentOptions = computed(() =>
  nodes.value.filter(
    (node) =>
      node.nodeNId !== selected.value &&
      node.kind.toLowerCase() === 'group' &&
      node.status === 'Active',
  ),
)

const selectedNode = computed(() =>
  selected.value === null
    ? null
    : (nodes.value.find((node) => node.nodeNId === selected.value) ?? null),
)

const previewNodes = computed<NavigationNodeDto[]>(() => {
  const build = (items: readonly NavigationNodeDto[]): NavigationNodeDto[] =>
    items.flatMap((node) => {
      if (node.status !== 'Active' || !node.visibleTerminals.includes(terminal.value)) return []
      const children = build(node.children ?? [])
      if (node.kind.toLowerCase() === 'group') return children.length ? [{ ...node, children }] : []
      const resource = store.resources.find((item) => item.resourceNId === node.resourceNId)
      if (
        resource === undefined ||
        resource.status !== 'Active' ||
        !resource.supportedTerminals.includes(terminal.value) ||
        (resource.requiredPermissionNId !== null && !has(resource.requiredPermissionNId)) ||
        (node.featureNId !== null &&
          store.features.find((item) => item.featureNId === node.featureNId)?.effectiveEnabled !==
            true)
      )
        return []
      return [{ ...node, children: [] }]
    })
  return build(store.navigationDraft?.nodes ?? [])
})

const previewRows = computed<RuntimePreviewRow[]>(() => {
  const flatten = (items: readonly NavigationNodeDto[], depth = 0): RuntimePreviewRow[] =>
    items.flatMap((item) => [
      { nodeNId: item.nodeNId, label: item.label, kind: nodeKindLabel(item, depth), depth },
      ...flatten(item.children ?? [], depth + 1),
    ])
  return flatten(previewNodes.value)
})

const previewStateMessage = computed(() => {
  if (previewRows.value.length > 0) return ''
  return nodes.value.length > 0 ? copy.value.noVisiblePreview : copy.value.noDraft
})

const navigationRootCount = computed(() => store.navigationDraft?.nodes.length ?? 0)
const navigationNodeCount = computed(() => nodes.value.length)
const draftTreeTitle = computed(
  () =>
    `${copy.value.draftTree} · ${navigationRootCount.value} ${copy.value.rootCount} · ${navigationNodeCount.value} ${copy.value.nodeCount}`,
)

const defaultImportItems = computed<NavigationDefaultImportItemDto[]>(
  () => store.navigationDefaultPreview?.items ?? [],
)
const defaultImportCounts = computed(() => {
  const counts = { add: 0, skipped: 0, blocked: 0 }
  defaultImportItems.value.forEach((item) => {
    const action = String(item.action ?? '').toLowerCase()
    if (action === 'add' || action === 'added') counts.add++
    else if (action === 'skipped') counts.skipped++
    else if (action === 'blocked') counts.blocked++
  })
  return counts
})
const defaultImportRevisionMatches = computed(
  () =>
    defaultImportPreviewRevision.value !== null &&
    defaultImportPreviewRevision.value === store.navigationDraft?.draftRevision,
)
const defaultImportCanConfirm = computed(
  () =>
    defaultImportItems.value.length > 0 &&
    defaultImportCounts.value.add > 0 &&
    defaultImportCounts.value.blocked === 0,
)
const validationExpired = computed(
  () =>
    store.navigationValidation !== null &&
    store.navigationValidation.draftRevision !== (store.navigationDraft?.draftRevision ?? -1),
)

const defaultPreviewLabels = computed(() => {
  const labels = new Map<string, string>()
  defaultImportItems.value.forEach((item) => labels.set(item.nodeNId, item.label || item.nodeNId))
  return labels
})

function defaultImportAction(item: NavigationDefaultImportItemDto): 'Add' | 'Skipped' | 'Blocked' {
  const action = String(item.action ?? '').toLowerCase()
  if (action === 'skipped') return 'Skipped'
  if (action === 'blocked') return 'Blocked'
  return 'Add'
}

function defaultImportActionLabel(item: NavigationDefaultImportItemDto): string {
  const action = defaultImportAction(item)
  return action === 'Add'
    ? copy.value.addAction
    : action === 'Skipped'
      ? copy.value.skipped
      : copy.value.blocked
}

function defaultImportPath(item: NavigationDefaultImportItemDto): string {
  const path: string[] = [item.label || item.nodeNId]
  const seen = new Set<string>([item.nodeNId])
  let parent: string | null = item.parentNodeNId ?? null
  while (parent && !seen.has(parent)) {
    seen.add(parent)
    path.unshift(defaultPreviewLabels.value.get(parent) ?? parent)
    parent =
      defaultImportItems.value.find((candidate) => candidate.nodeNId === parent)?.parentNodeNId ??
      null
  }
  return path.join(' / ')
}

function defaultImportKind(item: NavigationDefaultImportItemDto): string {
  return item.kind ? systemDataEnumLabel(localization.locale, item.kind) : copy.value.unknownType
}

function defaultImportReason(item: NavigationDefaultImportItemDto): string {
  const action = defaultImportAction(item)
  if (action === 'Add') return copy.value.addReason
  if (action === 'Skipped') return copy.value.skippedReason
  const detail = String(item.reason ?? '')
    .replace(/^缺少受信任资源\s*:/, '')
    .replace(/^Missing trusted resource\s*:\s*/i, '')
    .replace(/[。.]$/, '')
  return detail ? `${copy.value.blockedReason}: ${detail}` : copy.value.blockedReason
}

function validationNodePath(nodeNId: string | null): string {
  if (!nodeNId) return copy.value.unassociated
  const byId = new Map(nodes.value.map((node) => [node.nodeNId, node]))
  const path: string[] = []
  const seen = new Set<string>()
  let current = byId.get(nodeNId)
  while (current && !seen.has(current.nodeNId)) {
    seen.add(current.nodeNId)
    path.unshift(current.label)
    current = current.parentNodeNId ? byId.get(current.parentNodeNId) : undefined
  }
  return path.length > 0 ? path.join(' / ') : nodeNId
}

function validationMessage(code: string): string {
  const messages: Record<string, string> = {
    PERMISSION_UNVERIFIED: copy.value.validationPermissionUnverified,
    RESOURCE_NOT_FOUND: copy.value.validationResourceNotFound,
    RESOURCE_NOT_ACTIVE: copy.value.validationResourceNotActive,
    RESOURCE_RETIRED: copy.value.validationResourceRetired,
    TERMINAL_NOT_SUPPORTED: copy.value.validationTerminalNotSupported,
    FEATURE_INVALID: copy.value.validationFeatureInvalid,
    ACTION_RESOURCE_NOT_FOUND: copy.value.validationActionResourceNotFound,
    ACTION_RESOURCE_INVALID: copy.value.validationActionResourceInvalid,
    ACTION_RESOURCE_RETIRED: copy.value.validationActionResourceRetired,
    ORPHAN_NODE: copy.value.validationParentNotFound,
    INVALID_PARENT_KIND: copy.value.validationParentKind,
    MAX_DEPTH: copy.value.validationMaxDepth,
    EMPTY_GROUP: copy.value.validationEmptyGroup,
    NODE_CYCLE: copy.value.validationNodeCycle,
    DUPLICATE_NODE: copy.value.validationDuplicateNode,
    INVALID_HIERARCHY: copy.value.validationHierarchy,
  }
  return messages[code] ?? `${copy.value.validationUnknown} (${code})`
}

function validationReceiptCause(error: {
  resourceNId?: string | null
  moduleNId?: string | null
  manifestVersion?: string | null
  manifestChecksum?: string | null
  trustedReceiptVersion?: string | null
  trustedReceiptChecksum?: string | null
  trustedReceiptVerified?: boolean | null
  receiptDetails?: Array<{
    resourceNId: string
    moduleNId: string
    manifestVersion?: string | null
    manifestChecksum?: string | null
    trustedReceiptVersion?: string | null
    trustedReceiptChecksum?: string | null
    trustedReceiptVerified: boolean
  }>
}): string {
  const details = error.receiptDetails?.length
    ? error.receiptDetails
    : [
        {
          resourceNId: error.resourceNId ?? '—',
          moduleNId: error.moduleNId ?? '—',
          manifestVersion: error.manifestVersion,
          manifestChecksum: error.manifestChecksum,
          trustedReceiptVersion: error.trustedReceiptVersion,
          trustedReceiptChecksum: error.trustedReceiptChecksum,
          trustedReceiptVerified: error.trustedReceiptVerified === true,
        },
      ]
  return details
    .map((detail) => {
      const manifest = `${detail.moduleNId} / ${detail.manifestVersion ?? '—'} / ${detail.manifestChecksum ?? '—'}`
      const receipt = `${detail.trustedReceiptVersion ?? '—'} / ${detail.trustedReceiptChecksum ?? '—'}`
      const state = detail.trustedReceiptVerified
        ? copy.value.receiptVerified
        : copy.value.receiptUnavailable
      return `${copy.value.validationResource}: ${detail.resourceNId} · ${copy.value.validationManifest}: ${manifest} · ${copy.value.validationReceipt}: ${receipt} · ${state}`
    })
    .join('；')
}

interface NavigationValidationItem {
  code: string
  message: string
  paths: string[]
  cause: string
}

const validationItems = computed(() => {
  const groups = new Map<string, NavigationValidationItem>()
  const causeKeys = new Map<string, Set<string>>()
  ;(store.navigationValidation?.errors ?? []).forEach((error) => {
    const key = error.code
    const item = groups.get(key) ?? {
      code: error.code,
      message: validationMessage(error.code),
      paths: [],
      cause: '',
    }
    if (
      error.code === 'PERMISSION_UNVERIFIED' &&
      (error.receiptDetails?.length || error.resourceNId || error.moduleNId)
    ) {
      const cause = `${validationNodePath(error.nodeNId)}: ${validationReceiptCause(error)}`
      const seenCauses = causeKeys.get(key) ?? new Set<string>()
      if (!seenCauses.has(cause)) {
        seenCauses.add(cause)
        item.cause = item.cause ? `${item.cause}；${cause}` : cause
      }
      causeKeys.set(key, seenCauses)
    }
    const path = validationNodePath(error.nodeNId)
    if (!item.paths.includes(path)) item.paths.push(path)
    groups.set(key, item)
  })
  return [...groups.values()]
})

function resetDraft(parentNodeNId = '', kind: 'Group' | 'Link' = 'Link'): void {
  Object.assign(draft, {
    nodeNId: pendingNodeNId.value,
    label: '',
    kind,
    parentNodeNId,
    resourceNId: '',
    featureNId: '',
    iconKey: '',
    displayOrder: 0,
    visibleTerminals: ['Pc', 'Pda', 'Mobile'],
  })
}

function newNode(
  options: {
    parentNodeNId?: string
    kind?: 'Group' | 'Link'
    lockKind?: boolean
    lockParent?: boolean
  } = {},
): void {
  selected.value = null
  editorMode.value = 'node'
  const kind = options.kind ?? 'Link'
  const suffix =
    globalThis.crypto?.randomUUID?.() ??
    `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
  pendingNodeNId.value = `navigation.${kind.toLowerCase()}.${suffix}`
  draftKindLocked.value = options.lockKind === true
  draftParentLocked.value = options.lockParent === true
  resetDraft(options.parentNodeNId ?? '', kind)
  editorOpen.value = true
}

function newFirstLevel(): void {
  newNode({ kind: 'Group', lockKind: true, lockParent: true })
}

function newChild(row: NavigationConfigRow): void {
  if (!row.node || row.node.kind.toLowerCase() !== 'group') return
  const kind = row.depth === 0 ? 'Group' : 'Link'
  newNode({
    parentNodeNId: row.node.nodeNId,
    kind,
    lockKind: true,
    lockParent: true,
  })
}

function edit(node: NavigationNodeDto): void {
  selected.value = node.nodeNId
  pendingNodeNId.value = ''
  editorMode.value = 'node'
  draftKindLocked.value = false
  draftParentLocked.value = false
  Object.assign(draft, {
    nodeNId: node.nodeNId,
    label: node.label,
    kind: node.kind,
    parentNodeNId: node.parentNodeNId ?? '',
    resourceNId: node.resourceNId ?? '',
    featureNId: node.featureNId ?? '',
    iconKey: node.iconKey ?? '',
    displayOrder: node.displayOrder,
    visibleTerminals: [...node.visibleTerminals],
  })
  editorOpen.value = true
}

function openPermissionAssociation(node: NavigationNodeDto): void {
  selected.value = node.nodeNId
  editorMode.value = 'permissions'
  actionResourceNIds.value = [...(node.actionResourceNIds ?? [])]
  editorOpen.value = true
}

const navigationActionWidths: Record<NavigationRowAction, number> = {
  edit: 44,
  'add-child': 72,
  'associate-permission': 86,
  status: 70,
}
const navigationActionGap = 4
const navigationMoreWidth = 52

function rowActionCandidates(row: NavigationConfigRow): NavigationRowAction[] {
  if (!row.node) return []
  if (row.node.kind.toLowerCase() === 'group') {
    return row.depth < 2 ? ['edit', 'add-child', 'status'] : ['edit', 'status']
  }
  return ['edit', 'associate-permission', 'status']
}

function directRowActions(row: NavigationConfigRow, availableWidth = 220): NavigationRowAction[] {
  const actions = rowActionCandidates(row)
  const width = Number.isFinite(availableWidth) ? Math.max(0, Math.round(availableWidth)) : 0
  const totalWidth = actions.reduce(
    (total, action, index) =>
      total + (index === 0 ? 0 : navigationActionGap) + navigationActionWidth(row, action),
    0,
  )
  if (totalWidth <= width) return actions
  const directWidth = width - navigationMoreButtonWidth()
  if (directWidth <= 0) return []
  const direct: NavigationRowAction[] = []
  let used = 0
  for (const action of actions) {
    const gap = direct.length === 0 ? 0 : navigationActionGap
    const next = used + gap + navigationActionWidth(row, action)
    if (next > directWidth) break
    direct.push(action)
    used = next
  }
  return direct
}

function overflowRowActions(row: NavigationConfigRow, availableWidth = 220): NavigationRowAction[] {
  const direct = directRowActions(row, availableWidth)
  return rowActionCandidates(row).filter((action) => !direct.includes(action))
}

function rowActionLabel(action: NavigationRowAction): string {
  if (action === 'edit') return copy.value.edit
  if (action === 'add-child') return copy.value.addChild
  if (action === 'associate-permission') return copy.value.associatePermission
  return copy.value.disable
}

function rowActionText(row: NavigationConfigRow, action: NavigationRowAction): string {
  if (action === 'status' && row.node?.status !== 'Active') return copy.value.restore
  return rowActionLabel(action)
}

function navigationTextWidth(value: string): number {
  return [...value].reduce(
    (width, character) => width + (/^[\u0000-\u007f]$/.test(character) ? 7 : 14),
    0,
  )
}

function navigationActionWidth(row: NavigationConfigRow, action: NavigationRowAction): number {
  return Math.max(
    navigationActionWidths[action],
    navigationTextWidth(rowActionText(row, action)) + 24,
  )
}

function navigationMoreButtonWidth(): number {
  return Math.max(navigationMoreWidth, navigationTextWidth(copy.value.more) + 24)
}

function handleRowAction(row: NavigationConfigRow, action: NavigationRowAction): void {
  if (!row.node) return
  if (action === 'edit') edit(row.node)
  else if (action === 'add-child') newChild(row)
  else if (action === 'associate-permission') openPermissionAssociation(row.node)
  else void disableNode(row.node)
}

function navigationRequest(
  actionIds: string[],
): Omit<CreateNavigationNodeRequest, 'nodeNId' | 'kind' | 'navigationSetNId'> {
  return {
    label: draft.label.trim(),
    parentNodeNId: draft.parentNodeNId || null,
    resourceNId: draft.kind === 'Group' ? null : draft.resourceNId || null,
    featureNId: draft.kind === 'Group' ? null : draft.featureNId || null,
    iconKey: draft.iconKey || null,
    displayOrder: draft.displayOrder,
    visibleTerminals: draft.kind === 'Group' ? ['Pc', 'Pda', 'Mobile'] : draft.visibleTerminals,
    actionResourceNIds: draft.kind === 'Group' ? [] : actionIds,
    expectedDraftRevision: store.navigationDraft?.draftRevision ?? 0,
  }
}

async function save(): Promise<void> {
  if (editorMode.value === 'permissions') {
    const node = selectedNode.value
    if (node === null) return
    await store.updateNavigationNode(node.nodeNId, {
      label: node.label,
      parentNodeNId: node.parentNodeNId,
      resourceNId: node.resourceNId,
      featureNId: node.featureNId,
      iconKey: node.iconKey,
      displayOrder: node.displayOrder,
      visibleTerminals: node.visibleTerminals,
      actionResourceNIds: actionResourceNIds.value,
      expectedDraftRevision: store.navigationDraft?.draftRevision ?? 0,
    })
    if (!store.error) editorOpen.value = false
    return
  }
  const request = navigationRequest(
    selectedNode.value?.kind.toLowerCase() === 'link'
      ? [...(selectedNode.value.actionResourceNIds ?? [])]
      : [],
  )
  if (!request.label || (draft.kind === 'Link' && !request.resourceNId)) return
  if (selected.value) {
    await store.updateNavigationNode(selected.value, request)
    if (!store.error) editorOpen.value = false
  } else {
    const saved = await store.addNavigationNode({
      nodeNId: draft.nodeNId,
      kind: draft.kind,
      navigationSetNId: 'PLATFORM_NAVIGATION',
      ...request,
    })
    if (!saved) return
    pendingNodeNId.value = ''
    editorOpen.value = false
    resetDraft()
  }
}

async function confirmMutation(title: string, body: string): Promise<boolean> {
  try {
    await ElMessageBox.confirm(body, title, {
      type: 'warning',
      confirmButtonText: commonCopy.value.confirm,
      cancelButtonText: commonCopy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}

async function publish(): Promise<void> {
  if (await confirmMutation(copy.value.confirmPublishTitle, copy.value.confirmPublishBody))
    await store.publishNavigation()
}

async function rollback(): Promise<void> {
  if (await confirmMutation(copy.value.confirmRollbackTitle, copy.value.confirmRollbackBody))
    await store.rollbackNavigation()
}

async function previewDefaultImport(): Promise<void> {
  defaultImportConflict.value = ''
  await store.previewNavigationDefaults()
  const preview = store.navigationDefaultPreview
  if (!preview || store.error) return
  defaultImportPreviewRevision.value = preview.draftRevision
  defaultImportPreviewOpen.value = true
}

async function confirmDefaultImport(): Promise<void> {
  if (!defaultImportRevisionMatches.value) {
    defaultImportConflict.value = copy.value.previewConflict
    return
  }
  if (!defaultImportCanConfirm.value) {
    return
  }
  await store.importNavigationDefaults()
  if (store.error) {
    defaultImportConflict.value = /(?:409|CONFLICT|CONCURRENCY)/i.test(store.error)
      ? copy.value.previewConflict
      : store.error
    return
  }
  defaultImportPreviewRevision.value = store.navigationDefaultPreview?.draftRevision ?? null
  defaultImportPreviewOpen.value = false
}

function openRuntimePreview(): void {
  runtimePreviewOpen.value = true
}

async function disableNode(node: NavigationNodeDto): Promise<void> {
  if (node.status !== 'Active') {
    await store.restoreNavigationNode(node.nodeNId)
    return
  }
  if (await confirmMutation(copy.value.confirmDisableTitle, copy.value.confirmDisableBody))
    await store.deleteNavigationNode(node.nodeNId)
}

onMounted(async () => {
  try {
    permissionTree.value = await getManagementApi().getPermissionTree()
  } catch {
    permissionTree.value = []
  }
})

resetDraft()
</script>

<template>
  <SystemDataAdminFrame
    kind="navigation"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
  >
    <template #toolbar>
      <el-button data-testid="systemdata-navigation-preview" @click="openRuntimePreview">
        {{ copy.preview }}
      </el-button>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage">
        <el-button data-testid="systemdata-navigation-new-first-level" @click="newFirstLevel">
          {{ copy.newFirstLevel || copy.add }}
        </el-button>
        <el-button
          data-testid="systemdata-navigation-defaults"
          :disabled="store.loading"
          @click="previewDefaultImport"
        >
          {{ copy.importDefaults }}
        </el-button>
        <el-button data-testid="systemdata-navigation-validate" @click="store.validateNavigation">
          {{ copy.validate }}
        </el-button>
      </PermissionGate>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationPublish">
        <el-button
          type="primary"
          data-testid="systemdata-navigation-publish"
          :disabled="store.loading"
          @click="publish"
        >
          {{ copy.publish }}
        </el-button>
      </PermissionGate>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationRollback">
        <el-button type="danger" data-testid="systemdata-navigation-rollback" @click="rollback">
          {{ copy.rollback }}
        </el-button>
      </PermissionGate>
    </template>

    <AppDataTable
      table-key="systemdata-navigation"
      mode="tree"
      row-key="rowKey"
      :tree="{ childrenField: 'children' }"
      :rows="navigationRows"
      :total="navigationRows.length"
      :loading="store.loading"
      :columns="navigationColumns"
      :toolbar-title="draftTreeTitle"
      :toolbar-labels="true"
    >
      <template #cell-label="{ row }">
        <span :class="{ 'systemdata-navigation-action-row': row.isAction }">{{ row.label }}</span>
      </template>
      <template #cell-status="{ row }">
        <el-tag :type="row.status === 'Active' ? 'success' : 'warning'" effect="light">
          {{ systemDataEnumLabel(localization.locale, row.status) }}
        </el-tag>
      </template>
      <template #actions="{ row, availableWidth }">
        <div v-if="!row.isAction && row.node" class="systemdata-navigation-row-actions">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage">
            <template v-for="action in directRowActions(row, availableWidth)" :key="action">
              <el-button
                link
                :type="action === 'status' && row.node.status === 'Active' ? 'danger' : 'primary'"
                @click="handleRowAction(row, action)"
              >
                {{ rowActionText(row, action) }}
              </el-button>
            </template>
            <el-dropdown v-if="overflowRowActions(row, availableWidth).length" trigger="click">
              <el-button link type="primary" class="systemdata-navigation-more">
                {{ copy.more }}
              </el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item
                    v-for="action in overflowRowActions(row, availableWidth)"
                    :key="action"
                    @click="handleRowAction(row, action)"
                  >
                    {{ rowActionText(row, action) }}
                  </el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </PermissionGate>
        </div>
      </template>
    </AppDataTable>

    <p
      v-if="store.navigationPublishedRevision !== null"
      data-testid="systemdata-navigation-published-revision"
      class="systemdata-navigation-hint"
    >
      {{ copy.publishedRevision }} {{ store.navigationPublishedRevision }}
    </p>

    <details
      v-if="store.navigationValidation"
      class="systemdata-validation"
      :class="
        validationExpired
          ? 'is-failure'
          : store.navigationValidation.isValid
            ? 'is-success'
            : 'is-failure'
      "
      open
      role="status"
    >
      <summary>
        <strong>{{
          validationExpired
            ? copy.validationExpired
            : store.navigationValidation.isValid
              ? commonCopy.success
              : commonCopy.validationFailed
        }}</strong>
        <span>{{ validationItems.length }} {{ copy.validationItems }}</span>
      </summary>
      <p v-if="validationExpired">{{ copy.validationExpired }}</p>
      <ul v-if="validationItems.length">
        <li v-for="item in validationItems" :key="item.code">
          <strong>{{ item.message }}</strong>
          <span v-if="item.paths.length"> · {{ item.paths.join('、') }}</span>
          <p v-if="item.cause">{{ item.cause }}</p>
        </li>
      </ul>
    </details>

    <AppFormDrawer
      v-model="defaultImportPreviewOpen"
      :busy="store.loading"
      :title="copy.importDefaultsTitle"
      size="wide"
      @submit="confirmDefaultImport"
    >
      <div data-testid="systemdata-navigation-defaults-preview" class="systemdata-default-preview">
        <p class="systemdata-navigation-hint">{{ copy.importPreviewHint }}</p>
        <div class="systemdata-default-preview__summary" aria-label="default-import-summary">
          <el-tag type="success">{{ copy.addAction }} {{ defaultImportCounts.add }}</el-tag>
          <el-tag type="info">{{ copy.skipped }} {{ defaultImportCounts.skipped }}</el-tag>
          <el-tag type="danger">{{ copy.blocked }} {{ defaultImportCounts.blocked }}</el-tag>
        </div>
        <el-alert v-if="defaultImportConflict" type="error" :closable="false">
          {{ defaultImportConflict }}
        </el-alert>
        <ul v-if="defaultImportItems.length" class="systemdata-default-preview__list">
          <li
            v-for="item in defaultImportItems"
            :key="item.nodeNId"
            :class="`is-${defaultImportAction(item).toLowerCase()}`"
          >
            <div class="systemdata-default-preview__row">
              <strong>{{ item.label || item.nodeNId }}</strong>
              <el-tag
                size="small"
                :type="
                  defaultImportAction(item) === 'Add'
                    ? 'success'
                    : defaultImportAction(item) === 'Skipped'
                      ? 'info'
                      : 'danger'
                "
              >
                {{ defaultImportActionLabel(item) }}
              </el-tag>
            </div>
            <span class="systemdata-default-preview__meta">
              {{ defaultImportPath(item) }} · {{ defaultImportKind(item) }} · {{ copy.level }}
              {{ item.level || 1 }}
            </span>
            <p>{{ defaultImportReason(item) || copy.noReason }}</p>
          </li>
        </ul>
        <p v-else class="systemdata-navigation-hint">{{ copy.noDefaultPreview }}</p>
        <p v-if="defaultImportCounts.blocked" class="systemdata-default-preview__blocked-hint">
          {{ copy.blockedImportHint }}
        </p>
      </div>
      <template #footer>
        <el-button
          data-testid="systemdata-navigation-defaults-cancel"
          @click="defaultImportPreviewOpen = false"
        >
          {{ commonCopy.cancel }}
        </el-button>
        <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage">
          <el-button
            type="primary"
            data-testid="systemdata-navigation-defaults-confirm"
            :disabled="!defaultImportCanConfirm"
            @click="confirmDefaultImport"
          >
            {{ copy.importDefaultsConfirm }}
          </el-button>
        </PermissionGate>
      </template>
    </AppFormDrawer>

    <AppFormDrawer v-model="runtimePreviewOpen" :title="copy.runtimePreview" size="wide">
      <div data-testid="systemdata-navigation-runtime-preview" class="systemdata-runtime-preview">
        <div class="systemdata-runtime-preview__heading">
          <div>
            <strong>{{ copy.draftPreviewStatus }}</strong>
            <span
              >{{ copy.previewRevision }} {{ store.navigationDraft?.draftRevision ?? '—' }}</span
            >
          </div>
          <el-select v-model="terminal" size="small" :aria-label="copy.terminals">
            <el-option :label="systemDataEnumLabel(localization.locale, 'Pc')" value="Pc" />
            <el-option :label="systemDataEnumLabel(localization.locale, 'Pda')" value="Pda" />
            <el-option :label="systemDataEnumLabel(localization.locale, 'Mobile')" value="Mobile" />
          </el-select>
        </div>
        <div v-if="previewRows.length" class="systemdata-runtime-preview__tree">
          <div
            v-for="row in previewRows"
            :key="row.nodeNId"
            class="systemdata-runtime-preview__item"
            :style="{ paddingLeft: `${row.depth * 24 + 8}px` }"
          >
            <span>{{ row.label }}</span>
            <el-tag size="small" effect="plain">{{ row.kind }}</el-tag>
          </div>
        </div>
        <el-empty v-else :description="previewStateMessage" />
        <p class="systemdata-navigation-hint">{{ copy.previewFilterHint }}</p>
      </div>
      <template #footer>
        <el-button
          data-testid="systemdata-navigation-preview-close"
          @click="runtimePreviewOpen = false"
        >
          {{ commonCopy.cancel }}
        </el-button>
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      v-model="editorOpen"
      :busy="store.loading"
      :title="
        editorMode === 'permissions' ? copy.permissionAssociation : selected ? copy.edit : copy.add
      "
      size="medium"
      @submit="save"
    >
      <template v-if="editorMode === 'permissions'">
        <p class="systemdata-navigation-hint">{{ copy.actionResourceHint }}</p>
        <el-form label-width="110px">
          <el-form-item :label="copy.actionPermission">
            <el-checkbox-group v-model="actionResourceNIds">
              <el-checkbox
                v-for="resource in actionResources"
                :key="resource.resourceNId"
                :label="resource.resourceNId"
              >
                {{ resource.name }} ({{ resource.resourceNId }}) ·
                {{ permissionLabel(resource.requiredPermissionNId) }}
              </el-checkbox>
            </el-checkbox-group>
          </el-form-item>
        </el-form>
      </template>
      <template v-else>
        <p v-if="draft.kind === 'Link'" class="systemdata-navigation-hint">
          {{ copy.pageResourceHint }}
        </p>
        <el-form label-width="100px">
          <el-form-item :label="copy.nid"
            ><el-input v-model="draft.nodeNId" disabled
          /></el-form-item>
          <el-form-item :label="copy.label"><el-input v-model="draft.label" /></el-form-item>
          <el-form-item :label="copy.kind">
            <el-select v-model="draft.kind" :disabled="Boolean(selected) || draftKindLocked">
              <el-option :label="systemDataEnumLabel(localization.locale, 'Group')" value="Group" />
              <el-option :label="systemDataEnumLabel(localization.locale, 'Link')" value="Link" />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.parent">
            <el-select v-model="draft.parentNodeNId" clearable :disabled="draftParentLocked">
              <el-option
                v-for="item in parentOptions"
                :key="item.nodeNId"
                :label="`${item.label} (${item.nodeNId})`"
                :value="item.nodeNId"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.resource">
            <el-select v-model="draft.resourceNId" clearable :disabled="draft.kind === 'Group'">
              <el-option
                v-for="resource in pageResources"
                :key="resource.resourceNId"
                :label="
                  `${resource.name} (${resource.resourceNId})${
                    isRegisteredRouteName(resource.routeName) ? '' : ` · ${copy.routeUnavailable}`
                  }`
                "
                :disabled="!isRegisteredRouteName(resource.routeName)"
                :value="resource.resourceNId"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.feature">
            <el-select v-model="draft.featureNId" clearable :disabled="draft.kind === 'Group'">
              <el-option
                v-for="feature in store.features"
                :key="feature.featureNId"
                :label="`${feature.name} (${feature.featureNId})`"
                :value="feature.featureNId"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.icon"><el-input v-model="draft.iconKey" /></el-form-item>
          <el-form-item :label="copy.displayOrder">
            <el-input-number v-model="draft.displayOrder" :min="0" controls-position="right" />
          </el-form-item>
          <el-form-item :label="copy.terminals">
            <el-checkbox-group v-model="draft.visibleTerminals">
              <el-checkbox value="Pc" :aria-label="systemDataEnumLabel(localization.locale, 'Pc')">
                {{ systemDataEnumLabel(localization.locale, 'Pc') }}
              </el-checkbox>
              <el-checkbox
                value="Pda"
                :aria-label="systemDataEnumLabel(localization.locale, 'Pda')"
              >
                {{ systemDataEnumLabel(localization.locale, 'Pda') }}
              </el-checkbox>
              <el-checkbox
                value="Mobile"
                :aria-label="systemDataEnumLabel(localization.locale, 'Mobile')"
              >
                {{ systemDataEnumLabel(localization.locale, 'Mobile') }}
              </el-checkbox>
            </el-checkbox-group>
          </el-form-item>
        </el-form>
      </template>
      <template #footer>
        <el-button @click="editorOpen = false">{{ commonCopy.cancel }}</el-button>
        <PermissionGate :permission-n-id="PERMISSIONS.systemDataNavigationManage">
          <el-button type="primary" data-testid="systemdata-navigation-save" @click="save">
            {{ copy.save }}
          </el-button>
        </PermissionGate>
      </template>
    </AppFormDrawer>
  </SystemDataAdminFrame>
</template>

<style scoped>
.systemdata-navigation-action-row {
  padding: 4px 8px;
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-navigation-hint {
  margin: 0 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
}

.systemdata-navigation-row-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  max-width: 100%;
  white-space: nowrap;
}

.systemdata-navigation-more {
  flex: 0 0 auto;
}

.systemdata-default-preview,
.systemdata-runtime-preview {
  display: grid;
  gap: var(--ip-space-3);
}

.systemdata-default-preview__summary {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
}

.systemdata-default-preview__list {
  display: grid;
  gap: var(--ip-space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.systemdata-default-preview__list li {
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-default-preview__list li.is-blocked {
  border-color: var(--ip-color-danger);
}

.systemdata-default-preview__row,
.systemdata-runtime-preview__heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}

.systemdata-default-preview__meta,
.systemdata-runtime-preview__heading span {
  display: block;
  margin-top: 4px;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}

.systemdata-default-preview__list p,
.systemdata-default-preview__blocked-hint {
  margin: var(--ip-space-2) 0 0;
  color: var(--ip-color-text-secondary);
}

.systemdata-runtime-preview__tree {
  display: grid;
  gap: 2px;
  padding: var(--ip-space-2);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-runtime-preview__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 36px;
  gap: var(--ip-space-3);
  padding-right: var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border-radius: var(--ip-radius-sm);
}

.systemdata-validation {
  padding: var(--ip-space-3) var(--ip-space-4);
  color: var(--ip-color-text-primary);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-validation.is-success {
  background: var(--ip-color-success-bg);
  border-color: var(--ip-color-success);
}

.systemdata-validation.is-failure {
  background: var(--ip-color-danger-bg);
  border-color: var(--ip-color-danger);
}

.systemdata-validation summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  cursor: pointer;
}

.systemdata-validation summary span {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}

.systemdata-validation ul {
  margin: var(--ip-space-2) 0 0;
  padding-left: var(--ip-space-5);
}

.systemdata-validation li p {
  margin: 4px 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
</style>
