<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type { Json } from '@/api/studio'
import {
  createDashboardScreen,
  dashboardScreenUrl,
  listDashboardScreens,
  revokeDashboardScreen,
  type DashboardScreenLink,
  type ScreenResourceKind,
} from '@/api/dashboardScreen'
import { useLocalizationStore } from '@/stores/localizationStore'
const props = withDefaults(
  defineProps<{
    nId: string
    revisionNId: string
    parameters: Json
    resourceKind?: ScreenResourceKind
  }>(),
  { resourceKind: 'Dashboard' },
)
const visible = defineModel<boolean>({ required: true })
const locale = useLocalizationStore()
const tr = (zh: string, en: string) => (locale.locale === 'en-US' ? en : zh)
const links = ref<DashboardScreenLink[]>([])
const showHistory = ref(false)
const displayedLinks = computed(() =>
  links.value.filter((link) => !!link.revokedOn === showHistory.value),
)
const hours = ref(0)
const followPublished = ref(true)
const url = ref('')
const error = ref('')
const busy = ref(false)
async function refresh() {
  links.value = await listDashboardScreens(props.nId, props.resourceKind)
}
async function guard(work: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await work()
  } catch (e) {
    error.value = e instanceof Error ? e.message : tr('操作失败', 'Operation failed')
  } finally {
    busy.value = false
  }
}
watch(visible, (open) => {
  if (open) {
    url.value = ''
    showHistory.value = false
    void guard(refresh)
  }
})
async function create() {
  await guard(async () => {
    const link = await createDashboardScreen(
      props.nId,
      props.revisionNId,
      props.parameters,
      hours.value === 0 ? null : hours.value,
      followPublished.value,
      props.resourceKind,
    )
    url.value = dashboardScreenUrl(link.id, link.key, props.resourceKind)
    await refresh()
  })
}
async function copy(value = url.value) {
  url.value = value
  try {
    await navigator.clipboard.writeText(value)
    ElMessage.success(tr('链接已复制', 'Link copied'))
  } catch {
    ElMessage.warning(tr('请选中链接后手动复制', 'Select the link and copy it manually'))
  }
}
</script>
<template>
  <el-dialog
    v-model="visible"
    :title="tr('独立大屏链接', 'Standalone screen links')"
    width="min(840px, 94vw)"
    destroy-on-close
  >
    <p>
      {{
        tr(
          '持有链接的人无需登录即可查看此大屏。参数固定，数据按发布者当前权限刷新；请仅分享给预期观看者。',
          'Anyone with the link can view this screen without signing in. Parameters stay fixed, and data refreshes with the publisher’s current permissions. Share only with intended viewers.',
        )
      }}
    </p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-form inline>
      <el-form-item :label="tr('有效期', 'Expires after')">
        <el-select v-model="hours" style="width: 140px" :disabled="busy">
          <el-option :value="0" :label="tr('无限期', 'Never expires')" />
          <el-option
            v-for="days in [1, 7, 30, 90]"
            :key="days"
            :value="days * 24"
            :label="`${days} ${tr('天', 'days')}`"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="tr('版本更新', 'Version updates')">
        <el-select v-model="followPublished" style="width: 190px" :disabled="busy">
          <el-option :value="true" :label="tr('跟随最新发布版', 'Follow latest published')" />
          <el-option :value="false" :label="tr('固定生成时版本', 'Pin current version')" />
        </el-select>
      </el-form-item>
      <el-form-item
        ><el-button type="primary" :loading="busy" :disabled="!revisionNId" @click="create">{{
          tr('生成链接', 'Create link')
        }}</el-button></el-form-item
      >
    </el-form>
    <p class="screen-policy-hint">
      {{
        followPublished
          ? tr(
              '保存草稿不影响大屏；发布后，原链接在下一次刷新时自动切换新版本（默认30秒内），无需更换链接。',
              'Saving a draft does not change the screen. After publishing, the same link switches to the latest version on its next refresh (within 30 seconds by default).',
            )
          : tr(
              '此链接固定生成时的发布版本，后续发布调整不会影响它。',
              'This link stays on the version selected at creation; later publications do not affect it.',
            )
      }}
    </p>
    <div v-if="url" class="screen-link-result">
      <el-input :model-value="url" readonly :aria-label="tr('大屏链接', 'Screen link')" />
      <el-button @click="copy()">{{ tr('复制', 'Copy') }}</el-button>
      <a :href="url" target="_blank" rel="noopener noreferrer">{{
        tr('打开大屏', 'Open screen')
      }}</a>
      <small>{{
        tr(
          '有效链接可随时在下方列表重新复制；撤销后所有副本均失效。',
          'Active links can be copied again from the list. Revoking a link invalidates every copy.',
        )
      }}</small>
    </div>
    <el-radio-group v-model="showHistory" class="screen-link-tabs">
      <el-radio-button :value="false">{{ tr('当前链接', 'Current links') }}</el-radio-button>
      <el-radio-button :value="true">{{ tr('撤销历史', 'Revocation history') }}</el-radio-button>
    </el-radio-group>
    <el-table
      :data="displayedLinks"
      max-height="320"
      :empty-text="
        showHistory ? tr('暂无撤销记录', 'No revocations') : tr('暂无大屏链接', 'No screen links')
      "
    >
      <el-table-column :label="tr('创建时间', 'Created')" min-width="170"
        ><template #default="{ row }">{{
          new Date(row.createdOn).toLocaleString()
        }}</template></el-table-column
      >
      <el-table-column :label="tr('到期时间', 'Expires')" min-width="170"
        ><template #default="{ row }">{{
          row.expiresOn ? new Date(row.expiresOn).toLocaleString() : tr('无限期', 'Never expires')
        }}</template></el-table-column
      >
      <el-table-column :label="tr('状态', 'Status')" width="95"
        ><template #default="{ row }">{{
          row.revokedOn
            ? tr('已撤销', 'Revoked')
            : row.expiresOn && Date.parse(row.expiresOn) <= Date.now()
              ? tr('已过期', 'Expired')
              : tr('有效', 'Active')
        }}</template></el-table-column
      >
      <el-table-column :label="tr('版本更新', 'Version updates')" min-width="155">
        <template #default="{ row }">{{
          row.followPublished
            ? tr('跟随最新发布版', 'Follow latest published')
            : tr('固定生成时版本', 'Pinned version')
        }}</template>
      </el-table-column>
      <el-table-column v-if="showHistory" :label="tr('撤销时间', 'Revoked')" min-width="170">
        <template #default="{ row }">{{ new Date(row.revokedOn).toLocaleString() }}</template>
      </el-table-column>
      <el-table-column v-else :label="tr('操作', 'Actions')" width="140"
        ><template #default="{ row }">
          <el-button
            link
            :disabled="busy || !row.key"
            @click="copy(dashboardScreenUrl(row.id, row.key, resourceKind))"
            >{{ tr('复制', 'Copy') }}</el-button
          >
          <el-button
            link
            type="danger"
            :disabled="busy || !!row.revokedOn"
            @click="
              guard(async () => {
                await revokeDashboardScreen(nId, row.id, resourceKind)
                url = ''
                await refresh()
              })
            "
            >{{ tr('撤销', 'Revoke') }}</el-button
          ></template
        ></el-table-column
      >
    </el-table>
  </el-dialog>
</template>
<style scoped>
p {
  line-height: 1.6;
  margin: 0 0 20px;
}
.screen-policy-hint {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.screen-link-tabs {
  margin-bottom: 12px;
}
.screen-link-result {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 20px;
}
.screen-link-result .el-input {
  flex: 1;
  min-width: 200px;
}
small {
  width: 100%;
  color: var(--el-text-color-secondary);
}
</style>
