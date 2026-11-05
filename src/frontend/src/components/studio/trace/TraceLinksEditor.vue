<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { getTraceApi, type TraceLinkSources, type TraceResourceTarget } from '@/api/studioTrace'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceMappings from './TraceMappings.vue'
import { rowBinding, traceContexts, type TraceLink } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
import { useLocalizationStore } from '@/stores/localizationStore'
const model = defineModel<TraceLink[]>({ required: true })
const t = useTraceCopy()
const text = useTracePresentation()
const locale = useLocalizationStore()
const tr = (zh: string, en: string) => (locale.locale === 'en-US' ? en : zh)
const targets = ref<TraceResourceTarget[]>([])
const sources = ref<TraceLinkSources>(),
  sourceError = ref('')
let sourceGeneration = 0
async function loadSources() {
  const current = ++sourceGeneration
  sourceError.value = ''
  sources.value = undefined
  targets.value = []
  async function load<T>(request: () => Promise<T>, accept: (result: T) => void) {
    try {
      const result = await request()
      if (current === sourceGeneration) accept(result)
    } catch (e) {
      if (current !== sourceGeneration) return
      const message = e instanceof Error ? e.message : String(e)
      sourceError.value = [sourceError.value, message].filter(Boolean).join(' · ')
    }
  }
  await Promise.all([
    load(
      () => getTraceApi().linkSources(),
      (value) => {
        sources.value = value
      },
    ),
    load(
      () => getTraceApi().linkResources(),
      (value) => {
        targets.value = value.items
      },
    ),
  ])
}
function pinnedLabel(target: NonNullable<TraceLink['target']>) {
  const available = targets.value.find((entry) => entry.resourceNId === target.resourceNId)
  const revision = target.revisionNId.replace(/^revision-/, '').slice(0, 12)
  return (
    (available?.name ?? tr('已配置资源', 'Configured resource')) +
    ' · ' +
    tr('固定修订', 'Pinned revision') +
    ' ' +
    revision
  )
}
onMounted(loadSources)
onBeforeUnmount(() => {
  sourceGeneration++
})
const externalSource = (key: string | undefined) =>
  sources.value?.external.find((s) => s.key === key)
