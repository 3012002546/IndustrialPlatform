<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const props = defineProps<{
  modelValue: boolean
  actor: string
  actionLabel: string
  scopeSummary: string
  busy?: boolean
  error?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  confirm: [password: string]
  cancel: []
}>()

const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration)
const password = ref('')

watch(
  () => props.modelValue,
  (open) => {
    if (open) password.value = ''
  },
)

watch(
  () => props.error,
  (error) => {
    if (error) password.value = ''
  },
)

function submit(): void {
  if (props.busy || password.value.length === 0) return
  emit('confirm', password.value)
}

function cancel(): void {
  if (props.busy) return
  password.value = ''
  emit('cancel')
}

function onModelValueChange(open: boolean): void {
  if (open) emit('update:modelValue', true)
  else cancel()
}
</script>

<template>
  <AppFormDrawer
    :model-value="modelValue"
    :title="copy.stepUp"
    size="narrow"
    :busy="busy"
    :allow-mode-switch="false"
    @update:model-value="onModelValueChange"
    @cancel="cancel"
  >
    <div class="collaboration-step-up" data-testid="collaboration-step-up-drawer">
      <p class="collaboration-step-up__notice">{{ copy.passwordPrompt }}</p>
      <dl class="collaboration-step-up__context">
        <div>
          <dt>{{ copy.stepUpActor }}</dt>
          <dd>{{ actor }}</dd>
        </div>
        <div>
          <dt>{{ copy.stepUpAction }}</dt>
          <dd>{{ actionLabel }}</dd>
        </div>
        <div>
          <dt>{{ copy.stepUpScope }}</dt>
          <dd>{{ scopeSummary }}</dd>
        </div>
      </dl>
      <label class="collaboration-step-up__password">
        <span>{{ copy.password }}</span>
        <el-input
          v-model="password"
          data-testid="step-up-password"
          type="password"
          autocomplete="current-password"
          :disabled="busy"
          show-password
          @keyup.enter="submit"
        />
      </label>
      <p v-if="error" class="collaboration-step-up__error" role="alert">{{ error }}</p>
    </div>
    <template #footer>
      <el-button :disabled="busy" @click="cancel">
        {{ copy.cancel }}
      </el-button>
      <el-button
        type="primary"
        data-testid="step-up-submit"
        :disabled="busy || password.length === 0"
        :loading="busy"
        @click="submit"
      >
        {{ busy ? copy.saving : copy.confirm }}
      </el-button>
    </template>
  </AppFormDrawer>
</template>

<style scoped>
.collaboration-step-up {
  display: grid;
  gap: var(--ip-space-4);
}

.collaboration-step-up__notice,
.collaboration-step-up__error {
  margin: 0;
}

.collaboration-step-up__error {
  color: var(--ip-color-danger);
}

.collaboration-step-up__context {
  display: grid;
  gap: var(--ip-space-3);
  margin: 0;
}

.collaboration-step-up__context div {
  display: grid;
  gap: var(--ip-space-1);
}

.collaboration-step-up__context dt {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.collaboration-step-up__context dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.collaboration-step-up__password {
  display: grid;
  gap: var(--ip-space-2);
}
</style>
