<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getStudioApi, type Json, type StudioRuntime } from '@/api/studio'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import StudioRuntimeView from '@/components/studio/shared/StudioRuntimeView.vue'
import { useStudioCopy } from '@/components/studio/shared/copy'
import '@/components/studio/shared/studio.css'
const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  runtime = ref<StudioRuntime | null>(null),
  error = ref(''),
  busy = ref(false),
  parameters = ref<Json>({})
const contract = ref<StudioRuntime | null>(null),
  runParameters = ref<Json>({}),
  needsParameters = ref(false)
const definitions = computed(() =>
  ((contract.value?.document.parameters as Json[]) ?? []).filter(
    (p) => p.source !== 'TrustedContext',
  ),
)
const mode = computed(() =>
  route.path.endsWith('/records/new')
    ? 'new'
    : route.path.endsWith('/edit')
      ? 'edit'
      : route.params.recordNId
        ? 'detail'
        : undefined,
)
const recordNId = computed(() =>
  typeof route.params.recordNId === 'string' ? route.params.recordNId : undefined,
)
let generation = 0
function resetParameters() {
  parameters.value = {}
  void load()
}
async function load() {
  const request = ++generation
  busy.value = true
  error.value = ''
  try {
    const revisionNId =
      typeof route.query.revisionNId === 'string' ? route.query.revisionNId : undefined
    const schema = await getStudioApi().pageContract(String(route.params.nId), revisionNId)
    if (request !== generation) return
    contract.value = schema
    for (const p of definitions.value) {
      if (parameters.value[String(p.name)] === undefined) {
        const value = route.query[String(p.name)] ?? p.defaultValue
        if (value !== undefined)
          parameters.value[String(p.name)] =
            p.type === 'Int32'
              ? Number(value)
              : p.type === 'Boolean'
                ? value === 'true' || value === true
                : value
      }
    }
    needsParameters.value = definitions.value.some(
      (p) =>
        p.required &&
        (parameters.value[String(p.name)] == null ||
          String(parameters.value[String(p.name)]).trim() === ''),
    )
    if (needsParameters.value) {
      runtime.value = null
      return
    }
    const value = await getStudioApi().runtime(
      String(route.params.nId),
      schema.revisionNId,
      parameters.value,
    )
    if (request === generation) {
      runtime.value = value
      runParameters.value = { ...parameters.value }
    }
  } catch (e) {
    if (request === generation) error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (request === generation) busy.value = false
  }
}
watch(
  () => route.fullPath,
  () => void load(),
)
onMounted(load)
</script>
<template>
  <AppPage
    :title="runtime?.resource.name ?? contract?.resource.name ?? t('run')"
    class="studio-page"
    ><template #actions
      ><el-button @click="router.push('/pc/studio/low-code')">{{ t('back') }}</el-button
      ><el-button :disabled="busy" @click="load">{{ t('refresh') }}</el-button></template
    ><el-alert v-if="error" :title="error" type="error" :closable="false" />
    <AppQueryPanel v-if="definitions.length" show-actions @submit="load" @reset="resetParameters"
      ><div class="studio-form-grid">
        <el-form-item
          v-for="p in definitions"
          :key="String(p.name)"
          :label="String(p.label || p.name)"
          :required="p.required"
          ><el-switch
            v-if="p.type === 'Boolean'"
            v-model="parameters[String(p.name)]" /><el-date-picker
            v-else-if="p.type === 'Date'"
            v-model="parameters[String(p.name)]"
            type="date"
            value-format="YYYY-MM-DD" /><el-date-picker
            v-else-if="p.type === 'DateTime'"
            v-model="parameters[String(p.name)]"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ssZ" /><el-input-number
            v-else-if="p.type === 'Int32'"
            v-model="parameters[String(p.name)]" /><el-input
            v-else
            v-model="parameters[String(p.name)]"
            :aria-label="String(p.label || p.name)"
        /></el-form-item></div
    ></AppQueryPanel>
    <el-alert v-if="needsParameters" :title="t('paramsMissing')" type="info" :closable="false" />
    <el-skeleton v-if="busy && !runtime" :rows="8" animated /><StudioRuntimeView
      v-if="runtime"
      :key="String(route.params.recordNId ?? mode ?? 'list')"
      :runtime="runtime"
      :parameters="runParameters"
      :record-mode="mode"
      :record-n-id="recordNId"
      @refresh="load" /><el-empty
      v-else-if="!busy && !error && !needsParameters"
      :description="t('noRuntime')"
  /></AppPage>
</template>
