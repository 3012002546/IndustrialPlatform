<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { getStudioApi, type Json, type StudioResource } from '@/api/studio'
import StudioFieldTree from '../shared/StudioFieldTree.vue'
import { rowBinding } from './model'
import { useTraceCopy } from './copy'
const model = defineModel<string>({ required: true })
const props = defineProps<{ datasets: StudioResource[] }>()
const t = useTraceCopy(),
  schema = ref<Json>(),
  error = ref(''),
  busy = ref(false)
let generation = 0
const selected = computed(() => props.datasets.find((d) => d.publishedRevisionNId === model.value))
watch(
  () => [model.value, props.datasets] as const,
  async ([revision]) => {
    const current = ++generation
    schema.value = undefined
    error.value = ''
    const resource = props.datasets.find((d) => d.publishedRevisionNId === revision)
    if (!resource || !revision) return
    busy.value = true
    try {
      const result = await getStudioApi().revision(resource.nId, revision)
      if (current === generation) schema.value = result.document.result
    } catch (e) {
      if (current === generation) error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (current === generation) busy.value = false
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  generation++
})
</script>
<template>
  <label
    >{{ t('dataset')
    }}<el-select v-model="model" filterable clearable :aria-label="t('dataset')"
      ><el-option
        v-for="d in datasets.filter((d) => d.publishedRevisionNId)"
        :key="d.nId"
        :value="d.publishedRevisionNId!"
        :label="`${d.name} · ${d.publishedRevisionNo}`" /><el-option
        v-if="model && !selected"
        :value="model"
        :label="model" /></el-select
  ></label>
  <small v-if="!datasets.length">{{ t('noPublished') }}</small
  ><small v-if="busy">{{ t('loading') }}</small
  ><small v-if="error" role="alert">{{ error }}</small>
  <details v-if="schema">
    <summary>{{ t('fields') }}</summary>
    <StudioFieldTree
      :sources="[{ label: selected?.name ?? model, schema, binding: rowBinding() }]"
    />
  </details>
</template>
