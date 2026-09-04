<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import { localeMessages } from '@/localization/i18n'
import { PC_DENSITIES, THEME_MODES, THEME_PALETTES } from '@/theme'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataThemePolicyView,
  },
)
const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'themes'))
const themeCopy = computed(() => localeMessages[localization.locale].common.theme)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)
const formError = ref('')
const draft = reactive({
  allowedPalettes: [] as string[],
  allowedModes: [] as string[],
  allowedPcDensities: [] as string[],
  defaultPalette: '',
  defaultMode: '',
  defaultPcDensity: '',
})
function sync(): void {
  formError.value = ''
  if (store.themePolicy)
    Object.assign(draft, {
      allowedPalettes: [...store.themePolicy.allowedPalettes],
      allowedModes: [...store.themePolicy.allowedModes],
      allowedPcDensities: [...store.themePolicy.allowedPcDensities],
      defaultPalette: store.themePolicy.defaultPalette,
      defaultMode: store.themePolicy.defaultMode,
      defaultPcDensity: store.themePolicy.defaultPcDensity,
    })
}
async function reload(): Promise<void> {
  await store.load('themes')
  sync()
}
function paletteLabel(value: string): string {
  const key =
    value === 'industrial-cyan'
      ? 'industrialCyan'
      : value === 'technology-blue'
        ? 'technologyBlue'
        : 'neutralGray'
  return themeCopy.value.palettes[key]
}
function modeLabel(value: string): string {
  return themeCopy.value.modes[value as keyof typeof themeCopy.value.modes] ?? value
}
function densityLabel(value: string): string {
  return themeCopy.value.densities[value as keyof typeof themeCopy.value.densities] ?? value
}
async function save(): Promise<void> {
  formError.value = ''
  if (
    !draft.allowedPalettes.length ||
    !draft.allowedModes.length ||
    !draft.allowedPcDensities.length
  ) {
    formError.value = copy.value.invalid
    return
  }
  if (
    !draft.allowedPalettes.includes(draft.defaultPalette) ||
    !draft.allowedModes.includes(draft.defaultMode) ||
    !draft.allowedPcDensities.includes(draft.defaultPcDensity)
  ) {
    formError.value = copy.value.invalid
    return
  }
  const expectedPolicyRevision = store.themePolicy?.policyRevision ?? 0
  await store.updateThemeDefaults({
    expectedPolicyRevision,
    allowedPalettes: [...draft.allowedPalettes],
    allowedModes: [...draft.allowedModes],
    allowedPcDensities: [...draft.allowedPcDensities],
    defaultPalette: draft.defaultPalette,
    defaultMode: draft.defaultMode,
    defaultPcDensity: draft.defaultPcDensity,
  })
}
watch(() => store.themePolicy, sync, { immediate: true })
</script>
<template>
  <SystemDataAdminFrame
    kind="themes"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
    ><template #toolbar
      ><el-button type="default" :loading="store.loading" @click="reload">{{
        copy.reload
      }}</el-button
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataThemePolicyManage"
        ><el-button type="primary" :disabled="store.loading" @click="save">
          {{ copy.save }}
        </el-button></PermissionGate
      ></template
    >
    <div class="systemdata-theme-editor">
      <h2>{{ copy.allowedPalettes }}</h2>
      <p
        v-if="store.themePolicy && store.themePolicy.configured === false"
        class="theme-unconfigured"
      >
        {{ copy.unconfigured }}
      </p>
      <p v-if="formError" role="alert">{{ formError }}</p>
      <el-form label-width="120px"
        ><el-form-item :label="copy.allowedPalettes"
          ><el-checkbox-group v-model="draft.allowedPalettes"
            ><el-checkbox
              v-for="value in THEME_PALETTES"
              :key="value"
              :label="value">{{ paletteLabel(value) }}</el-checkbox></el-checkbox-group></el-form-item
        ><el-form-item :label="copy.allowedModes"
          ><el-checkbox-group v-model="draft.allowedModes"
            ><el-checkbox
              v-for="value in THEME_MODES"
              :key="value"
              :label="value">{{ modeLabel(value) }}</el-checkbox></el-checkbox-group></el-form-item
        ><el-form-item :label="copy.allowedDensities"
          ><el-checkbox-group v-model="draft.allowedPcDensities"
            ><el-checkbox
              v-for="value in PC_DENSITIES"
              :key="value"
              :label="value">{{ densityLabel(value) }}</el-checkbox></el-checkbox-group></el-form-item
        ><el-form-item :label="copy.defaultPalette"
          ><el-select v-model="draft.defaultPalette"
            ><el-option
              v-for="value in draft.allowedPalettes"
              :key="value"
              :label="paletteLabel(value)"
              :value="value" /></el-select></el-form-item
        ><el-form-item :label="copy.defaultMode"
          ><el-select v-model="draft.defaultMode"
            ><el-option
              v-for="value in draft.allowedModes"
              :key="value"
              :label="modeLabel(value)"
              :value="value" /></el-select></el-form-item
        ><el-form-item :label="copy.defaultDensity"
          ><el-select v-model="draft.defaultPcDensity"
            ><el-option
              v-for="value in draft.allowedPcDensities"
              :key="value"
              :label="densityLabel(value)"
              :value="value" /></el-select></el-form-item
      ></el-form>
      <div
        class="systemdata-theme-preview"
        :data-palette="draft.defaultPalette"
        :data-mode="draft.defaultMode"
        :data-density="draft.defaultPcDensity"
      >
        <strong>{{ copy.previewTitle || copy.preview }}</strong>
        <span
          >{{ paletteLabel(draft.defaultPalette) }} · {{ modeLabel(draft.defaultMode) }} ·
          {{ densityLabel(draft.defaultPcDensity) }}</span
        >
        <div class="systemdata-theme-preview__sample">Aa · {{ copy.previewSample || copy.save }}</div>
      </div>
    </div></SystemDataAdminFrame
  >