const mesSource = (key: string | undefined) => sources.value?.mes.find((s) => s.key === key)
function addLink() {
  model.value = [
    ...model.value,
    {
      id: crypto.randomUUID(),
      label: '',
      kind: 'File',
      value: rowBinding(),
      openMode: 'Modal',
      fileSource: { mode: 'Owned' },
    },
  ]
}
</script>
<template>
  <fieldset>
    <legend>{{ t('links') }}</legend>
    <p>{{ t('filePolicy') }}</p>
    <el-alert v-if="sourceError" :title="sourceError" type="warning" :closable="false"
      ><el-button @click="loadSources">{{ t('retry') }}</el-button></el-alert
    >
    <div v-for="(link, i) in model" :key="link.id" class="studio-field-block">
      <el-input v-model="link.label" :aria-label="t('label')" :placeholder="t('label')" />
      <el-select
        v-model="link.kind"
        :aria-label="t('kind')"
        @change="
          () => {
            delete link.target
            if (link.kind === 'Resource') {
              delete link.value
              delete link.fileSource
              delete link.allowedOriginKey
            } else if (link.kind === 'External') {
              link.value ??= rowBinding()
              delete link.fileSource
              link.allowedOriginKey = ''
            } else {
              link.value ??= rowBinding()
              delete link.allowedOriginKey
              link.fileSource = { mode: 'Owned' }
            }
          }
        "
        ><el-option value="File" :label="tr('附件', 'File')" /><el-option
          value="External"
          :label="t('external')" /><el-option
          value="Resource"
          :label="tr('平台已发布资源', 'Published platform resource')"
      /></el-select>
      <BindingEditor
        v-if="link.kind !== 'Resource'"
        :model-value="link.value ?? rowBinding()"
        :contexts="traceContexts"
        @update:model-value="(v) => (link.value = v)"
      />
      <template v-if="link.kind === 'Resource'">
        <el-select
          :model-value="link.target?.revisionNId"
          filterable
          :aria-label="tr('已发布资源及版本', 'Published resource and version')"
          :placeholder="tr('选择已发布资源及版本', 'Select published resource and version')"
          @update:model-value="
            (id: string) => {
              const target = targets.find((t) => t.revisionNId === id)
              if (target)
                link.target = {
                  kind: target.kind,
                  resourceNId: target.resourceNId,
                  revisionNId: target.revisionNId,
                }
            }
          "
        >
          <el-option
            v-if="
              link.target &&
              !targets.some((target) => target.revisionNId === link.target?.revisionNId)
            "
            :value="link.target.revisionNId"
            :label="pinnedLabel(link.target)"
            disabled
          />
          <el-option
            v-for="target in targets"
            :key="target.revisionNId"
            :value="target.revisionNId"
            :label="target.name + ' · v' + target.revisionNo + ' · ' + target.kind"
          />
        </el-select>
        <small>{{
          tr(
            '固定所选发布版本；参数由当前授权记录计算。',
            'Pins the selected revision; parameters come from the authorized record.',
          )
        }}</small>
        <small>{{
          targets
            .find((target) => target.revisionNId === link.target?.revisionNId)
            ?.parameters.filter((p) => p.source !== 'TrustedContext')
            .map(
              (p) =>
                `${p.name}${p.required ? ' *' : ''}${p.label && p.label !== p.name ? ' (' + p.label + ')' : ''}`,
            )
            .join(', ')
        }}</small>
        <TraceMappings
          :model-value="link.parameterMap ?? []"
          @update:model-value="(v) => (link.parameterMap = v)"
        />
      </template>
      <template v-else-if="link.kind === 'External'"
        ><el-select
          v-model="link.allowedOriginKey"
          filterable
          :aria-label="t('origin')"
          :placeholder="t('origin')"
          ><el-option
            v-for="source in sources?.external ?? []"
            :key="source.key"
            :value="source.key"
            :label="source.key"
        /></el-select>
        <small v-if="!externalSource(link.allowedOriginKey)?.embeddable">{{
          t('blockedFrame')
        }}</small>
        <small v-else>{{
          externalSource(link.allowedOriginKey)?.allowedParameters.join(', ')
        }}</small>
        <TraceMappings
          :model-value="link.parameterMap ?? []"
          @update:model-value="(v) => (link.parameterMap = v)"
      /></template>
      <template v-else-if="link.fileSource"
        ><el-select v-model="link.fileSource.mode" :aria-label="t('kind')"
          ><el-option
            value="Owned"
            :label="t('owned')"
            :disabled="!sources?.ownedAvailable" /><el-option
            value="Mes"
            :label="t('mes')" /></el-select
        ><template v-if="link.fileSource.mode === 'Mes'"
          ><el-select
            v-model="link.fileSource.sourceKey"
            filterable
            :aria-label="t('sourceKey')"
            :placeholder="t('sourceKey')"
            ><el-option
              v-for="source in sources?.mes ?? []"
              :key="source.key"
              :value="source.key"
              :label="`${source.key} · ${text.option(source.scanPolicy)}`"
              :disabled="!source.available"
          /></el-select>
          <small v-if="mesSource(link.fileSource.sourceKey)"
            >{{ text.option(mesSource(link.fileSource.sourceKey)?.scanPolicy ?? '') }} ·
            {{ mesSource(link.fileSource.sourceKey)?.allowedContentTypes.join(', ') }} ·
            {{ mesSource(link.fileSource.sourceKey)?.maxBytes }} {{ t('bytes') }}
            <span v-if="!mesSource(link.fileSource.sourceKey)?.available">{{
              t('blocked')
            }}</span></small
          >
          <label
            >{{ t('version')
            }}<BindingEditor
              :model-value="link.fileSource.version ?? rowBinding()"
              :contexts="traceContexts"
              @update:model-value="(v) => (link.fileSource!.version = v)" /></label></template
      ></template>
      <el-button :aria-label="t('remove')" @click="model = model.filter((_, index) => index !== i)"
        >−</el-button
      >
    </div>
    <el-button @click="addLink">{{ t('add') }}</el-button>
  </fieldset>
</template>
