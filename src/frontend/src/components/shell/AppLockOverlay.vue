<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'

import { ApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import { useLockStore } from '@/stores/lockStore'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const lockStore = useLockStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.copy)
const password = ref('')
const error = ref<string | null>(null)
const passwordInput = ref<HTMLInputElement | null>(null)
const submitting = ref(false)

function focusPassword(): void {
  void nextTick(() => passwordInput.value?.focus())
}

watch(
  () => lockStore.isLocked,
  (locked) => {
    if (locked) focusPassword()
    else password.value = ''
  },
  { immediate: true },
)

function messageFor(errorValue: unknown): string {
  return errorValue instanceof ApiError ? errorValue.message : DEFAULT_ERROR_MESSAGES.unknown
}

async function submit(): Promise<void> {
  if (submitting.value || password.value.length === 0) return
  submitting.value = true
  error.value = null
  try {
    await lockStore.unlock(password.value)
  } catch (errorValue) {
    error.value = messageFor(errorValue)
    password.value = ''
    focusPassword()
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div
    v-if="lockStore.isLocked"
    class="app-lock-overlay"
    role="dialog"
    aria-modal="true"
    aria-labelledby="app-lock-title"
    data-testid="app-lock-overlay"
    @keydown.esc.prevent
  >
    <form class="app-lock-card" @submit.prevent="submit">
      <h1 id="app-lock-title">{{ copy.workspaceLocked }}</h1>
      <p class="app-lock-card__user">
        {{ lockStore.lockedUser?.displayName }} · {{ lockStore.lockedUser?.username }}
      </p>
      <label for="app-lock-password">{{ copy.currentPassword }}</label>
      <input
        id="app-lock-password"
        ref="passwordInput"
        v-model="password"
        type="password"
        autocomplete="current-password"
        data-testid="app-lock-password"
        :disabled="submitting"
      />
      <p v-if="error" class="app-lock-card__error" role="alert">{{ error }}</p>
      <button
        type="submit"
        :disabled="submitting || password.length === 0"
        data-testid="app-lock-submit"
      >
        {{ submitting ? copy.unlocking : copy.unlock }}
      </button>
    </form>
  </div>
</template>

<style scoped>
.app-lock-overlay {
  position: fixed;
  z-index: 2000;
  inset: 0;
  display: grid;
  place-items: center;
  background: rgb(15 23 42 / 0.72);
  pointer-events: all;
}

.app-lock-card {
  display: flex;
  width: min(360px, calc(100vw - 32px));
  flex-direction: column;
  gap: var(--ip-space-3);
  padding: var(--ip-space-6);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-lg);
}

.app-lock-card h1 {
  margin: 0;
  font-size: var(--ip-font-size-xl);
}
.app-lock-card__user {
  margin: 0;
  color: var(--ip-color-text-secondary);
}
.app-lock-card input {
  height: 36px;
  padding: 0 var(--ip-space-3);
  color: inherit;
  background: transparent;
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
}
.app-lock-card button {
  min-height: 36px;
  color: #fff;
  background: var(--ip-color-primary);
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}
.app-lock-card button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.app-lock-card__error {
  margin: 0;
  color: var(--ip-color-danger);
}
</style>