</template>

<style scoped>
.systemdata-theme-editor {
  display: grid;
  max-width: 960px;
  min-width: 0;
  gap: var(--ip-space-4);
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.systemdata-theme-editor h2 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-lg);
  line-height: var(--ip-line-height-tight);
}

.systemdata-theme-editor p {
  margin: 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.systemdata-theme-editor p[role='alert'] {
  padding: var(--ip-space-3);
  color: var(--ip-color-danger);
  background: var(--ip-color-danger-bg);
  border: 1px solid var(--ip-color-danger);
  border-radius: var(--ip-radius-md);
}
.systemdata-theme-preview {
  display: grid;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}
.systemdata-theme-preview__sample {
  padding: var(--ip-space-3);
  color: #fff;
  background: #147d8b;
  border-radius: var(--ip-radius-sm);
  font-size: var(--ip-font-size-lg);
}
.systemdata-theme-preview[data-palette='technology-blue'] .systemdata-theme-preview__sample {
  background: #2457a6;
}
.systemdata-theme-preview[data-palette='neutral-gray'] .systemdata-theme-preview__sample {
  background: #5b6470;
}
.systemdata-theme-preview[data-mode='dark'] {
  background: #202833;
  color: #f4f6f8;
}
.systemdata-theme-preview[data-density='compact'] .systemdata-theme-preview__sample {
  padding-block: var(--ip-space-2);
}

.systemdata-theme-editor :deep(.el-form) {
  width: 100%;
}

.systemdata-theme-editor :deep(.el-checkbox-group) {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
}

.systemdata-theme-editor :deep(.el-checkbox.is-checked .el-checkbox__label) {
  color: var(--ip-color-text-primary);
}

.systemdata-theme-editor :deep(.el-form-item__label) {
  width: 160px !important;
  flex: 0 0 160px;
  line-height: var(--ip-line-height-normal);
}

.systemdata-theme-editor :deep(.el-select) {
  width: min(100%, 360px);
}
</style>
