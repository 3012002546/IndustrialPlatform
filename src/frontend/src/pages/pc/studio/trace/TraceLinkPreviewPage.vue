<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { getStudioApi } from '@/api/studio'
import { useTraceCopy } from '@/components/studio/trace/copy'
import { useTraceLinkCopy } from '@/components/studio/trace/linkCopy'

const route = useRoute(),
  t = useTraceCopy(),
  copy = useTraceLinkCopy()
const fileUrl = ref(''),
  title = ref(''),
  error = ref(''),
  loading = ref(false)
let generation = 0
let controller: AbortController | undefined
let expiryTimer: ReturnType<typeof setTimeout> | undefined
function release() {
  generation++
  controller?.abort()
  clearTimeout(expiryTimer)
  if (fileUrl.value) URL.revokeObjectURL(fileUrl.value)
  fileUrl.value = ''
  loading.value = false
}
watch(
  () => route.fullPath,
  async () => {
    release()
    error.value = ''
    title.value = typeof route.query.title === 'string' ? route.query.title : ''
    const current = generation
    const { nId, ticket, expiresOn } = route.query
    if (
      typeof nId !== 'string' ||
      !nId ||
      typeof ticket !== 'string' ||
      !ticket ||
      typeof expiresOn !== 'string'
    ) {
      error.value = copy('invalid')
      return
    }
    const remaining = Date.parse(expiresOn) - Date.now()
    if (!Number.isFinite(remaining) || remaining <= 0) {
      error.value = copy('expired')
      return
    }
    expiryTimer = setTimeout(
      () => {
        release()
        error.value = copy('expired')
      },
      Math.min(remaining, 2147483647),
    )
    loading.value = true
    controller = new AbortController()
    try {
      // The platform client supplies the current Native/ExternalMes identity. The ticket alone
      // cannot read bytes; the server rechecks the signed node and file permissions on every read.
      const blob = await getStudioApi().client.getBlob?.(
        `/api/v1/studio/trace/${encodeURIComponent(nId)}/link-content?ticket=${encodeURIComponent(ticket)}`,
        { signal: controller.signal },
      )
      if (current !== generation) return
      if (!blob) throw new Error('STUDIO_FILES_UNAVAILABLE')
      fileUrl.value = URL.createObjectURL(blob)
    } catch (cause) {
      if (current === generation)
        error.value = cause instanceof Error ? cause.message : copy('invalid')
    } finally {
      if (current === generation) loading.value = false
    }
  },
  { immediate: true },
)
onBeforeUnmount(release)
</script>

<template>
  <main class="trace-link-preview">
    <header>
      <h1>{{ title || copy('preview') }}</h1>
    </header>
    <p v-if="loading" role="status">{{ t('loading') }}</p>
    <el-alert v-else-if="error" :title="error" type="warning" :closable="false" />
    <iframe
      v-else-if="fileUrl"
      :src="fileUrl"
      :title="title || copy('preview')"
      sandbox=""
      referrerpolicy="no-referrer"
    />
  </main>
</template>

<style scoped>
.trace-link-preview {
  height: 100dvh;
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  box-sizing: border-box;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
}
h1 {
  margin: 0;
  font-size: var(--ip-font-size-lg);
}
iframe {
  width: 100%;
  box-sizing: border-box;
  min-height: 0;
  flex: 1;
  border: 1px solid var(--ip-color-border);
}
</style>
