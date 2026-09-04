<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { Refresh } from '@element-plus/icons-vue'

import AppErrorAlert from '@/components/base/AppErrorAlert.vue'
import AppLoadingState from '@/components/base/AppLoadingState.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppPermissionState from '@/components/base/AppPermissionState.vue'
import { localeMessages } from '@/localization/i18n'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import {
  type SystemDataAdminKind,
  useSystemDataManagementStore,
} from '@/stores/systemData/managementStore'

const props = defineProps<{
  kind: SystemDataAdminKind
  title: string
  description: string
  permission?: string
}>()
const authStore = useAuthStore()
const localization = useLocalizationStore()
const store = useSystemDataManagementStore()
const copy = computed(() => localeMessages[localization.locale].systemData.copy)
const hasPermission = computed(() => !props.permission || authStore.hasPermission(props.permission))
const recordCount = computed(() => {
  switch (props.kind) {
    case 'organizations':
      return flattenCount(store.organizationTree)
    case 'assignments':
      return store.assignments.length
    case 'navigation':
      // Navigation table pagination is root-level; descendants are exposed by the tree.
      return store.navigationDraft?.nodes.length ?? 0
    case 'features':
      return store.features.length
    case 'services':
      return store.services.length
    case 'themes':
      return store.themePolicy?.configured === false ? 0 : store.themePolicy === null ? 0 : 1
    case 'service-initialization':
      return store.initializationRegistrations?.total ?? 0
    default:
      return 0
  }
})
const statusMessage = computed(() => {
  const errorText = store.error ?? ''
  return /(?:409|CONFLICT|CONCURRENCY)/i.test(errorText) ? copy.value.conflict : errorText
})

function flattenCount(items: readonly unknown[]): number {
  return items.reduce<number>((total, item) => {
    if (typeof item !== 'object' || item === null) return total + 1
    const children = 'children' in item && Array.isArray(item.children) ? item.children : []
    return total + 1 + flattenCount(children)
  }, 0)
}

onMounted(() => {
  if (hasPermission.value) void store.load(props.kind)
})
</script>

<template>
  <AppPage class="systemdata-admin-frame" :title="title" :description="description">
    <template #heading-meta>
      <span
        class="systemdata-record-count"
        data-testid="systemdata-record-count"
        aria-live="polite"
      >
        {{ recordCount }} {{ copy.records }}
      </span>
    </template>
    <template #actions>
      <div v-if="hasPermission" class="systemdata-page-actions" :aria-label="copy.queryAndActions">
        <slot name="toolbar" />
        <el-button
          data-testid="systemdata-refresh"
          :disabled="store.loading"
          @click="store.retry(kind)"
        >
          <el-icon class="systemdata-page-action-icon" aria-hidden="true"><Refresh /></el-icon>
          {{ copy.refresh }}
        </el-button>
      </div>
    </template>
    <div data-testid="systemdata-admin-page" class="systemdata-admin-page" tabindex="0">
      <AppPermissionState v-if="!hasPermission" />
      <template v-else>
        <div class="systemdata-admin-content" :aria-busy="store.loading">
          <AppErrorAlert
            v-if="store.error"
            :title="copy.interfaceUnavailable"
            :message="statusMessage"
            :trace-id="store.traceId || ''"
            ><el-button link type="primary" @click="store.retry(kind)">{{
              copy.retry
            }}</el-button></AppErrorAlert
          >
          <slot :store="store" />
          <AppLoadingState v-if="store.loading" class="systemdata-admin-loading-overlay" />
        </div>
      </template>
    </div>
  </AppPage>
</template>

<style scoped>
.systemdata-admin-frame {
  display: flex;
  flex-direction: column;
  gap: 0;
  min-width: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}
.systemdata-admin-frame :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}
.systemdata-admin-frame :deep(.app-page__heading-row) {
  gap: var(--ip-space-4);
}
.systemdata-admin-frame :deep(.app-page__heading-meta) {
  min-height: 22px;
}
.systemdata-admin-frame :deep(.app-page__extensions) {
  align-items: flex-start;
}
.systemdata-admin-frame :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.systemdata-record-count {
  display: inline-flex;
  min-height: 22px;
  align-items: center;
  box-sizing: border-box;
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-full, 999px);
  font-size: var(--ip-font-size-xs);
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
}
.systemdata-admin-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}
.systemdata-admin-page:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: -2px;
}
.systemdata-page-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
}
.systemdata-page-actions {
  justify-content: flex-end;
}
.systemdata-page-actions :deep(.systemdata-page-action-icon) {
  display: inline-flex;
  flex: 0 0 14px;
  width: 14px;
  height: 14px;
  margin-right: 4px;
}
.systemdata-page-actions :deep(.systemdata-page-action-icon > svg) {
  width: 14px;
  height: 14px;
}
.systemdata-admin-content {
  position: relative;
  display: flex;
  flex: 0 0 auto;
  flex-direction: column;
  gap: var(--ip-space-4);
  padding: 18px 20px 20px;
  box-sizing: border-box;
  min-width: 0;
  min-height: 100%;
}
.systemdata-admin-loading-overlay {
  position: absolute;
  z-index: 2;
  inset: 0;
  margin: 0 !important;
  background: color-mix(in srgb, var(--ip-color-bg-container) 82%, transparent);
}

