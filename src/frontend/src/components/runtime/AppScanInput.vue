<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { systemDataPageCopy } from '@/localization/systemData'
import { usePlatformLocale } from '@/localization/localeContext'
import {
  getScannerRuntime,
  type ScanResultV1,
  type ScannerError,
  type ScannerSession,
  type ScannerSource,
} from '@/platform/runtime/scannerRuntime'

const props = withDefaults(
  defineProps<{
    title?: string
    modelValue?: string
    mode?: 'broadcast' | 'input'
    inputDisabled?: boolean
    showCamera?: boolean
    ownerPageId: string
  }>(),
  {
    title: '扫描条码',
    modelValue: '',
    mode: 'input',
    inputDisabled: false,
    showCamera: true,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  result: [result: ScanResultV1]
  error: [error: ScannerError]
}>()

const locale = usePlatformLocale()
const copy = computed(() => systemDataPageCopy(locale.value, 'scannerTest'))
const runtime = getScannerRuntime()
const inputElement = ref<HTMLInputElement | null>(null)
const cameraFileInput = ref<HTMLInputElement | null>(null)
const inputValue = ref(props.modelValue)
const sessionNId = ref('')
const errorCode = ref<string | null>(null)
const composing = ref(false)
const cameraInFlight = ref(false)
let cameraSessionNId: string | null = null
const source = computed<ScannerSource>(() => (props.mode === 'broadcast' ? 'broadcast' : 'keyboard'))
let session = acquireSession()
sessionNId.value = session.sessionNId
let unsubscribe = subscribeToSession(session)

const cameraVisible = computed(() => props.mode === 'input' && props.showCamera)
const cameraDisabled = computed(() => !runtime.cameraAvailable)
const errorMessage = computed(() => {
  if (errorCode.value === null) return ''
  if (errorCode.value === 'RUNTIME_UNSUPPORTED') return copy.value.cameraUnsupported
  if (errorCode.value === 'RUNTIME_INVALID_ARGUMENT') return copy.value.invalidValue
  return copy.value.cameraFailed
})

function acquireSession(): ScannerSession {
  return runtime.acquire({ ownerPageId: props.ownerPageId, source: source.value })
}

function subscribeToSession(nextSession: ScannerSession): () => void {
  return runtime.subscribe(nextSession.sessionNId, (result) => {
    inputValue.value = result.value
    emit('update:modelValue', result.value)
    emit('result', result)
    errorCode.value = null
  })
}

function publishKeyboard(): void {
  if (props.inputDisabled || composing.value) return
  const value = inputValue.value
  if (!runtime.publish({
    sessionNId: session.sessionNId,
    subjectEpoch: session.subjectEpoch,
    source: 'keyboard',
    value,
  })) {
    if (value.length === 0 || value.length > 4096) emitError('RUNTIME_INVALID_ARGUMENT', false)
  }
}

function onInput(event: Event): void {
  const value = (event.target as HTMLInputElement).value.slice(0, 4096)
  inputValue.value = value
  emit('update:modelValue', value)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.isComposing || composing.value) return
  if (event.key === 'Enter') {
    event.preventDefault()
    publishKeyboard()
  } else if (event.key === 'Tab') {
    publishKeyboard()
  }
}

function emitError(code: string, retryable: boolean): void {
  errorCode.value = code
  emit('error', { code, retryable })
}

async function openCamera(): Promise<void> {
  errorCode.value = null
  if (cameraDisabled.value) {
    emitError('RUNTIME_UNSUPPORTED', false)
    return
  }
  if (cameraInFlight.value) return
  cameraInFlight.value = true
  const previousSubjectEpoch = session.subjectEpoch
  unsubscribe()
  session = await runtime.reset(session.sessionNId, {
    ownerPageId: props.ownerPageId,
    source: 'camera',
    subjectEpoch: previousSubjectEpoch,
  })
  sessionNId.value = session.sessionNId
  unsubscribe = subscribeToSession(session)
  cameraSessionNId = session.sessionNId

  if (runtime.nativeCameraAvailable) {
    try {
      await runtime.scanNativeOnce(session)
    } finally {
      await restoreInputSession()
    }
    return
  }
  if (cameraFileInput.value !== null) cameraFileInput.value.value = ''
  cameraFileInput.value?.click()
}

async function onCameraFileChange(event: Event): Promise<void> {
  const file = (event.target as HTMLInputElement).files?.[0]
  ;(event.target as HTMLInputElement).value = ''
  if (file === undefined) {
    await restoreInputSession()
    return
  }
  try {
    if (!(await runtime.scanImage(session, file))) emitError('RUNTIME_INVALID_ARGUMENT', true)
  } catch {
    emitError('RUNTIME_UNSUPPORTED', false)
  } finally {
    await restoreInputSession()
  }
}

async function restoreInputSession(): Promise<void> {
  const activeCameraSessionNId = cameraSessionNId
  cameraSessionNId = null
  cameraInFlight.value = false
  if (activeCameraSessionNId === null || session.sessionNId !== activeCameraSessionNId) return
  const subjectEpoch = session.subjectEpoch
  unsubscribe()
  session = await runtime.reset(session.sessionNId, {
    ownerPageId: props.ownerPageId,
    source: source.value,
    subjectEpoch,
  })
  sessionNId.value = session.sessionNId
  unsubscribe = subscribeToSession(session)
}

