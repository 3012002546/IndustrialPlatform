<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import StudioFileField from '@/components/studio/shared/StudioFileField.vue'
import { downloadBlob } from '@/components/management/download'
import { PermissionGate, usePermission } from '@/permissions'
import { getStudioApi, type Json } from '@/api/studio'
import { useReportCopy } from '@/components/studio/report/copy'
import { useStudioCopy } from '@/components/studio/shared/copy'
import { useOfficeSave } from '@/components/studio/report/useOfficeSave'
import { pendingOperation } from '@/components/studio/shared/pendingOperation'
const api = getStudioApi(),
  permissions = usePermission(),
  t = useReportCopy(),
  shared = useStudioCopy(),
  error = ref(''),
  busy = ref(false),
  closing = ref(false),
  drawer = ref(''),
  name = ref(''),
  versionNote = ref(''),
  sourceFile = ref<Json | null>(null),
  versions = ref<Json[]>([]),
  selected = ref<Json>(),
  session = ref<Json>(),
  sessionWorkbookNId = ref(''),
  editorForm = ref<HTMLFormElement>(),
  editorFrame = ref<HTMLIFrameElement>(),
  confirmedVersion = ref(''),
  historyVersion = ref(''),
  hasWorkingCopy = ref(false),
  table = ref<{ reload: () => Promise<void> }>()