.systemdata-admin-frame :deep(.vxe-table--render-default) {
  --vxe-ui-font-color: var(--ip-color-text-primary);
  --vxe-ui-font-lighten-color: var(--ip-color-text-secondary);
  --vxe-ui-font-darken-color: var(--ip-color-text-primary);
  --vxe-ui-font-disabled-color: var(--ip-color-text-disabled);
  --vxe-ui-layout-background-color: var(--ip-color-bg-container);
  --vxe-ui-table-header-background-color: var(--ip-color-bg-muted);
  --vxe-ui-table-column-to-row-background-color: var(--ip-color-bg-muted);
  --vxe-ui-table-footer-background-color: var(--ip-color-bg-container);
  --vxe-ui-table-border-color: var(--ip-color-border);
  --vxe-ui-table-row-hover-background-color: var(--ip-color-bg-container);
  --vxe-ui-table-row-striped-background-color: var(--ip-color-bg-muted);
  --systemdata-table-action-color: color-mix(
    in srgb,
    var(--ip-color-primary) 40%,
    var(--ip-color-text-primary)
  );
  --systemdata-table-action-hover-color: color-mix(
    in srgb,
    var(--ip-color-primary-hover) 35%,
    var(--ip-color-text-primary)
  );
  --systemdata-table-selection-background: color-mix(
    in srgb,
    var(--ip-color-primary) 20%,
    var(--ip-color-bg-container)
  );
  --vxe-ui-table-row-current-background-color: var(--systemdata-table-selection-background);
  --vxe-ui-table-row-checkbox-checked-background-color: var(
    --systemdata-table-selection-background
  );
  --vxe-ui-table-row-radio-checked-background-color: var(--systemdata-table-selection-background);
  --vxe-ui-table-fixed-scrolling-box-shadow-color: color-mix(
    in srgb,
    var(--ip-color-text-primary) 22%,
    transparent
  );
}

.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-table--header-wrapper),
.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-table--body-wrapper),
.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-table--footer-wrapper),
.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-table--fixed-left-wrapper),
.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-table--fixed-right-wrapper) {
  color: var(--ip-color-text-primary);
  background-color: var(--ip-color-bg-container);
}

.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-header--column) {
  color: var(--ip-color-text-primary);
  background-color: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}

.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-body--column),
.systemdata-admin-frame :deep(.vxe-table--render-default .vxe-footer--column) {
  border-color: var(--ip-color-border);
}

.systemdata-admin-frame
  :deep(.vxe-table--render-default .vxe-body--row.row--stripe > .vxe-body--column) {
  background-color: color-mix(in srgb, var(--ip-color-bg-muted) 72%, var(--ip-color-bg-container));
}

.systemdata-admin-frame
  :deep(.vxe-table--render-default .vxe-body--row.row--current > .vxe-body--column),
.systemdata-admin-frame
  :deep(.vxe-table--render-default .vxe-body--row.row--checked > .vxe-body--column),
.systemdata-admin-frame
  :deep(.vxe-table--render-default .vxe-body--row.row--radio > .vxe-body--column) {
  background-color: var(--systemdata-table-selection-background);
}

.systemdata-admin-frame
  :deep(
    .vxe-table--render-default
      .app-data-table__actions-column
      .el-button--primary.is-link:not(.is-disabled):not(:disabled)
  ) {
  color: var(--systemdata-table-action-color);
}

.systemdata-admin-frame
  :deep(
    .vxe-table--render-default
      .app-data-table__actions-column
      .el-button--primary.is-link:not(.is-disabled):not(:disabled):hover
  ),
.systemdata-admin-frame
  :deep(
    .vxe-table--render-default
      .app-data-table__actions-column
      .el-button--primary.is-link:not(.is-disabled):not(:disabled):focus-visible
  ),
.systemdata-admin-frame
  :deep(
    .vxe-table--render-default
      .app-data-table__actions-column
      .el-button--primary.is-link:not(.is-disabled):not(:disabled):active
  ) {
  color: var(--systemdata-table-action-hover-color);
}

.systemdata-admin-page > :deep(.app-error-alert),
.systemdata-admin-page > :deep(.app-loading-state),
.systemdata-admin-page > :deep(.app-permission-state) {
  margin: 18px 20px 20px;
}

@media (max-width: 720px) {
  .systemdata-admin-frame :deep(.app-page__heading-row) {
    flex-direction: column;
  }

  .systemdata-admin-frame :deep(.app-page__extensions) {
    width: 100%;
  }

  .systemdata-page-actions {
    justify-content: flex-start;
  }
}
</style>
