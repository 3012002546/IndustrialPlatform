<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/authStore'
import { getStudioApi, type Json, type StudioRuntime } from '@/api/studio'
import { useStudioCopy } from './copy'
import { createCustomBridge } from './customBridge'

const props = defineProps<{ runtime: StudioRuntime; preview?: boolean; parameters?: Json }>()
const auth = useAuthStore()
const t = useStudioCopy(),
  frame = ref<HTMLIFrameElement | null>(null)
const src = ref(''),
  bridgeId = ref(''),
  error = ref(''),
  loading = ref(false),
  pendingKey = ref('')
let bridge: ReturnType<typeof createCustomBridge> | undefined,
  generation = 0,
  loads = 0
let timeout: ReturnType<typeof setTimeout> | undefined
const storageKey = () =>
  `studio.custom.pending.${auth.user?.tenantId ?? ''}.${auth.user?.userId ?? ''}.${props.runtime.resource.nId}`
function remember(key: string) {
  pendingKey.value = key
  try {
    if (key) sessionStorage.setItem(storageKey(), key)
    else sessionStorage.removeItem(storageKey())
  } catch {
    /* in-memory guard remains */
  }
}
async function checkOperation() {
  try {
    const result = await getStudioApi().operation(pendingKey.value)
    if (!props.preview && !['Unknown', 'Pending'].includes(String(result.status))) remember('')
    else error.value = t.value('unknown')
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
}
async function execute(method: 'query' | 'executeAction', args: Json): Promise<unknown> {
  const api = getStudioApi(),
    runtime = props.runtime
  if (method === 'query') {
    if (
      typeof args.instanceId !== 'string' ||
      !((runtime.document.instances as Json[]) ?? []).some((i) => i.instanceId === args.instanceId)
    )
      throw new Error('STUDIO_FORBIDDEN')
    const parameters = { ...props.parameters, ...(args.pageParameters ?? {}) }
    const result = props.preview
      ? await api.preview(runtime.resource.nId, runtime.document, parameters)
      : await api.runtime(runtime.resource.nId, runtime.revisionNId, parameters)
    return result.instances[args.instanceId]
  }
  if (pendingKey.value && !props.preview) throw new Error('STUDIO_OPERATION_UNKNOWN')
  const action = ((runtime.document.actions as Json[]) ?? []).find((a) => a.id === args.actionId)
  if (!action || !args.context || typeof args.context !== 'object' || Array.isArray(args.context))
    throw new Error('STUDIO_FORBIDDEN')
  if (action.confirmText && !props.preview)
    await ElMessageBox.confirm(String(action.confirmText), String(action.label || action.id))
  const key = crypto.randomUUID()
  const context = { ...args.context, pageParameters: props.parameters ?? {} }
  if (!props.preview) remember(key)
  try {
    const result = await api.action(
      runtime.resource.nId,
      String(action.id),
      props.preview
        ? { pageDocument: runtime.document, context }
        : { revisionNId: runtime.revisionNId, context },
      !!props.preview,
      key,
    )
    if (!props.preview && !['Unknown', 'Pending'].includes(String(result.status))) remember('')
    return result
  } catch (e) {
    if (
      !props.preview &&
      e instanceof ApiError &&
      !['network', 'timeout', 'server', 'unknown', 'invalidResponse'].includes(e.kind) &&
      e.details.code !== 'STUDIO_OPERATION_UNKNOWN'
    )
      remember('')
    throw e
  }
}
function close() {
  bridge?.close()
  bridge = undefined
  src.value = ''
  clearTimeout(timeout)
}
async function load() {
  const request = ++generation
  close()
  loads = 0
  error.value = ''
  loading.value = true
  try {
    try {
      pendingKey.value = props.preview ? '' : (sessionStorage.getItem(storageKey()) ?? '')
    } catch {
      /* optional storage */
    }
    const content = await getStudioApi().customContent(props.runtime.resource.nId, {
      revisionNId: props.preview ? null : props.runtime.revisionNId,
      draftVersion: props.preview ? props.runtime.resource.optimisticVersion : null,
      parameters: props.parameters ?? {},
    })
    if (request !== generation) return
    if (content.mode === 'Html') {
      bridgeId.value = content.bridgeId
      bridge = createCustomBridge(
        () => frame.value?.contentWindow ?? null,
        content.bridgeId,
        execute,
      )
      src.value = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '') + content.contentUrl
    } else {
      const url = new URL(content.url)
      if (url.origin === window.location.origin || url.protocol !== 'https:')
        throw new Error('STUDIO_CUSTOM_URL_FORBIDDEN')
      src.value = url.href
    }
    timeout = setTimeout(() => {
      if (loading.value) {
        loading.value = false
        error.value = t.value('customLoadTimeout')
      }
    }, 15000)
  } catch (e) {
    if (request === generation) {
      error.value = e instanceof Error ? e.message : t.value('error')
      loading.value = false
    }
  }
}
function loaded() {
  clearTimeout(timeout)
  loading.value = false
  loads++
  if (props.runtime.document.mode !== 'Html') return
  if (loads > 1) {
    close()
    error.value = t.value('customChannelClosed')
    return
  }
  frame.value?.contentWindow?.postMessage({ type: 'init', bridgeId: bridgeId.value }, '*')
}
const receive = (event: MessageEvent) => {
  void bridge?.receive(event)
}
window.addEventListener('message', receive)
watch(
  () => [
    props.runtime.resource.nId,
    props.runtime.revisionNId,
    props.runtime.resource.optimisticVersion,
    props.parameters,
  ],
  () => void load(),
  { immediate: true, deep: true },
)
onBeforeUnmount(() => {
  ++generation
  close()
  window.removeEventListener('message', receive)
})
</script>

<template>
  <div class="studio-custom-runtime">
    <div class="studio-tools">
      <el-button :loading="loading" @click="load">{{ t('refresh') }}</el-button
      ><el-button v-if="pendingKey" @click="checkOperation"
        >{{ t('check') }} · {{ pendingKey }}</el-button
      >
    </div>
    <el-alert v-if="pendingKey" :title="t('unknown')" type="warning" :closable="false" />
    <el-alert v-if="error" :title="error" type="warning" :closable="false" />
    <el-alert
      v-if="runtime.document.mode === 'Link'"
      :title="t('embedLoadHelp')"
      type="info"
      :closable="false"
    />
    <el-skeleton v-if="loading" :rows="3" animated />
    <iframe
      v-if="src"
      ref="frame"
      :src="src"
      :title="runtime.resource.name"
      :sandbox="
        runtime.document.mode === 'Html'
          ? 'allow-scripts'
          : 'allow-scripts allow-forms allow-same-origin'
      "
      referrerpolicy="no-referrer"
      @load="loaded"
    />
  </div>
</template>

<style scoped>
.studio-custom-runtime {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 65vh;
}
iframe {
  width: 100%;
  flex: 1;
  min-height: 65vh;
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
}
</style>