const office = useOfficeSave(postOffice, (kind) =>
  t.value(
    kind === 'notReady'
      ? 'workbookNotReady'
      : kind === 'timeout'
        ? 'workbookSaveTimeout'
        : 'workbookSaveFailed',
  ),
)
let versionTimer: ReturnType<typeof setTimeout> | undefined
let generation = 0
let renewing = false
let disposed = false
const importRequest = pendingOperation()
// Drawers/dialogs mount their lazy slots after the open state changes.
watch(
  [editorForm, editorFrame],
  ([form, frame]) => {
    if (form && frame && session.value) form.submit()
  },
  { flush: 'post' },
)
function importKey(input: Json) {
  return importRequest.key([drawer.value, selected.value?.nId, input])
}
function invalidateSession() {
  generation++
  if (versionTimer) clearTimeout(versionTimer)
  versionTimer = undefined
  office.reset()
}
function postOffice(messageId: string, values: Json = {}) {
  if (!session.value || !editorFrame.value?.contentWindow) return
  editorFrame.value.contentWindow.postMessage(
    JSON.stringify({ MessageId: messageId, SendTime: Date.now(), Values: values }),
    new URL(String(session.value.editorUrl)).origin,
  )
}
async function renewToken() {
  if (
    renewing ||
    closing.value ||
    !session.value ||
    (!session.value.slidingSession && !session.value.supportsTokenRefresh) ||
    !sessionWorkbookNId.value
  )
    return
  renewing = true
  const own = generation,
    id = String(session.value.sessionNId)
  try {
    const result = await api.renewWorkbookSession(sessionWorkbookNId.value, id)
    if (own !== generation || session.value?.sessionNId !== id) return
    session.value = { ...session.value, expiresOn: result.expiresOn }
    if (result.accessToken) {
      session.value.accessToken = result.accessToken
      postOffice('Reset_Access_Token', {
        token: result.accessToken,
        ttl: session.value.slidingSession ? 0 : new Date(result.expiresOn).getTime(),
      })
    }
  } catch (e) {
    if (own === generation) error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    renewing = false
  }
}
function officeMessage(event: MessageEvent) {
  if (
    !session.value ||
    event.source !== editorFrame.value?.contentWindow ||
    event.origin !== new URL(String(session.value.editorUrl)).origin
  )
    return
  try {
    const message = typeof event.data === 'string' ? JSON.parse(event.data) : event.data
    office.message(message)
    if (message?.MessageId === 'UI_Save') saveFromToolbar()
    if (message?.MessageId === 'App_TokenExpiring' || message?.MessageId === 'App_TokenExpired')
      void renewToken()
  } catch {
    /* Ignore unrelated messages from the registered editor. */
  }
}
onMounted(() => {
  window.addEventListener('message', officeMessage)
  void table.value?.reload()
})
async function load() {
  const items = await api.workbooks()
  return { items, total: items.length, pageIndex: 1, pageSize: 200 }
}
async function guarded(fn: () => Promise<void>) {
  if (busy.value || closing.value) return
  busy.value = true
  error.value = ''
  try {
    await fn()
  } catch (e) {
    if (!disposed) error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
function importFile() {
  void guarded(async () => {
    if (!sourceFile.value?.fileNId) throw new Error(t.value('import'))
    const input = { name: name.value, sourceFileNId: sourceFile.value.fileNId }
    await api.importWorkbook(input, importKey(input))
    importRequest.clear()
    drawer.value = ''
    await table.value?.reload()
  })
}
function startImport() {
  if (busy.value || closing.value || session.value) return
  name.value = ''
  sourceFile.value = null
  importRequest.clear()
  drawer.value = 'import'
}
function open(row: Json) {
  void guarded(async () => {
    if (session.value) return
    const own = ++generation
    const workbookNId = String(row.nId)
    selected.value = row
    const result = await api.workbookSession(workbookNId, {
      mode: row.isArchived || !permissions.has('studio.workbook.edit') ? 'View' : 'Edit',
      baseContentVersion: row.contentVersion,
    })
    if (own !== generation || disposed) {
      await api.closeWorkbookSession(workbookNId, String(result.sessionNId))
      return
    }
    if (new URL(String(result.editorUrl)).protocol !== 'https:') {
      await api.closeWorkbookSession(workbookNId, String(result.sessionNId))
      throw new Error(t.value('error'))
    }
    session.value = result
    sessionWorkbookNId.value = workbookNId
    confirmedVersion.value = String(result.contentVersion)
    historyVersion.value = String(result.historyVersion)
    hasWorkingCopy.value = !!result.hasWorkingCopy
    drawer.value = 'editor'
    const refreshVersion = async () => {
      if (own !== generation || !session.value) return
      try {
        // Renew independently of history loading: a failed history request must not expire editing.
        if (new Date(session.value.expiresOn).getTime() - Date.now() < 300000) await renewToken()
        const state = await api.workbookState(workbookNId, String(result.sessionNId))
        if (own === generation) applyState(state)
      } catch (e) {
        if (own === generation) error.value = e instanceof Error ? e.message : t.value('error')
      }
      if (own === generation) versionTimer = setTimeout(() => void refreshVersion(), 10000)
    }
    versionTimer = setTimeout(() => void refreshVersion(), 10000)
  })
}
async function closeEditor() {
  if (closing.value || (session.value && busy.value)) return false
  closing.value = true
  try {
    if (session.value && sessionWorkbookNId.value) {
      if (session.value.mode === 'Edit' && office.ready.value) {
        if (office.modified.value) {
          try {
            await ElMessageBox.confirm(t.value('workbookUnsaved'), {
              confirmButtonText: t.value('workbookSaveAndClose'),
              cancelButtonText: t.value('workbookStay'),
            })
          } catch {
            return false
          }
        }
        await saveWorking()
      }
      await api.closeWorkbookSession(sessionWorkbookNId.value, String(session.value.sessionNId))
    }
    invalidateSession()
    session.value = undefined
    sessionWorkbookNId.value = ''
    drawer.value = ''
    await table.value?.reload()
    return true
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    return false
  } finally {
    closing.value = false
  }
}
onBeforeRouteLeave(closeEditor)
onBeforeUnmount(() => {
  window.removeEventListener('message', officeMessage)
  disposed = true
  invalidateSession()
  if (session.value && sessionWorkbookNId.value)
    void api
      .closeWorkbookSession(sessionWorkbookNId.value, String(session.value.sessionNId))
      .catch(() => {})
})
function history(row: Json) {
  void guarded(async () => {
    if (session.value) return
    const own = ++generation
    selected.value = row
    versions.value = []
    drawer.value = 'versions'
    const values = await api.workbookVersions(String(row.nId))
    if (own === generation) versions.value = values
  })
}
function archive(row: Json) {
  void guarded(async () => {
    await api.archiveWorkbook(String(row.nId), {
      optimisticVersion: row.optimisticVersion,
      concurrencyVersion: row.concurrencyVersion,
    })
    await table.value?.reload()
  })
}
function applyState(state: Json) {
  confirmedVersion.value = String(state.contentVersion)
  historyVersion.value = String(state.historyVersion)
  hasWorkingCopy.value = !!state.hasWorkingCopy
  if (selected.value?.nId === state.nId) selected.value = { ...selected.value, ...state }
}
async function saveWorking() {
  const own = generation,
    id = session.value?.sessionNId
  await office.save()
  if (own !== generation || id !== session.value?.sessionNId) return
  if (office.modified.value) throw new Error(t.value('workbookChangedDuringSave'))
  const state = await api.workbookState(sessionWorkbookNId.value, String(id))
  if (disposed || own !== generation || id !== session.value?.sessionNId) return
  applyState(state)
  error.value = ''
}
function saveFromToolbar() {
  const own = generation
  void saveWorking().catch((e) => {
    if (!disposed && own === generation)
      error.value = e instanceof Error ? e.message : t.value('error')
  })
}
function edit(row: Json) {
  if (busy.value || closing.value || session.value) return
  selected.value = row
  name.value = String(row.name)
  drawer.value = 'edit'
}
function startVersion(row?: Json) {
  if (busy.value || closing.value || office.saving.value) return
  if (row) {
    selected.value = row
    versionNote.value = ''
  }
  if (session.value)
    void guarded(async () => {
      await saveWorking()
      await checkpoint()
    })
  else drawer.value = 'checkpoint'
}
async function checkpoint() {
  if (!selected.value) return
  const state = await api.saveWorkbookVersion(String(selected.value.nId), {
    baseContentVersion: session.value ? confirmedVersion.value : selected.value.contentVersion,
    note: versionNote.value,
  })
  applyState(state)
  if (!session.value) drawer.value = ''
  versionNote.value = ''
  await table.value?.reload()
}
function submit() {
  if (drawer.value === 'edit')
    void guarded(async () => {
      if (!selected.value) return
      await api.editWorkbook(String(selected.value.nId), {
        name: name.value,
        optimisticVersion: selected.value.optimisticVersion,
        concurrencyVersion: selected.value.concurrencyVersion,
      })
      drawer.value = ''
      await table.value?.reload()
    })
  else if (drawer.value === 'checkpoint') void guarded(checkpoint)
  else if (drawer.value === 'copy') saveCopy()
  else importFile()
}
function copy(row: Json) {
  if (busy.value || closing.value || session.value) return
  importRequest.clear()
  selected.value = row
  name.value = `${row.name} (${t.value('workingCopy')})`
  drawer.value = 'copy'
}
function saveCopy() {
  void guarded(async () => {
    if (!selected.value) return
    const input = {
      name: name.value,
      baseContentVersion: selected.value.contentVersion,
    }
    await api.copyWorkbook(String(selected.value.nId), input, importKey(input))
    importRequest.clear()
    drawer.value = ''
    await table.value?.reload()
  })
}
function download(row: Json) {
  void guarded(async () => {
    if (!selected.value) return
    const blob = await api.client.getBlob?.(
      `/api/v1/studio/workbooks/${encodeURIComponent(String(selected.value.nId))}/versions/${encodeURIComponent(String(row.nId))}/content`,
    )
    if (!blob) throw new Error(t.value('error'))
    downloadBlob(blob, `${selected.value.name}-${row.contentVersion}.xlsx`)
  })
}
</script>
<template>
  <AppPage :title="t('books')"
    ><el-alert
      v-if="error && drawer !== 'editor'"
      :title="error"
      type="error"
      :closable="false"
    /><AppDataTable
      ref="table"
      table-key="studio.workbooks"
      row-key="nId"
      :loader="load"
      :columns="[
        { field: 'name', title: t('name') },
        { field: 'historyVersion', title: t('historyVersion') },
        { field: 'hasWorkingCopy', title: t('workbookDraft') },
        { field: 'isArchived', title: t('archive') },
      ]"
      ><template #cell-hasWorkingCopy="{ row }">{{
        t(row.hasWorkingCopy ? 'workbookDraft' : 'workbookPinned')
      }}</template
      ><template #cell-isArchived="{ row }">{{ shared(row.isArchived ? 'yes' : 'no') }}</template
      ><template #toolbar-actions
        ><PermissionGate permission-n-id="studio.workbook.edit"
          ><el-button :disabled="busy || closing" @click="startImport">{{
            t('import')
          }}</el-button></PermissionGate
        ></template
      ><template #actions="{ row }">
        <span class="workbook-actions">
          <el-button link type="primary" :disabled="busy || closing" @click="open(row)">{{
            t('open')
          }}</el-button>
          <el-dropdown trigger="click" :disabled="busy || closing">
            <el-button link :disabled="busy || closing">{{ shared('more') }}</el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item :disabled="busy || closing" @click="history(row)">{{
                  t('versions')
                }}</el-dropdown-item>
                <PermissionGate permission-n-id="studio.workbook.edit">
                  <el-dropdown-item
                    :disabled="row.isArchived || busy || closing"
                    @click="edit(row)"
                    >{{ t('workbookEdit') }}</el-dropdown-item
                  >
                  <el-dropdown-item
                    :disabled="row.isArchived || !row.hasWorkingCopy || busy || closing"
                    @click="startVersion(row)"
                    >{{ t('saveVersion') }}</el-dropdown-item
                  >
                  <el-dropdown-item :disabled="busy || closing" @click="copy(row)">{{
                    t('saveAs')
                  }}</el-dropdown-item>
                  <el-dropdown-item
                    :disabled="row.isArchived || busy || closing"
                    @click="archive(row)"
                    >{{ t('archive') }}</el-dropdown-item
                  >
                </PermissionGate>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </span>
      </template></AppDataTable
    ><AppFormDrawer
      :model-value="!!drawer"
      :title="
        t(
          drawer === 'import'
            ? 'import'
            : drawer === 'edit'
              ? 'workbookEdit'
              : drawer === 'checkpoint'
                ? 'saveVersion'
                : drawer === 'copy'
                  ? 'saveAs'
                  : drawer === 'editor'
                    ? 'books'
                    : 'versions',
        )
      "
      :size="drawer === 'editor' ? 'fullscreen' : 'wide'"
      :busy="busy || closing"
      :hide-footer="!['import', 'copy', 'edit', 'checkpoint'].includes(drawer)"
      @update:model-value="(v) => !v && closeEditor()"
      @submit="submit"
      ><template v-if="drawer === 'edit' || drawer === 'checkpoint'"
        ><el-alert :title="t('workbookPolicy')" type="info" :closable="false" /><el-form
          label-position="top"
          ><el-form-item v-if="drawer === 'edit'" :label="t('name')"
            ><el-input v-model="name" maxlength="128" /></el-form-item
          ><el-form-item v-else :label="t('workbookVersionNote')"
            ><el-input
              v-model="versionNote"
              type="textarea"
              maxlength="1000" /></el-form-item></el-form></template
      ><template v-else-if="drawer === 'copy'"
        ><el-form label-position="top"
          ><el-form-item :label="t('name')"
            ><el-input v-model="name" /></el-form-item></el-form></template
      ><template v-else-if="drawer === 'import'"
        ><el-form label-position="top"
          ><el-form-item :label="t('name')"><el-input v-model="name" /></el-form-item
          ><el-form-item :label="t('import')"
            ><StudioFileField
              v-model="sourceFile"
              model-n-id=""
              module-key="Report"
              purpose="WorkbookImport"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              :disabled="busy" /></el-form-item></el-form></template
      ><template v-else-if="drawer === 'editor' && session"
        ><div class="workbook-editor-status">
          <span
            >{{ selected?.name }} · {{ t('historyVersion') }}: {{ historyVersion }} ·
            {{ t(hasWorkingCopy ? 'workbookDraft' : 'workbookPinned') }} ·
            {{
              t(
                !office.ready.value
                  ? 'workbookLoading'
                  : office.saving.value
                    ? 'workbookSaving'
                    : office.modified.value
                      ? 'workbookModified'
                      : 'workbookSaved',
              )
            }}</span
          ><span v-if="session.mode === 'Edit'" class="workbook-editor-actions"
            ><el-button
              :disabled="!office.ready.value || office.saving.value || busy || closing"
              @click="saveFromToolbar"
              >{{ t('workbookSave') }}</el-button
            ><el-input
              v-model="versionNote"
              :placeholder="t('workbookVersionNote')"
              maxlength="1000"
            /><el-button
              :disabled="!office.ready.value || office.saving.value || busy || closing"
              @click="startVersion()"
              >{{ t('saveVersion') }}</el-button
            ></span
          >
        </div>
        <el-alert v-if="error" :title="error" type="error" :closable="false" />
        <form
          ref="editorForm"
          :action="session.editorUrl"
          method="post"
          target="studio-workbook-office"
        >
          <input type="hidden" name="access_token" :value="session.accessToken" /><input
            type="hidden"
            name="access_token_ttl"
            :value="session.slidingSession ? '0' : String(new Date(session.expiresOn).getTime())"
          />
        </form>
        <iframe
          ref="editorFrame"
          name="studio-workbook-office"
          :title="t('books')"
          sandbox="allow-scripts allow-same-origin allow-forms"
          referrerpolicy="no-referrer"
          @load="postOffice('Host_PostmessageReady')"
          class="workbook-office" /></template
      ><AppDataTable
        v-else-if="selected"
        table-key="studio.workbook.versions"
        row-key="nId"
        :rows="versions"
        :columns="[
          { field: 'contentVersion', title: t('historyVersion') },
          { field: 'sha256', title: 'SHA256' },
          { field: 'createdOn', title: 'UTC' },
          { field: 'note', title: t('workbookVersionNote') },
        ]"
        ><template #actions="{ row }"
          ><PermissionGate permission-n-id="studio.workbook.export"
            ><el-button link :disabled="busy" @click="download(row)">{{
              t('download')
            }}</el-button></PermissionGate
          ></template
        ></AppDataTable
      ></AppFormDrawer
    ></AppPage
  >
</template>
<style scoped>
.workbook-actions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  white-space: nowrap;
}
.workbook-office {
  width: 100%;
  flex: 1 1 auto;
  min-height: 0;
  border: 0;
}
.workbook-editor-status {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  flex-wrap: wrap;
  margin: 0;
  padding: var(--ip-space-1) var(--ip-space-4);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}
.workbook-editor-actions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
}
.workbook-editor-actions .el-input {
  width: 220px;
}
</style>
