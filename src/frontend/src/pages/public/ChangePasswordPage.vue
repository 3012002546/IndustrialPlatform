<script setup lang="ts">
/**
 * 首次登录/强制改密页(§29A.4):普通新用户首次登录只允许修改密码与注销;
 * 改密成功后服务端撤销全部会话,前端清理本地会话并回登录页重新登录。
 * 内置 admin(MustChangePassword=false)不受本门禁影响。
 */
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { ApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const CURRENT_PASSWORD_ID = 'ip-change-current-password'
const NEW_PASSWORD_ID = 'ip-change-new-password'
const CONFIRM_PASSWORD_ID = 'ip-change-confirm-password'

const authStore = useAuthStore()
const router = useRouter()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].changePassword)

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const currentPasswordVisible = ref(false)
const newPasswordVisible = ref(false)
const submitting = ref(false)
const error = ref<string | null>(null)

const currentPasswordTouched = ref(false)
const newPasswordTouched = ref(false)
const confirmPasswordTouched = ref(false)

const currentPasswordError = computed(
  () => currentPasswordTouched.value && currentPassword.value === '',
)
const newPasswordError = computed(() => {
  if (!newPasswordTouched.value || newPassword.value === '') return false
  return !meetsPolicy(newPassword.value)
})
const confirmPasswordError = computed(
  () => confirmPasswordTouched.value && confirmPassword.value !== newPassword.value,
)

/** 与后端 PasswordPolicy 一致的复杂度提示(§8):≥12 位,含大小写/数字/特殊字符。 */
function meetsPolicy(password: string): boolean {
  return (
    password.length >= 12 &&
    /[A-Z]/.test(password) &&
    /[a-z]/.test(password) &&
    /\d/.test(password) &&
    /[!@#$%^&*()\-_=+[\]{}|;:,.<>?/]/.test(password)
  )
}

const policyHint = computed(() => (newPasswordError.value ? copy.value.passwordPolicy : ''))

function toErrorMessage(errorValue: unknown): string {
  if (errorValue instanceof ApiError) return errorValue.message
  return DEFAULT_ERROR_MESSAGES.unknown
}

async function onSubmit(): Promise<void> {
  if (submitting.value) return
  currentPasswordTouched.value = true
  newPasswordTouched.value = true
  confirmPasswordTouched.value = true
  if (currentPasswordError.value || newPasswordError.value || confirmPasswordError.value) {
    return
  }
  error.value = null
  submitting.value = true
  try {
    await authStore.changePassword(currentPassword.value, newPassword.value)
    // 会话已被撤销,回登录页重新登录
    await router.push({ name: ROUTE_NAMES.login })
  } catch (changeError) {
    error.value = toErrorMessage(changeError)
  } finally {
    submitting.value = false
  }
}

async function onLogout(): Promise<void> {
  await authStore.logout()
  await router.push({ name: ROUTE_NAMES.login })
}
</script>

<template>
  <main class="change-password-page">
    <section class="change-password-card" aria-labelledby="change-password-title">
      <header class="change-password-card__header">
        <h1 id="change-password-title" class="change-password-card__title">{{ copy.title }}</h1>
        <p class="change-password-card__subtitle">
          {{ copy.subtitle }}
        </p>
      </header>

      <form class="change-password-card__form" novalidate @submit.prevent="onSubmit">
        <div class="change-password-card__field">
          <label class="change-password-card__label" :for="CURRENT_PASSWORD_ID">{{
            copy.currentPassword
          }}</label>
          <div class="change-password-card__password">
            <input
              :id="CURRENT_PASSWORD_ID"
              v-model="currentPassword"
              class="change-password-card__input"
              :type="currentPasswordVisible ? 'text' : 'password'"
              autocomplete="current-password"
              data-testid="change-current-password"
              :aria-invalid="currentPasswordError || undefined"
              @blur="currentPasswordTouched = true"
            />
            <button
              type="button"
              class="change-password-card__toggle"
              :aria-pressed="currentPasswordVisible"
              :aria-label="
                currentPasswordVisible ? copy.hideCurrentPassword : copy.showCurrentPassword
              "
              data-testid="change-current-toggle"
              @click="currentPasswordVisible = !currentPasswordVisible"
            >
              <svg
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                aria-hidden="true"
                focusable="false"
              >
                <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" stroke="currentColor" />
                <circle cx="12" cy="12" r="2.5" stroke="currentColor" />
              </svg>
            </button>
          </div>
          <p v-if="currentPasswordError" class="change-password-card__error" role="alert">
            {{ copy.currentPasswordRequired }}
          </p>
        </div>

        <div class="change-password-card__field">
          <label class="change-password-card__label" :for="NEW_PASSWORD_ID">{{
            copy.newPassword
          }}</label>
          <div class="change-password-card__password">
            <input
              :id="NEW_PASSWORD_ID"
              v-model="newPassword"
              class="change-password-card__input"
              :type="newPasswordVisible ? 'text' : 'password'"
              autocomplete="new-password"
              data-testid="change-new-password"
              :aria-invalid="newPasswordError || undefined"
              :aria-describedby="newPasswordError ? `${NEW_PASSWORD_ID}-error` : undefined"
              @blur="newPasswordTouched = true"
            />
            <button
              type="button"
              class="change-password-card__toggle"
              :aria-pressed="newPasswordVisible"
              :aria-label="newPasswordVisible ? copy.hideNewPassword : copy.showNewPassword"
              data-testid="change-new-toggle"
              @click="newPasswordVisible = !newPasswordVisible"
            >
              <svg
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                aria-hidden="true"
                focusable="false"
              >
                <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" stroke="currentColor" />
                <circle cx="12" cy="12" r="2.5" stroke="currentColor" />
              </svg>
            </button>
          </div>
          <p
            v-if="newPasswordError"
            :id="`${NEW_PASSWORD_ID}-error`"
            class="change-password-card__error"
            role="alert"
          >
            {{ policyHint }}
          </p>
        </div>

        <div class="change-password-card__field">
          <label class="change-password-card__label" :for="CONFIRM_PASSWORD_ID">{{
            copy.confirmPassword
          }}</label>
          <input
            :id="CONFIRM_PASSWORD_ID"
            v-model="confirmPassword"
            class="change-password-card__input"
            type="password"
            autocomplete="new-password"
            data-testid="change-confirm-password"
            :aria-invalid="confirmPasswordError || undefined"
            @blur="confirmPasswordTouched = true"
          />
          <p v-if="confirmPasswordError" class="change-password-card__error" role="alert">
            {{ copy.passwordsMismatch }}
          </p>
        </div>

        <p
          v-if="error"
          class="change-password-card__error change-password-card__error--submit"
          role="alert"
        >
          {{ error }}
        </p>

        <div class="change-password-card__actions">
          <button
            type="submit"
            class="change-password-card__submit"
            data-testid="change-submit"
            :disabled="submitting"
          >
            {{ submitting ? copy.submitting : copy.submit }}
          </button>
          <button
            type="button"
            class="change-password-card__logout"
            data-testid="change-logout"
            @click="onLogout"
          >
            {{ copy.logout }}
          </button>
        </div>
      </form>
    </section>
  </main>
</template>

<style scoped>
.change-password-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f5f7fa;
  padding: 24px;
}

