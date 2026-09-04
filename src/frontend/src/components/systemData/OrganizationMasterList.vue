<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { OrganizationNodeDto } from '@/api/systemData/managementTypes'
import { systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'

const props = withDefaults(
  defineProps<{
    nodes: OrganizationNodeDto[]
    selectedNId: string | null
    loading?: boolean
  }>(),
  { loading: false },
)
const emit = defineEmits<{
  select: [nId: string | null]
  refresh: []
}>()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'organizations'))
const search = ref('')

interface FlatOrganization {
  node: OrganizationNodeDto
  depth: number
  ancestors: string[]
  ancestorNIds: string[]
}

const flatNodes = computed<FlatOrganization[]>(() => {
  const result: FlatOrganization[] = []
  const visit = (
    nodes: readonly OrganizationNodeDto[],
    ancestors: string[],
    ancestorNIds: string[],
    depth: number,
  ): void => {
    for (const node of nodes) {
      result.push({ node, depth, ancestors, ancestorNIds })
      visit(node.children, [...ancestors, node.name], [...ancestorNIds, node.nId], depth + 1)
    }
  }
  visit(props.nodes, [], [], 0)
  return result
})

const expandedNIds = ref<Set<string>>(new Set())
watch(
  () => props.nodes,
  () => {
    const known = new Set(
      flatNodes.value
        .filter((entry) => entry.node.children.length > 0)
        .map((entry) => entry.node.nId),
    )
    expandedNIds.value = new Set([...expandedNIds.value].filter((nId) => known.has(nId)))
    if (expandedNIds.value.size === 0) expandedNIds.value = known
  },
  { immediate: true },
)

function isExpanded(nId: string): boolean {
  return expandedNIds.value.has(nId)
}

function toggleExpanded(nId: string): void {
  const next = new Set(expandedNIds.value)
  if (next.has(nId)) next.delete(nId)
  else next.add(nId)
  expandedNIds.value = next
}

function expandAll(): void {
  expandedNIds.value = new Set(
    flatNodes.value
      .filter((entry) => entry.node.children.length > 0)
      .map((entry) => entry.node.nId),
  )
}

function collapseAll(): void {
  expandedNIds.value = new Set()
}

const visibleNodes = computed(() => {
  const keyword = search.value.trim().toLocaleLowerCase()
  if (keyword !== '') {
    const matchingNIds = new Set(
      flatNodes.value
        .filter(({ node }) =>
          [node.name, node.nId, node.type, node.status]
            .join(' ')
            .toLocaleLowerCase()
            .includes(keyword),
        )
        .map(({ node }) => node.nId),
    )
    const contextNIds = new Set<string>()
    for (const { node, ancestorNIds } of flatNodes.value) {
      if (matchingNIds.has(node.nId)) {
        contextNIds.add(node.nId)
        ancestorNIds.forEach((nId) => contextNIds.add(nId))
      }
    }
    return flatNodes.value.filter(({ node }) => contextNIds.has(node.nId))
  }
  return flatNodes.value.filter(({ ancestorNIds }) =>
    ancestorNIds.every((nId) => expandedNIds.value.has(nId)),
  )
})

const visibleNIds = computed(() => visibleNodes.value.map(({ node }) => node.nId))
watch(visibleNIds, (nIds) => {
  if (props.selectedNId !== null && !nIds.includes(props.selectedNId)) emit('select', null)
})

function select(nId: string): void {
  emit('select', props.selectedNId === nId ? null : nId)
}

