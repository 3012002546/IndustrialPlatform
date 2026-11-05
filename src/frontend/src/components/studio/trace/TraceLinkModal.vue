<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { TraceModalLink } from '@/api/studioTrace'
import { getStudioApi } from '@/api/studio'
import { ApiError } from '@/api/errors'
import { useTraceCopy } from './copy'
import { useTraceLinkCopy } from './linkCopy'
const props = defineProps<{ link: TraceModalLink | null }>()
const emit = defineEmits<{ close: []; denied: [message: string] }>()
const t = useTraceCopy(),
  linkCopy = useTraceLinkCopy(),
  expired = ref(false),
  failed = ref(false),
  fileUrl = ref(''),
  fileError = ref(''),
  loading = ref(false),
  popupBlocked = ref(false)
let expiryTimer: ReturnType<typeof setTimeout> | undefined
let generation = 0
let controller: AbortController | undefined
function release() {
  generation++
  controller?.abort()
  clearTimeout(expiryTimer)
  if (fileUrl.value) URL.revokeObjectURL(fileUrl.value)
  fileUrl.value = ''
  loading.value = false
}
watch(
  () => props.link,
  async (link) => {
    release()
    const current = generation
    failed.value = false
    expired.value = false
    fileError.value = ''
    popupBlocked.value = false
    if (link) {
      const remaining = Date.parse(link.expiresOn) - Date.now()
      if (!Number.isFinite(remaining) || remaining <= 0) expired.value = true
      else
        expiryTimer = setTimeout(
          () => {
            expired.value = true
            release()
          },
          Math.min(remaining, 2147483647),
        )
      if (link.kind === 'File' && link.embeddable && !expired.value) {
        try {
          const location = new URL(link.modalUrl, window.location.origin)
          if (
            location.origin !== window.location.origin ||
            !/\/api\/v1\/studio\/trace\/[^/]+\/link-content$/.test(location.pathname)
          )
            throw new Error('TRACE_LINK_INVALID')
          controller = new AbortController()
          loading.value = true
          // Use the platform client so Native Bearer and embedded session headers remain intact.
          const blob = await getStudioApi().client.getBlob?.(location.pathname + location.search, {
            signal: controller.signal,
          })
          if (generation !== current) return
          if (!blob) throw new Error('STUDIO_FILES_UNAVAILABLE')
          fileUrl.value = URL.createObjectURL(blob)
        } catch (e) {
          if (generation === current) {
            fileError.value = e instanceof Error ? e.message : String(e)
            if (e instanceof ApiError && ['unauthorized', 'forbidden', 'notFound'].includes(e.kind))
              emit('denied', fileError.value)
          }
        } finally {
          if (generation === current) loading.value = false
        }
      }
    }
  },
  { immediate: true },
)
onBeforeUnmount(release)
onDeactivated(release)
const url = computed(() => {
  if (!props.link?.embeddable || expired.value || failed.value) return ''
  if (props.link.kind === 'File') return fileUrl.value
  try {
    const value = new URL(props.link.modalUrl, window.location.origin)
    if (
      !['https:', 'http:'].includes(value.protocol) ||
      value.username ||
      value.password ||
      (value.origin !== window.location.origin && props.link.kind !== 'External')
    )
      return ''
    return value.href
  } catch {
    return ''
  }
})
const browserUrl = computed(() => {
  if (!props.link || expired.value || fileError.value) return ''
  try {
    const link = props.link
    const value = new URL(
      link.kind === 'File' ? link.newTabUrl || '' : link.modalUrl,
      window.location.origin,
    )
    if (value.username || value.password) return ''
    if (link.kind === 'External') return value.protocol === 'https:' ? value.href : ''
    const content = new URL(link.modalUrl, window.location.origin)
    const match = content.pathname.match(/^(.*)\/api\/v1\/studio\/trace\/([^/]+)\/link-content$/)
    const prefix = match?.[1]
    if (
      link.kind !== 'File' ||
      !link.newTabUrl ||
      prefix === undefined ||
      content.origin !== window.location.origin ||
      value.origin !== window.location.origin ||
      value.pathname !== prefix + '/screen/trace-link' ||
      value.searchParams.get('nId') !== decodeURIComponent(match?.[2] || '') ||
      !value.searchParams.get('ticket') ||
      value.searchParams.get('ticket') !== content.searchParams.get('ticket')
    )
      return ''
    return value.href
  } catch {
    return ''
  }
})
function openBrowserTab() {
  if (!props.link || Date.parse(props.link.expiresOn) <= Date.now() || !browserUrl.value) return
  popupBlocked.value = false
  if (props.link.kind === 'External') {
    window.open(browserUrl.value, '_blank', 'noopener,noreferrer')
    return
  }
  // The trusted same-origin blank inherits tab-scoped authentication. Disconnect its opener
  // synchronously, before navigating; no credentials are placed in the URL or passed to content.
  const tab = window.open('about:blank', '_blank')
  if (!tab) {
    popupBlocked.value = true
    return
  }
  tab.opener = null
  tab.location.replace(browserUrl.value)
}
</script>
<template>
  <AppFormDrawer
    :model-value="!!link"
    :title="link?.title || t('detail')"
    mode="modal"
    :allow-mode-switch="false"
    size="near-fullscreen"
    hide-footer
    @update:model-value="
      (v) => {
        if (!v) emit('close')
      }
    "
    @cancel="emit('close')"
  >
    <template #header-actions>
      <el-button :disabled="!browserUrl" data-testid="trace-link-new-tab" @click="openBrowserTab">
        {{ linkCopy('openBrowserTab') }}
      </el-button>
    </template>
    <el-alert
      v-if="popupBlocked"
      :title="linkCopy('popupBlocked')"
      type="warning"
      :closable="false"
    />
    <template v-if="url"
      ><p class="trace-link-hint">{{ linkCopy('fallback') }}</p>
      <iframe
        :src="url"
        :title="link?.title || t('detail')"
        :sandbox="link?.kind === 'External' ? 'allow-scripts allow-same-origin allow-forms' : ''"
        referrerpolicy="no-referrer"
        class="trace-link-frame"
        @error="failed = true"
    /></template>
    <p v-else-if="loading" role="status">{{ t('loading') }}</p>
    <el-alert
      v-else
      :title="expired ? linkCopy('expired') : fileError || t('blockedFrame')"
      type="warning"
      :closable="false"
    />
  </AppFormDrawer>
</template>
<style scoped>
.trace-link-frame {
  width: 100%;
  box-sizing: border-box;
  height: 100%;
  flex: 1 1 auto;
  min-height: 0;
  border: 1px solid var(--el-border-color);
}
.trace-link-hint {
  margin: 0 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
}
</style>