async function clear(): Promise<void> {
  inputValue.value = ''
  emit('update:modelValue', '')
  cameraSessionNId = null
  cameraInFlight.value = false
  unsubscribe()
  session = await runtime.reset(session.sessionNId, {
    ownerPageId: props.ownerPageId,
    source: source.value,
    subjectEpoch: session.subjectEpoch,
  })
  sessionNId.value = session.sessionNId
  unsubscribe = subscribeToSession(session)
  errorCode.value = null
  inputElement.value?.focus()
}

function onCompositionStart(): void {
  composing.value = true
}

function onCompositionEnd(): void {
  composing.value = false
}

watch(
  () => props.modelValue,
  (value) => {
    if (value !== inputValue.value) inputValue.value = value
  },
)

watch(
  () => [props.ownerPageId, source.value] as const,
  async () => {
    cameraSessionNId = null
    cameraInFlight.value = false
    unsubscribe()
    session = await runtime.reset(session.sessionNId, {
      ownerPageId: props.ownerPageId,
      source: source.value,
      subjectEpoch: session.subjectEpoch,
    })
    sessionNId.value = session.sessionNId
    unsubscribe = subscribeToSession(session)
  },
)

onBeforeUnmount(() => {
  unsubscribe()
  runtime.release(session.sessionNId)
})

defineExpose({ clear })
</script>

<template>
  <div class="app-scan-input" data-testid="app-scan-input">
    <label class="app-scan-input__title" :for="`scan-input-${sessionNId}`">{{ title }}</label>
    <div class="app-scan-input__control">
      <input
        :id="`scan-input-${sessionNId}`"
        ref="inputElement"
        :value="inputValue"
        :disabled="inputDisabled"
        :placeholder="copy.placeholder"
        :data-session-id="sessionNId"
        data-testid="scan-input"
        maxlength="4096"
        autocomplete="off"
        @input="onInput"
        @keydown="onKeydown"
        @compositionstart="onCompositionStart"
        @compositionend="onCompositionEnd"
      />
      <button
        v-if="cameraVisible"
        type="button"
        class="app-scan-input__camera"
        data-testid="scan-camera"
        :disabled="cameraDisabled"
        :aria-label="copy.camera"
        :title="cameraDisabled ? copy.cameraUnsupported : copy.camera"
        @click="openCamera"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <path d="M4 7.5h3l1.3-2h7.4l1.3 2h3A1.5 1.5 0 0 1 21.5 9v9A1.5 1.5 0 0 1 20 19.5H4A1.5 1.5 0 0 1 2.5 18V9A1.5 1.5 0 0 1 4 7.5Z" stroke="currentColor" stroke-width="1.7" />
          <circle cx="12" cy="13.5" r="3.5" stroke="currentColor" stroke-width="1.7" />
        </svg>
      </button>
      <button type="button" data-testid="scan-clear" class="app-scan-input__clear" @click="clear">
        {{ copy.clearInput }}
      </button>
      <input
        ref="cameraFileInput"
        class="app-scan-input__file"
        type="file"
        accept="image/*"
        capture="environment"
        tabindex="-1"
        aria-hidden="true"
        @change="onCameraFileChange"
        @cancel="restoreInputSession"
      />
    </div>
    <p v-if="errorCode !== null" class="app-scan-input__error" role="alert">{{ errorMessage }}</p>
  </div>
</template>

<style scoped>
.app-scan-input {
  display: grid;
  grid-template-columns: minmax(88px, 128px) minmax(0, 1fr);
  align-items: start;
  gap: var(--ip-space-3);
}

.app-scan-input__title {
  min-height: var(--ip-touch-min-size);
  padding-top: var(--ip-space-3);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  font-weight: 600;
}

.app-scan-input__control {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: var(--ip-space-2);
}

.app-scan-input__control input:not([type='file']) {
  flex: 1 1 auto;
  min-width: 0;
  min-height: var(--ip-touch-min-size);
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  font: inherit;
}

.app-scan-input__control input:not([type='file']):focus {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.app-scan-input__camera,
.app-scan-input__clear {
  min-width: var(--ip-touch-min-size);
  min-height: var(--ip-touch-min-size);
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.app-scan-input__camera:disabled {
  color: var(--ip-color-text-disabled);
  cursor: not-allowed;
}

.app-scan-input__clear {
  white-space: nowrap;
}

.app-scan-input__file {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  opacity: 0;
  pointer-events: none;
}

.app-scan-input__error {
  grid-column: 2;
  margin: calc(var(--ip-space-2) * -1) 0 0;
  color: var(--ip-color-danger);
  font-size: var(--ip-font-size-sm);
}

@media (max-width: 600px) {
  .app-scan-input {
    grid-template-columns: 1fr;
    gap: var(--ip-space-2);
  }

  .app-scan-input__title {
    min-height: auto;
    padding-top: 0;
  }

  .app-scan-input__error {
    grid-column: 1;
  }
}
</style>
