<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import { MAX_TRACE_FILE_BYTES, parseTraceDesign, TraceDesignFileError } from './designFile'
import { useTraceCopy } from './copy'
import type { TraceDocument } from './model'
const emit = defineEmits<{ close: []; apply: [document: TraceDocument] }>()
const t = useTraceCopy(),
  candidate = ref<ReturnType<typeof parseTraceDesign>>(),
  error = ref(''),
  reading = ref(false)
let generation = 0
async function read(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  const current = ++generation
  candidate.value = undefined
  error.value = ''
  if (!file) return
  reading.value = true
  try {
    if (file.size > MAX_TRACE_FILE_BYTES) throw new TraceDesignFileError('designFileSize')
    const value = parseTraceDesign(await file.text())
    if (current === generation) candidate.value = value
  } catch (e) {
    if (current === generation)
      error.value = t(e instanceof TraceDesignFileError ? e.reason : 'designFileInvalid')
  } finally {
    if (current === generation) reading.value = false
  }
}
onBeforeUnmount(() => generation++)
</script>
<template>
  <AppFormDrawer
    :model-value="true"
    :title="t('importDesign')"
    mode="modal"
    size="wide"
    @update:model-value="emit('close')"
  >
    <p>{{ t('importHint') }}</p>
    <label
      >{{ t('chooseFile')
      }}<input
        type="file"
        accept=".json,application/json"
        :aria-label="t('chooseFile')"
        @change="read"
    /></label>
    <p v-if="reading">{{ t('loading') }}</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div v-if="candidate" class="trace-import-summary">
      <h3>{{ candidate.name || t('importReady') }}</h3>
      <p>
        {{ t('object') }}: {{ candidate.document.objects.length }} · {{ t('relation') }}:
        {{ candidate.document.relations.length }}
      </p>
    </div>
    <template #footer
      ><el-button @click="emit('close')">{{ t('cancel') }}</el-button>
      <el-button
        type="primary"
        :disabled="reading || !candidate"
        @click="candidate && emit('apply', candidate.document)"
        >{{ t('importApply') }}</el-button
      >
    </template>
  </AppFormDrawer>
</template>
<style scoped>
label {
  display: grid;
  gap: 8px;
  margin-block: 16px;
}
.trace-import-summary {
  padding: 12px;
  margin-top: 16px;
  background: var(--el-fill-color-light);
  border-radius: var(--el-border-radius-base);
}
</style>
