<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox, ElMessage } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { PermissionGate } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { loadDashboardDesigner, loadPlaylistDesigner } from '@/router/studioRoutes'
import {
  getStudioApi,
  type Json,
  type StudioResource,
  type StudioSource,
  type StudioRevisionSummary,
} from '@/api/studio'
import { initialReportDocument } from '@/components/studio/report/model'
import { initialDocument, safeTotal } from '@/components/studio/shared/model'
import { initialDashboardDocument, initialPlaylistDocument } from './dashboard/model'
import { useStudioCopy } from '@/components/studio/shared/copy'
import '@/components/studio/shared/studio.css'

const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy()
const auth = useAuthStore(),
  lowCodeReady = ref(false),
  dashboardReady = ref(false)
const isSource = computed(() => route.path.endsWith('/sources'))
const isDashboard = computed(() => route.path.endsWith('/dashboards'))
const isPlaylist = computed(() => route.path.endsWith('/playlists'))
const kind = computed(() =>
  route.path.endsWith('/reports')
    ? 'Report'
    : route.path.endsWith('/report-bundles')
      ? 'ReportBundle'
      : route.path.endsWith('/datasets')
        ? 'Dataset'
        : route.path.endsWith('/ledger-models')
          ? 'LedgerModel'
          : isDashboard.value
            ? 'Dashboard'
            : isPlaylist.value
              ? 'Playlist'
              : 'LowCodePage',
)
const section = computed(() =>
  kind.value === 'Report'
    ? 'reports'
    : kind.value === 'ReportBundle'
      ? 'reportBundles'
      : isSource.value
        ? 'sources'
        : kind.value === 'Dataset'
          ? 'datasets'
          : kind.value === 'LedgerModel'
            ? 'ledger'
            : kind.value === 'Dashboard'
              ? 'dashboards'
              : kind.value === 'Playlist'
                ? 'playlists'
                : 'lowcode',
)
const permission = computed(() =>
  kind.value === 'Report' || kind.value === 'ReportBundle'
    ? 'studio.report.design'
    : isSource.value
      ? 'studio.source.manage'
      : kind.value === 'Dataset'
        ? 'studio.dataset.design'
        : kind.value === 'Dashboard' || kind.value === 'Playlist'
          ? 'studio.dashboard.design'
          : 'studio.lowcode.design',
)
const keyword = ref(''),
  status = ref(''),
  error = ref(''),
  unavailable = ref(''),
  busy = ref(false),
  queryMode = ref<'top' | 'header'>('top')
const table = ref<{
  reload: () => Promise<void>
  setHeaderFilter: (field: string, value: unknown) => void
} | null>(null)
watch(table, (value) => {
  if (value) void value.reload()
})
function changeQueryMode(mode: 'top' | 'header') {
  queryMode.value = mode
  if (mode === 'header') {
    table.value?.setHeaderFilter('name', keyword.value)
    table.value?.setHeaderFilter(
      'isEnabled',
      status.value === 'Enabled' ? true : status.value === 'Disabled' ? false : null,
    )
  }
}
function tableQuery(request: AppDataTableRequest) {
  if (request.queryMode !== 'header') return
  keyword.value = String(request.filters.name ?? '')
  status.value =
    request.filters.isEnabled === true
      ? 'Enabled'
      : request.filters.isEnabled === false
        ? 'Disabled'
        : ''
}
const drawer = ref(''),
  name = ref(''),
  description = ref(''),
  pageMode = ref('Crud'),
  note = ref('')
const current = ref<StudioResource | null>(null),
  revisions = ref<StudioRevisionSummary[]>([]),
  grants = ref<Json[]>([])
const source = ref<Json>({}),
  diagnostics = ref<unknown>(null)