.change-password-card {
  width: 100%;
  max-width: 420px;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 8px 24px rgb(0 0 0 / 8%);
  padding: 32px;
}

.change-password-card__header {
  margin-bottom: 24px;
}

.change-password-card__title {
  margin: 0 0 8px;
  font-size: 22px;
}

.change-password-card__subtitle {
  margin: 0;
  color: #606266;
  font-size: 13px;
  line-height: 1.5;
}

.change-password-card__field {
  margin-bottom: 16px;
}

.change-password-card__label {
  display: block;
  margin-bottom: 6px;
  font-size: 13px;
  color: #303133;
}

.change-password-card__input {
  width: 100%;
  box-sizing: border-box;
  padding: 9px 12px;
  border: 1px solid #dcdfe6;
  border-radius: 6px;
  font-size: 14px;
  outline: none;
}

.change-password-card__input:focus {
  border-color: #409eff;
}

.change-password-card__password {
  display: flex;
  align-items: center;
}

.change-password-card__password .change-password-card__input {
  flex: 1;
}

.change-password-card__toggle {
  margin-left: -34px;
  background: transparent;
  border: 0;
  cursor: pointer;
  padding: 4px;
  color: #909399;
}

.change-password-card__error {
  margin: 6px 0 0;
  color: #f56c6c;
  font-size: 12px;
}

.change-password-card__error--submit {
  margin: 12px 0 0;
}

.change-password-card__actions {
  display: flex;
  gap: 12px;
  margin-top: 24px;
}

.change-password-card__submit {
  flex: 1;
  padding: 10px 0;
  background: #409eff;
  color: #fff;
  border: 0;
  border-radius: 6px;
  font-size: 14px;
  cursor: pointer;
}

.change-password-card__submit:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.change-password-card__logout {
  padding: 10px 16px;
  background: transparent;
  color: #606266;
  border: 1px solid #dcdfe6;
  border-radius: 6px;
  font-size: 14px;
  cursor: pointer;
}
</style>
