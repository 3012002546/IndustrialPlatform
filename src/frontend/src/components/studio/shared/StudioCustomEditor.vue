<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Json } from '@/api/studio'
import BindingEditor from './BindingEditor.vue'
import type { Binding } from './model'
import { useStudioCopy } from './copy'
defineProps<{ allowedOrigins: string[] }>()
const document = defineModel<Json>('document', { required: true })
const t = useStudioCopy(),
  tab = ref('html')
const formFields = computed({
  get: () => ((document.value.customHtml?.formFields ?? []) as string[]).join(', '),
  set: (value: string) => {
    document.value.customHtml.formFields = value
      .split(',')
      .map((v) => v.trim())
      .filter(Boolean)
  },
})
</script>
<template>
  <div class="studio-custom-editor">
    <template v-if="document.mode === 'Html' && document.customHtml">
      <el-tabs v-model="tab"
        ><el-tab-pane label="HTML" name="html" /><el-tab-pane label="CSS" name="css" /><el-tab-pane
          label="JavaScript"
          name="js"
      /></el-tabs>
      <el-input
        v-model="document.customHtml[tab]"
        type="textarea"
        :rows="23"
        :aria-label="tab"
        spellcheck="false"
      />
      <el-form-item :label="t('customFormFields')"
        ><el-input v-model="formFields" placeholder="field1, field2"
      /></el-form-item>
    </template>
    <el-form v-else-if="document.embeddedPage" label-position="top">
      <el-alert :title="t('embedLoadHelp')" type="info" :closable="false" />
      <el-form-item label="URL" required
        ><el-input v-model="document.embeddedPage.url" placeholder="https://" :maxlength="4096"
      /></el-form-item>
      <p>{{ t('allowedOrigins') }}：{{ allowedOrigins.join(', ') || t('none') }}</p>
      <h3>{{ t('parameters') }}</h3>
      <div
        v-for="(map, index) in document.embeddedPage.parameterMap as Json[]"
        :key="index"
        class="studio-binding"
      >
        <el-input v-model="map.target" :placeholder="t('target')" />
        <BindingEditor
          v-model="map.source as unknown as Binding"
          :instances="[]"
          :contexts="['PageParam', 'Constant']"
        />
        <el-button
          type="danger"
          link
          @click="document.embeddedPage.parameterMap.splice(index, 1)"
          >{{ t('remove') }}</el-button
        >
      </div>
      <el-button
        @click="
          document.embeddedPage.parameterMap.push({
            target: '',
            source: { from: 'PageParam', path: [], nullPolicy: 'Error' },
          })
        "
        >{{ t('add') }}</el-button
      >
    </el-form>
  </div>
</template>
<style scoped>
.studio-custom-editor {
  padding: 20px;
  width: 100%;
  box-sizing: border-box;
}
.studio-custom-editor :deep(textarea) {
  font-family: Consolas, monospace;
  font-size: 14px;
  line-height: 1.65;
  tab-size: 2;
}
</style>