const columns = computed(() => [
  { field: 'name', title: t.value('name'), minWidth: 180, sortable: true },
  ...(isSource.value
    ? [
        { field: 'provider', title: t.value('provider'), width: 150 },
        { field: 'configurationVersion', title: t.value('sourceVersion'), width: 140 },
        {
          field: 'credentialConfigured',
          title: t.value('credential'),
          width: 140,
          filter: false as const,
        },
      ]
    : [
        {
          field: 'publishedRevisionNo',
          title: t.value('version'),
          width: 130,
          filter: false as const,
        },
        { field: 'hasDraftChanges', title: t.value('draft'), width: 130, filter: false as const },
      ]),
  {
    field: 'isEnabled',
    title: t.value('status'),
    width: 110,
    filter: {
      kind: 'select' as const,
      options: [
        { label: t.value('enable'), value: true },
        { label: t.value('disable'), value: false },
      ],
    },
  },
  {
    field: 'lastUpdatedOn',
    title: t.value('updated'),
    minWidth: 170,
    sortable: true,
    filter: false as const,
  },
])
async function load(request: AppDataTableRequest) {
  error.value = ''
  try {
    const cap = await getStudioApi().capabilities()
    const lowCodeModule = cap.modules.find(
      (x) => x.key.toLowerCase().replaceAll('-', '') === 'lowcode',
    )
    const dashboardModule = cap.modules.find(
      (x) => x.key.toLowerCase().replaceAll('-', '') === 'dashboard',
    )
    lowCodeReady.value =
      !!lowCodeModule?.installed && !!lowCodeModule.enabled && !!lowCodeModule.available
    dashboardReady.value =
      !!dashboardModule?.installed && !!dashboardModule.enabled && !!dashboardModule.available
    const moduleReady =
      kind.value === 'Report' || kind.value === 'ReportBundle'
        ? !!cap.modules.find((m) => m.key === 'Report')?.available
        : kind.value === 'Dashboard' || kind.value === 'Playlist'
          ? dashboardReady.value
          : isSource.value || kind.value === 'Dataset' || lowCodeReady.value
    if (!cap.coreReady || !moduleReady) {
      unavailable.value =
        (kind.value === 'Dashboard' || kind.value === 'Playlist'
          ? dashboardModule?.reasonCode
          : lowCodeModule?.reasonCode) ?? t.value('unavailable')
      return { items: [], total: 0, pageIndex: request.pageIndex, pageSize: request.pageSize }
    }
    unavailable.value = ''
    const params = {
      keyword: request.queryMode === 'header' ? (request.filters.name ?? '') : keyword.value,
      status:
        request.queryMode === 'header'
          ? request.filters.isEnabled === true
            ? 'Enabled'
            : request.filters.isEnabled === false
              ? 'Disabled'
              : ''
          : status.value,
      page: request.pageIndex,
      pageSize: request.pageSize,
      sortBy: request.sort?.field ?? 'lastUpdatedOn',
      sortOrder: request.sort?.order ?? 'desc',
    }
    const result = isSource.value
      ? await getStudioApi().sources(params)
      : await getStudioApi().list(kind.value, params)
    return {
      items: result.items as unknown as Json[],
      total: safeTotal(result.totalCount),
      pageIndex: result.page,
      pageSize: result.pageSize,
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    throw e
  }
}
function refresh() {
  table.value?.reload()
}
function reset() {
  keyword.value = ''
  status.value = ''
  refresh()
}
function openCreate() {
  error.value = ''
  name.value = ''
  description.value = ''
  pageMode.value = 'Crud'
  source.value = {
    nId: crypto.randomUUID(),
    provider: 'RegisteredApi',
    endpointKey: '',
    catalogKey: '',
    allowedObjects: [],
    timeoutSeconds: 30,
    maxRows: 10000,
    maxBytes: '10485760',
  }
  drawer.value = 'create'
}
function design(row: Json) {
  void router.push(
    `/pc/studio/${kind.value === 'Report' ? 'reports' : kind.value === 'ReportBundle' ? 'report-bundles' : kind.value === 'Dataset' ? 'datasets' : kind.value === 'LedgerModel' ? 'ledger-models' : kind.value === 'Dashboard' ? 'dashboards' : kind.value === 'Playlist' ? 'playlists' : 'low-code'}/${encodeURIComponent(String(row.nId))}/design`,
  )
}
function run(row: Json) {
  void router.push(
    `/pc/studio/${kind.value === 'Report' ? 'reports' : kind.value === 'ReportBundle' ? 'report-bundles' : kind.value === 'Dashboard' ? 'dashboards' : kind.value === 'Playlist' ? 'playlists' : 'low-code'}/${encodeURIComponent(String(row.nId))}/run`,
  )
}
async function guarded(action: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await action()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
function save() {
  void guarded(async () => {
    if (drawer.value === 'grants' && current.value) {
      await getStudioApi().saveGrants(current.value, grants.value)
      drawer.value = ''
      refresh()
      return
    }
    if (!name.value.trim()) throw new Error(t.value('nameRequired'))
    if (isSource.value) {
      await getStudioApi().saveSource(
        { ...source.value, name: name.value, description: description.value },
        drawer.value === 'edit' ? (source.value.nId as string) : undefined,
      )
      drawer.value = ''
      refresh()
    } else {
      const document =
        kind.value === 'Report' || kind.value === 'ReportBundle'
          ? initialReportDocument(
              kind.value,
              pageMode.value === 'ElectronicRecord' ? 'ElectronicRecord' : 'Simple',
            )
          : kind.value === 'Dashboard'
            ? initialDashboardDocument()
            : kind.value === 'Playlist'
              ? initialPlaylistDocument()
              : initialDocument(kind.value)
      if (kind.value === 'LowCodePage') document.mode = pageMode.value
      const created = await getStudioApi().create({
        kind: kind.value,
        name: name.value,
        description: description.value,
        document,
      })
      drawer.value = ''
      design(created as unknown as Json)
    }
  })
}
async function more(row: Json, command: string) {
  await guarded(async () => {
    if (isSource.value) {
      const value = row as unknown as StudioSource
      if (command === 'edit') {
        source.value = JSON.parse(JSON.stringify(value)) as Json
        name.value = value.name
        description.value = value.description
        drawer.value = 'edit'
        return
      }
      const result = await getStudioApi().sourceAction(value, command)
      diagnostics.value = result
      drawer.value = 'diagnostics'
      refresh()
      return
    }
    current.value = row as unknown as StudioResource
    if (command === 'revisions') {
      revisions.value = await getStudioApi().revisions(current.value.nId)
      note.value = ''
      drawer.value = 'revisions'
    } else if (command === 'grants') {
      grants.value = await getStudioApi().grants(current.value.nId)
      drawer.value = 'grants'
    } else {
      await ElMessageBox.confirm(t.value('confirm') + ' ' + t.value(command), t.value(command))
      await getStudioApi().resourceAction(current.value, command)
      refresh()
    }
  })
}
function rollback(revision: StudioRevisionSummary) {
  void guarded(async () => {
    if (!current.value) return
    if (!note.value.trim()) throw new Error(t.value('releaseNote'))
    await getStudioApi().rollback(current.value, revision.nId, note.value)
    drawer.value = ''
    refresh()
    ElMessage.success(t.value('saved'))
  })
}
// 在用户查看列表时预热设计页代码；不缓存草稿或任何业务数据。
watch(
  kind,
  (value) => {
    if (value === 'Dashboard' && auth.hasPermission('studio.dashboard.design'))
      void loadDashboardDesigner().catch(() => undefined)
    if (value === 'Playlist' && auth.hasPermission('studio.dashboard.design'))
      void loadPlaylistDesigner().catch(() => undefined)
  },
  { immediate: true },
)
watch(
  () => route.path,
  () => {
    reset()
    drawer.value = ''
    unavailable.value = ''
  },
)
</script>

<template>
  <AppPage :title="t(section)" :description="t('description')" class="studio-page">
    <template #actions>
      <el-dropdown @command="(v: string) => router.push('/pc/studio/' + v)"
        ><el-button>{{ t('navigation') }}</el-button
        ><template #dropdown
          ><el-dropdown-menu
            ><el-dropdown-item v-if="auth.hasPermission('studio.source.view')" command="sources">{{
              t('sources')
            }}</el-dropdown-item
            ><el-dropdown-item
              v-if="auth.hasPermission('studio.dataset.view')"
              command="datasets"
              >{{ t('datasets') }}</el-dropdown-item
            ><el-dropdown-item
              v-if="lowCodeReady && auth.hasPermission('studio.lowcode.design')"
              command="ledger-models"
              >{{ t('ledger') }}</el-dropdown-item
            ><el-dropdown-item
              v-if="lowCodeReady && auth.hasPermission('studio.lowcode.view')"
              command="low-code"
              >{{ t('lowcode') }}</el-dropdown-item
            ><el-dropdown-item
              v-if="dashboardReady && auth.hasPermission('studio.dashboard.view')"
              command="dashboards"
              >{{ t('dashboards') }}</el-dropdown-item
            ><el-dropdown-item
              v-if="dashboardReady && auth.hasPermission('studio.dashboard.view')"
              command="playlists"
              >{{ t('playlists') }}</el-dropdown-item
            ></el-dropdown-menu
          ></template
        ></el-dropdown
      >
      <PermissionGate :permission-n-id="permission"
        ><el-button type="primary" :disabled="!!unavailable" @click="openCreate">{{
          t('create')
        }}</el-button></PermissionGate
      >
    </template>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="unavailable" :title="unavailable" type="warning" :closable="false" />
    <AppQueryPanel v-if="queryMode === 'top'" show-actions @submit="refresh" @reset="reset"
      ><div class="studio-query">
        <el-input
          v-model="keyword"
          :placeholder="t('name')"
          :aria-label="t('name')"
          clearable
          @keyup.enter="refresh"
        /><el-select v-model="status" :placeholder="t('status')" :aria-label="t('status')" clearable
          ><el-option :label="t('enable')" value="Enabled" /><el-option
            :label="t('disable')"
            value="Disabled" /><el-option v-if="!isSource" :label="t('unsaved')" value="Draft"
        /></el-select></div
    ></AppQueryPanel>
    <AppDataTable
      :key="section"
      ref="table"
      :table-key="'studio.' + section"
      :columns="columns"
      :loader="load"
      row-key="nId"
      :query-mode="queryMode"
      selection="none"
      :action-column-width="225"
      @query-mode-change="changeQueryMode"
      @query-change="tableQuery"
    >
      <template #cell-isEnabled="{ row }">{{ t(row.isEnabled ? 'yes' : 'no') }}</template
      ><template #cell-hasDraftChanges="{ row }">{{
        t(row.hasDraftChanges ? 'yes' : 'no')
      }}</template
      ><template #cell-credentialConfigured="{ row }">{{
        t(row.credentialConfigured ? 'yes' : 'no')
      }}</template>
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="permission"
          ><el-button v-if="!isSource" link type="primary" @click="design(row)">{{
            t('design')
          }}</el-button
          ><el-button v-else link type="primary" @click="more(row, 'edit')">{{
            t('edit')
          }}</el-button></PermissionGate
        >
        <PermissionGate
          :permission-n-id="
            kind === 'Report' || kind === 'ReportBundle'
              ? 'studio.report.run'
              : kind === 'Dashboard' || kind === 'Playlist'
                ? 'studio.dashboard.run'
                : 'studio.lowcode.run'
          "
          ><el-button
            v-if="
              (kind === 'LowCodePage' ||
                kind === 'Dashboard' ||
                kind === 'Playlist' ||
                kind === 'Report' ||
                kind === 'ReportBundle') &&
              !isSource
            "
            link
            type="primary"
            :disabled="!row.publishedRevisionNId || !row.isEnabled"
            @click="run(row)"
            >{{ t('run') }}</el-button
          ></PermissionGate
        >
        <el-dropdown @command="(c: string) => more(row, c)"
          ><el-button link>{{ t('more') }}</el-button
          ><template #dropdown
            ><el-dropdown-menu>
              <template v-if="isSource"
                ><PermissionGate permission-n-id="studio.source.test"
                  ><el-dropdown-item command="test">{{
                    t('test')
                  }}</el-dropdown-item></PermissionGate
                ><PermissionGate permission-n-id="studio.source.manage"
                  ><el-dropdown-item :command="row.isEnabled ? 'disable' : 'enable'">{{
                    t(row.isEnabled ? 'disable' : 'enable')
                  }}</el-dropdown-item></PermissionGate
                ></template
              >
              <template v-else
                ><el-dropdown-item command="revisions">{{ t('revisions') }}</el-dropdown-item
                ><PermissionGate permission-n-id="studio.resource.grant"
                  ><el-dropdown-item command="grants">{{
                    t('grants')
                  }}</el-dropdown-item></PermissionGate
                ><PermissionGate :permission-n-id="permission"
                  ><el-dropdown-item :command="row.isEnabled ? 'disable' : 'enable'">{{
                    t(row.isEnabled ? 'disable' : 'enable')
                  }}</el-dropdown-item
                  ><el-dropdown-item command="delete">{{ t('delete') }}</el-dropdown-item
                  ><el-dropdown-item command="restore">{{
                    t('restore')
                  }}</el-dropdown-item></PermissionGate
                ></template
              >
            </el-dropdown-menu></template
          ></el-dropdown
        >
      </template>
    </AppDataTable>
    <AppFormDrawer
      :model-value="!!drawer"
      :title="t(drawer === 'create' ? 'create' : drawer)"
      size="wide"
      :busy="busy"
      :hide-footer="['revisions', 'diagnostics'].includes(drawer)"
      @update:model-value="(v) => !v && (drawer = '')"
      @submit="save"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <el-form v-if="drawer === 'create' || drawer === 'edit'" label-position="top">
        <el-form-item v-if="kind === 'ReportBundle'" :label="t('mode')"
          ><el-radio-group v-model="pageMode"
            ><el-radio-button value="Simple">Simple</el-radio-button
            ><el-radio-button value="ElectronicRecord"
              >ElectronicRecord</el-radio-button
            ></el-radio-group
          ></el-form-item
        >
        <el-form-item :label="t('name')" required
          ><el-input v-model="name" maxlength="128" /></el-form-item
        ><el-form-item :label="t('descriptionField')"
          ><el-input v-model="description" type="textarea"
        /></el-form-item>
        <el-form-item v-if="!isSource && kind === 'LowCodePage'" :label="t('mode')"
          ><el-radio-group v-model="pageMode"
            ><el-radio-button value="Crud">CRUD</el-radio-button
            ><el-radio-button value="Compose">Compose</el-radio-button
            ><el-radio-button value="Code">Code</el-radio-button></el-radio-group
          ></el-form-item
        >
        <template v-if="isSource"
          ><el-alert :title="t('noSecret')" :closable="false" /><el-form-item :label="t('provider')"
            ><el-select v-model="source.provider" :disabled="drawer === 'edit'"
              ><el-option
                v-for="provider in ['RegisteredApi', 'PostgreSQL', 'SqlServer', 'Sqlite']"
                :key="provider"
                :label="provider"
                :value="provider" /></el-select></el-form-item
          ><el-form-item :label="t('endpoint')" required
            ><el-input v-model="source.endpointKey" /></el-form-item
          ><el-form-item :label="t('catalog')"
            ><el-input v-model="source.catalogKey" /></el-form-item
          ><el-form-item :label="t('objects')"
            ><div v-for="(obj, i) in source.allowedObjects as Json[]" :key="i" class="studio-grant">
              <el-select v-model="obj.kind"
                ><el-option
                  v-for="v in ['Api', 'Table', 'View', 'Procedure']"
                  :key="v"
                  :label="v"
                  :value="v" /></el-select
              ><el-input v-model="obj.qualifiedName" :placeholder="t('key')" /><el-button
                @click="source.allowedObjects.splice(i, 1)"
                >{{ t('remove') }}</el-button
              >
            </div>
            <el-button
              @click="
                source.allowedObjects.push({
                  kind: source.provider === 'RegisteredApi' ? 'Api' : 'View',
                  qualifiedName: '',
                })
              "
              >{{ t('add') }}</el-button
            ></el-form-item
          ><el-form-item :label="t('timeout')"
            ><el-input-number v-model="source.timeoutSeconds" :min="1" :max="300" /></el-form-item
          ><el-form-item :label="t('maxRows')"
            ><el-input-number v-model="source.maxRows" :min="1" :max="1000000" /></el-form-item
          ><el-form-item :label="t('maxBytes')"><el-input v-model="source.maxBytes" /></el-form-item
        ></template>
      </el-form>
      <template v-if="drawer === 'revisions'"
        ><el-input v-model="note" :placeholder="t('releaseNote')" />
        <div v-for="rev in revisions" :key="rev.nId" class="studio-revision">
          <div>
            <strong>v{{ rev.revisionNo }}</strong> · {{ rev.createdOn }}
            <p>{{ rev.releaseNote }}</p>
          </div>
          <PermissionGate
            :permission-n-id="
              kind === 'Dataset'
                ? 'studio.dataset.publish'
                : kind === 'Dashboard' || kind === 'Playlist'
                  ? 'studio.dashboard.publish'
                  : 'studio.lowcode.publish'
            "
            ><el-button :disabled="busy || !note.trim()" @click="rollback(rev)">{{
              t('rollback')
            }}</el-button></PermissionGate
          >
        </div>
        <el-empty v-if="!revisions.length" :description="t('empty')"
      /></template>
      <template v-if="drawer === 'grants'"
        ><el-alert :title="t('permission')" :closable="false" />
        <div v-for="(grant, i) in grants" :key="i" class="studio-grant">
          <el-select v-model="grant.subjectKind" :aria-label="t('subjectKind')"
            ><el-option label="User" value="User" /><el-option
              label="Role"
              value="Role" /></el-select
          ><el-input
            v-model="grant.subjectNId"
            :placeholder="t('subject')"
            :aria-label="t('subject')"
          /><el-select v-model="grant.capability" :aria-label="t('capability')"
            ><el-option
              v-for="c in ['View', 'Design', 'Publish', 'Run', 'Export']"
              :key="c"
              :label="c"
              :value="c" /></el-select
          ><el-button @click="grants.splice(i, 1)">{{ t('remove') }}</el-button>
        </div>
        <el-button
          @click="grants.push({ subjectKind: 'User', subjectNId: '', capability: 'View' })"
          >{{ t('add') }}</el-button
        ></template
      >
      <pre v-if="drawer === 'diagnostics'" class="studio-json">{{
        JSON.stringify(diagnostics, null, 2)
      }}</pre>
    </AppFormDrawer>
  </AppPage>
</template>
