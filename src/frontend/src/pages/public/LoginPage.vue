<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import { ApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const LOGIN_USERNAME_ID = 'ip-login-username'
const LOGIN_PASSWORD_ID = 'ip-login-password'

const authStore = useAuthStore()
const router = useRouter()
const route = useRoute()

const username = ref('')
const password = ref('')
const passwordVisible = ref(false)
const submitting = ref(false)

const usernameTouched = ref(false)
const passwordTouched = ref(false)
const loginError = ref<string | null>(null)

const usernameError = computed(() => usernameTouched.value && username.value.trim() === '')
const passwordError = computed(() => passwordTouched.value && password.value === '')

/** 安全 redirect(§15.1):仅接受站内相对路径,拒绝开放重定向与协议相对地址。 */
const redirectTarget = computed<string | { name: string }>(() => {
  const raw = route.query.redirect
  if (typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//')) {
    return raw
  }
  return { name: ROUTE_NAMES.root }
})

function focusFirstInvalid(): void {
  const target = usernameError.value ? LOGIN_USERNAME_ID : LOGIN_PASSWORD_ID
  document.getElementById(target)?.focus()
}

function toLoginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  return DEFAULT_ERROR_MESSAGES.unknown
}

async function onSubmit(): Promise<void> {
  if (submitting.value) return
  usernameTouched.value = true
  passwordTouched.value = true
  if (usernameError.value || passwordError.value) {
    focusFirstInvalid()
    return
  }
  loginError.value = null
  submitting.value = true
  try {
    await authStore.login({ username: username.value.trim(), password: password.value })
    await router.push(redirectTarget.value)
  } catch (error) {
    loginError.value = toLoginErrorMessage(error)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="login-page">
    <section class="login-card" aria-labelledby="login-title">
      <header class="login-card__header">
        <h1 id="login-title" class="login-card__title">登录</h1>
        <p class="login-card__subtitle">Industrial Platform · PC / PDA / Mobile</p>
      </header>

      <MockModeBanner class="login-card__banner" />

      <form class="login-card__form" novalidate @submit.prevent="onSubmit">
        <div class="login-card__field">
          <label class="login-card__label" :for="LOGIN_USERNAME_ID">用户名</label>
          <input
            :id="LOGIN_USERNAME_ID"
            v-model="username"
            class="login-card__input"
            type="text"
            autocomplete="username"
            data-testid="login-username"
            :aria-invalid="usernameError || undefined"
            :aria-describedby="usernameError ? `${LOGIN_USERNAME_ID}-error` : undefined"
            @blur="usernameTouched = true"
          />
          <p
            v-if="usernameError"
            :id="`${LOGIN_USERNAME_ID}-error`"
            class="login-card__error"
            role="alert"
          >
            请输入用户名
          </p>
        </div>

        <div class="login-card__field">
          <label class="login-card__label" :for="LOGIN_PASSWORD_ID">密码</label>
          <div class="login-card__password">
            <input
              :id="LOGIN_PASSWORD_ID"
              v-model="password"
              class="login-card__input"
              :type="passwordVisible ? 'text' : 'password'"
              autocomplete="current-password"
              data-testid="login-password"
              :aria-invalid="passwordError || undefined"
              :aria-describedby="passwordError ? `${LOGIN_PASSWORD_ID}-error` : undefined"
              @blur="passwordTouched = true"
            />
            <button
              type="button"
              class="login-card__toggle"
              :aria-pressed="passwordVisible"
              aria-label="显示密码"
              data-testid="password-toggle"
              @click="passwordVisible = !passwordVisible"
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
            v-if="passwordError"
            :id="`${LOGIN_PASSWORD_ID}-error`"
            class="login-card__error"
            role="alert"
          >
            请输入密码
          </p>
        </div>

        <p v-if="loginError" class="login-card__error login-card__error--login" role="alert">
          {{ loginError }}
        </p>

        <button
          type="submit"
          class="login-card__submit"
          data-testid="login-submit"
          :disabled="submitting"
        >
          {{ submitting ? '登录中…' : '登录' }}
        </button>
      </form>

      <p class="login-card__hint">演示账号:mock.admin / Mock@123456</p>
    </section>
  </main>
</template>

<style scoped>
.login-page {
  display: grid;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  place-items: center;
  background: var(--ip-color-bg-page);
}

.login-card {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
  width: 100%;
  max-width: 360px;
  padding: var(--ip-space-8) var(--ip-space-6);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-md);
}

.login-card__header {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.login-card__title {
  margin: 0;
  font-size: var(--ip-font-size-2xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
  color: var(--ip-color-text-primary);
}

.login-card__subtitle {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.login-card__banner {
  align-self: flex-start;
}

.login-card__form {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}

.login-card__field {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.login-card__label {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-primary);
}

.login-card__input {
  box-sizing: border-box;
  width: 100%;
  height: 40px;
  padding: 0 var(--ip-space-3);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.login-card__input[aria-invalid='true'] {
  border-color: var(--ip-color-danger);
}

.login-card__password {
  position: relative;
}

.login-card__password .login-card__input {
  padding-right: var(--ip-space-10);
}

.login-card__toggle {
  position: absolute;
  top: 50%;
  right: var(--ip-space-2);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  transform: translateY(-50%);
  cursor: pointer;
}

.login-card__toggle:hover {
  color: var(--ip-color-text-primary);
}

.login-card__error {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-danger);
}

.login-card__error--login {
  padding: var(--ip-space-2) var(--ip-space-3);
  background: var(--ip-color-danger-bg);
  border-radius: var(--ip-radius-md);
}

.login-card__submit {
  box-sizing: border-box;
  height: 40px;
  border: 0;
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-primary);
  color: #fff;
  font-size: var(--ip-font-size-md);
  font-weight: 500;
  cursor: pointer;
}

.login-card__submit:hover:not(:disabled) {
  background: var(--ip-color-primary-hover);
}

.login-card__submit:disabled {
  background: var(--ip-color-text-disabled);
  cursor: not-allowed;
}

.login-card__hint {
  margin: 0;
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}
</style>
