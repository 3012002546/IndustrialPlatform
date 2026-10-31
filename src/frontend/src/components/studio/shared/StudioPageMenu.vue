<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { useLocalizationStore } from '@/stores/localizationStore'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import {
  getStudioApi,
  type StudioMenuOptions,
  type StudioPublicationCapabilities,
  type StudioResource,
} from '@/api/studio'
import { useStudioCopy } from './copy'

const props = defineProps<{ resource: StudioResource | null; headless?: boolean }>()
const router = useRouter(),
  t = useStudioCopy()
const capabilities = ref<StudioPublicationCapabilities | null>(null)
const options = ref<StudioMenuOptions | null>(null)
const visible = ref(false),
  busy = ref(false),
  error = ref('')
const form = ref({
  nodeNId: null as string | null,
  parentNodeNId: '',
  label: '',
  iconKey: 'editpen',
  displayOrder: 0,
})
const canMount = computed(() => capabilities.value?.canMountMenu === true)
const native = computed(() => capabilities.value?.deploymentMode === 'PlatformNative')
const locale = useLocalizationStore()
const english = computed(() => locale.locale === 'en-US')
const text = (zh: string, en: string) => (english.value ? en : zh)
async function guard(action: () => Promise<void>) {
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
async function load() {
  capabilities.value = await getStudioApi().publicationCapabilities()
}
async function open() {
  visible.value = true
  options.value = null
  form.value = {
    nodeNId: null,
    parentNodeNId: '',
    label: props.resource?.name ?? '',
    iconKey: 'editpen',
    displayOrder: 0,
  }
  await guard(async () => {
    await load()
    if (!native.value || !props.resource) return
    options.value = await getStudioApi().pageMenus(props.resource.nId)
    const current = options.value.links.find((link) => !link.detached)
    if (current) edit(current)
  })
}
function edit(link: StudioMenuOptions['links'][number]) {
  form.value = {
    nodeNId: link.nodeNId,
    parentNodeNId: link.parentNodeNId,
    label: link.label,
    iconKey: link.iconKey ?? '',
    displayOrder: link.displayOrder,
  }
}
async function save() {
  await guard(async () => {
    if (!props.resource || !options.value) return
    const previous = new Set(options.value.links.map((link) => link.nodeNId))
    const editedNode = form.value.nodeNId
    options.value = await getStudioApi().mountPageMenu(props.resource.nId, {
      ...form.value,
      expectedRevision: options.value.revision,
    })
    const saved = options.value.links.find(
      (link) => link.nodeNId === editedNode || !previous.has(link.nodeNId),
    )
    if (saved) edit(saved)
    window.dispatchEvent(new Event('studio-navigation-changed'))
  })
}
async function detach(link: StudioMenuOptions['links'][number]) {
  try {
    await ElMessageBox.confirm(link.path, text('解除此菜单入口', 'Remove this menu entry'), {
      type: 'warning',
    })
  } catch {
    return
  }
  await guard(async () => {
    if (!props.resource || !options.value) return
    options.value = await getStudioApi().detachPageMenu(
      props.resource.nId,
      link.nodeNId,
      options.value.revision,
    )
    window.dispatchEvent(new Event('studio-navigation-changed'))
  })
}
function screen() {
  if (!props.resource) return
  const target = router.resolve({
    name: 'studio-lowcode-screen',
    params: { nId: props.resource.nId },
  })
  window.open(target.href, '_blank', 'noopener,noreferrer')
}
onMounted(() => {
  if (!props.headless) void guard(load)
})
watch(
  () => props.resource?.nId,
  () => {
    options.value = null
    visible.value = false
  },
)
defineExpose({ open, canMount })
</script>

<template>
  <el-button v-if="!headless && native && resource" :disabled="busy" @click="open">{{
    text('菜单关联', 'Menu entries')
  }}</el-button>
  <el-button v-if="!headless && resource?.publishedRevisionNId" @click="screen">{{
    text('全屏展示', 'Full screen')
  }}</el-button>
  <AppFormDrawer
    v-model="visible"
    :title="
      native ? text('页面菜单关联', 'Page menu entries') : text('独立展示', 'Standalone screen')
    "
    :busy="busy"
    hide-footer
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-button v-if="headless && resource?.publishedRevisionNId" @click="screen">{{
      text('全屏展示', 'Full screen')
    }}</el-button>
    <el-alert
      v-if="native"
      :title="
        text(
          '菜单沿用页面 Run 授权；可在资源授权中分配角色。这里只发布当前入口，不发布其他菜单草稿。',
          'Menu access follows page Run grants. Only this entry is published.',
        )
      "
      type="info"
      :closable="false"
    />
    <div v-for="link in native ? options?.links : []" :key="link.nodeNId" class="studio-revision">
      <div>
        <strong>{{ link.path }}</strong>
        <p>
          {{
            link.detached
              ? text('已解除，不自动恢复', 'Detached; not restored automatically')
              : resource?.isEnabled
                ? text('已挂载', 'Mounted')
                : text('页面停用，入口隐藏', 'Hidden while the page is disabled')
          }}
        </p>
      </div>
      <el-button :disabled="!canMount || busy" @click="edit(link)">{{
        text('编辑 / 重新挂载', 'Edit / reattach')
      }}</el-button>
      <el-button
        v-if="!link.detached"
        :disabled="!canMount || busy"
        type="danger"
        plain
        @click="detach(link)"
        >{{ text('解除', 'Detach') }}</el-button
      >
    </div>
    <el-form
      v-if="canMount && resource?.isEnabled && resource.publishedRevisionNId"
      label-position="top"
    >
      <el-form-item :label="text('已发布目录', 'Published group')" required
        ><el-select v-model="form.parentNodeNId"
          ><el-option
            v-for="(label, id) in options?.groups"
            :key="id"
            :label="label"
            :value="String(id)" /></el-select
      ></el-form-item>
      <el-form-item :label="text('菜单名称', 'Menu label')" required
        ><el-input v-model="form.label" :maxlength="128"
      /></el-form-item>
      <el-form-item :label="text('图标键', 'Icon key')"
        ><el-input v-model="form.iconKey" :maxlength="128"
      /></el-form-item>
      <el-form-item :label="text('排序', 'Order')"
        ><el-input-number v-model="form.displayOrder" :min="0" :max="100000"
      /></el-form-item>
      <el-button
        type="primary"
        :loading="busy"
        :disabled="!form.parentNodeNId || !form.label.trim()"
        @click="save"
        >{{
          form.nodeNId
            ? text('保存此菜单入口', 'Save this menu entry')
            : text('挂载并发布此入口', 'Publish this menu entry')
        }}</el-button
      >
      <el-button v-if="options?.links.length" :disabled="busy" @click="form.nodeNId = null">{{
        text('新增入口', 'New entry')
      }}</el-button>
      <el-button :disabled="busy" @click="open">{{
        text('刷新 / 重试', 'Refresh / retry')
      }}</el-button>
    </el-form>
    <el-alert
      v-else-if="native"
      :title="
        capabilities?.reasonCode ?? text('请先发布并启用页面', 'Publish and enable this page first')
      "
      type="info"
      :closable="false"
    />
  </AppFormDrawer>
</template>