function onKeydown(event: KeyboardEvent, nId: string): void {
  const index = visibleNIds.value.indexOf(nId)
  const entry = flatNodes.value.find((item) => item.node.nId === nId)
  const focus = (targetNId: string | undefined): void => {
    if (!targetNId) return
    document.querySelector<HTMLElement>(`[data-testid="organization-card-${targetNId}"]`)?.focus()
  }
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    select(nId)
  } else if (event.key === 'ArrowDown') {
    event.preventDefault()
    focus(visibleNIds.value[Math.min(index + 1, visibleNIds.value.length - 1)])
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    focus(visibleNIds.value[Math.max(index - 1, 0)])
  } else if (event.key === 'Home') {
    event.preventDefault()
    focus(visibleNIds.value[0])
  } else if (event.key === 'End') {
    event.preventDefault()
    focus(visibleNIds.value[visibleNIds.value.length - 1])
  } else if (event.key === 'ArrowRight' && entry?.node.children.length) {
    event.preventDefault()
    if (!isExpanded(nId)) toggleExpanded(nId)
    else focus(visibleNIds.value[index + 1])
  } else if (event.key === 'ArrowLeft') {
    event.preventDefault()
    if (entry?.node.children.length && isExpanded(nId)) toggleExpanded(nId)
    else focus(entry?.ancestorNIds.at(-1) ?? undefined)
  }
}
</script>

<template>
  <div class="organization-master-list" role="tree" :aria-busy="loading">
    <div class="organization-master-list__toolbar">
      <el-input v-model="search" clearable :placeholder="copy.search" :aria-label="copy.search" />
      <el-button :loading="loading" @click="emit('refresh')">{{ copy.refresh }}</el-button>
      <el-button @click="expandAll">{{ copy.expandAll }}</el-button>
      <el-button @click="collapseAll">{{ copy.collapseAll }}</el-button>
      <el-button
        v-if="selectedNId"
        link
        data-testid="organization-selection-clear"
        @click="emit('select', null)"
      >
        {{ copy.clearSelection }}
      </el-button>
    </div>
    <div v-if="visibleNodes.length === 0" class="organization-master-list__empty">
      {{ copy.empty }}
    </div>
    <div v-else class="organization-master-list__cards">
      <button
        v-for="entry in visibleNodes"
        :key="entry.node.nId"
        class="organization-master-list__card"
        :class="{ 'is-selected': selectedNId === entry.node.nId }"
        :style="{ '--organization-depth': entry.depth }"
        :aria-selected="selectedNId === entry.node.nId"
        :aria-expanded="entry.node.children.length ? isExpanded(entry.node.nId) : undefined"
        :tabindex="
          selectedNId === entry.node.nId ||
          (!selectedNId && visibleNodes[0]?.node.nId === entry.node.nId)
            ? 0
            : -1
        "
        role="treeitem"
        :data-testid="`organization-card-${entry.node.nId}`"
        type="button"
        @click="select(entry.node.nId)"
        @keydown="onKeydown($event, entry.node.nId)"
      >
        <span class="organization-master-list__name" :title="entry.node.name">
          <span v-if="entry.node.children.length" aria-hidden="true">{{
            isExpanded(entry.node.nId) ? '▾' : '▸'
          }}</span>
          {{ entry.node.name }}
        </span>
        <span
          class="organization-master-list__path"
          v-if="entry.ancestors.length"
          :title="entry.ancestors.join(' / ')"
        >
          {{ entry.ancestors.join(' / ') }}
        </span>
        <span class="organization-master-list__meta">
          <small>{{ entry.node.nId }}</small>
          <small>{{ systemDataEnumLabel(localization.locale, entry.node.type) }}</small>
          <small>{{ systemDataEnumLabel(localization.locale, entry.node.status) }}</small>
        </span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.organization-master-list {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: var(--ip-space-3);
}

.organization-master-list__toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
}

.organization-master-list__toolbar :deep(.el-input) {
  min-width: 180px;
  flex: 1 1 180px;
}

.organization-master-list__cards {
  display: grid;
  gap: var(--ip-space-2);
}

.organization-master-list__card {
  display: grid;
  min-width: 0;
  gap: 3px;
  padding: var(--ip-space-3);
  padding-left: calc(var(--ip-space-3) + var(--organization-depth) * var(--ip-space-4));
  color: var(--ip-color-text-primary);
  text-align: left;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.organization-master-list__card:hover,
.organization-master-list__card.is-selected {
  border-color: var(--ip-color-primary);
  background: var(--ip-color-primary-light-9);
}

.organization-master-list__name {
  overflow: hidden;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.organization-master-list__path {
  overflow: hidden;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.organization-master-list__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
</style>
