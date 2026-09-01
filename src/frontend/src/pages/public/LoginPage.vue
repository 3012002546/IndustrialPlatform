<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  CircleCheckFilled,
  Close,
  Hide,
  OfficeBuilding,
  Stamp,
  Switch,
  User,
  View,
} from '@element-plus/icons-vue'

import { getAuthGateway } from '@/auth/gateway'
import type { BootstrapStatus } from '@/auth/types'
import MockModeBanner from '@/components/base/MockModeBanner.vue'
import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import LocaleControl from '@/components/localization/LocaleControl.vue'
import { ApiError } from '@/api/errors'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const LOGIN_USERNAME_ID = 'ip-login-username'
const LOGIN_PASSWORD_ID = 'ip-login-password'
const LOGIN_FORM_WIDTH = 430

const authStore = useAuthStore()
const router = useRouter()
const route = useRoute()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].login)

const username = ref('')
const password = ref('')
const passwordVisible = ref(false)
const loginMethodMenuOpen = ref(false)
const loginMethodToggle = ref<HTMLButtonElement | null>(null)
const loginMethodPanelClose = ref<HTMLButtonElement | null>(null)
const loginCardStyle = { '--login-form-width': `${LOGIN_FORM_WIDTH}px` }
const submitting = ref(false)

const usernameTouched = ref(false)
const passwordTouched = ref(false)
const loginError = ref<string | null>(null)

/** §29A.7:HTTP 模式加载时读取 bootstrap 状态,未完成时展示诊断并禁止登录。 */
const bootstrapStatus = ref<BootstrapStatus | null>(null)

const usernameError = computed(() => usernameTouched.value && username.value.trim() === '')
const passwordError = computed(() => passwordTouched.value && password.value === '')

/** bootstrap 未完成(初始化未完成或 admin 异常)时禁止登录。 */
const bootstrapBlocked = computed(
  () => bootstrapStatus.value !== null && bootstrapStatus.value.state !== 'Ready',
)

onMounted(async () => {
  if (loadRuntimeConfig().authMode !== 'http') return
  try {
    bootstrapStatus.value = await getAuthGateway().getBootstrapStatus()
  } catch {
    // 状态端点不可达时按 Ready 降级,不阻塞登录(由登录失败错误兜底)。
    bootstrapStatus.value = null
  }
})

/** 安全 redirect(§15.1):仅接受站内相对路径,拒绝开放重定向与协议相对地址。 */
const redirectTarget = computed<string | { name: string }>(() => {
  const raw = route.query.redirect
  if (typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//')) {
    return raw
  }
  return { name: ROUTE_NAMES.root }
})

/** SSO 仅在真实认证模式下可用(企业登录源依赖 Identity 网关,§26)。 */
const isHttpAuth = computed(() => loadRuntimeConfig().authMode === 'http')

/** 传给企业登录页的 redirect:与 redirectTarget 同规则,仅站内相对路径。 */
const ssoRedirectQuery = computed<{ redirect?: string }>(() => {
  const raw = route.query.redirect
  if (typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//')) {
    return { redirect: raw }
  }
  return {}
})

function focusFirstInvalid(): void {
  const target = usernameError.value ? LOGIN_USERNAME_ID : LOGIN_PASSWORD_ID
  document.getElementById(target)?.focus()
}

function closeLoginMethodMenu(restoreFocus = false): void {
  loginMethodMenuOpen.value = false
  if (restoreFocus) loginMethodToggle.value?.focus()
}

function toggleLoginMethodMenu(): void {
  loginMethodMenuOpen.value = !loginMethodMenuOpen.value
  if (loginMethodMenuOpen.value) {
    void nextTick(() => loginMethodPanelClose.value?.focus())
  }
}

function toLoginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  return localeMessages[locale.value].common.errors.unknown
}

async function onSubmit(): Promise<void> {
  if (submitting.value || bootstrapBlocked.value) return
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
  <main class="login-page" @click="closeLoginMethodMenu()">
    <section
      class="login-card"
      :class="{ 'login-card--methods-open': loginMethodMenuOpen }"
      :style="loginCardStyle"
      aria-labelledby="login-title"
    >
      <div class="login-card__form-pane">
        <header class="login-card__header">
          <div class="login-card__header-row" @click.stop>
            <PlatformBrand variant="light" />
            <div class="login-card__locale">
              <LocaleControl />
            </div>
          </div>
          <h1 id="login-title" class="login-card__title">{{ copy.title }}</h1>
          <p class="login-card__subtitle">{{ copy.subtitle }}</p>
        </header>

        <MockModeBanner class="login-card__banner" :label="copy.mockMode" />

        <div
          v-if="bootstrapBlocked"
          class="login-card__bootstrap-pending"
          role="alert"
          data-testid="login-bootstrap-pending"
        >
          {{
            bootstrapStatus?.state === 'RecoveryRequired'
              ? copy.bootstrapRecoveryRequired
              : copy.bootstrapPending
          }}
        </div>

        <form class="login-card__form" novalidate @submit.prevent="onSubmit">
          <div class="login-card__field">
            <div class="login-card__username-area">
              <div class="login-card__label-row">
                <label class="login-card__label" :for="LOGIN_USERNAME_ID">{{ copy.username }}</label>
              </div>
              <input
                :id="LOGIN_USERNAME_ID"
                v-model="username"
                class="login-card__input"
                type="text"
                autocomplete="username"
                data-testid="login-username"
                :aria-label="copy.username"
                :aria-invalid="usernameError || undefined"
                :aria-describedby="usernameError ? `${LOGIN_USERNAME_ID}-error` : undefined"
                @blur="usernameTouched = true"
              />
            </div>
            <p
              :id="`${LOGIN_USERNAME_ID}-error`"
              class="login-card__error login-card__field-error"
              :class="{ 'login-card__field-error--visible': usernameError }"
              :aria-hidden="!usernameError"
              role="alert"
            >
              {{ usernameError ? copy.usernameRequired : '\u00a0' }}
            </p>
          </div>

          <div class="login-card__field">
            <label class="login-card__label" :for="LOGIN_PASSWORD_ID">{{ copy.password }}</label>
            <div class="login-card__password">
              <input
                :id="LOGIN_PASSWORD_ID"
                v-model="password"
                class="login-card__input"
                :type="passwordVisible ? 'text' : 'password'"
                autocomplete="current-password"
                data-testid="login-password"
                :aria-label="copy.password"
                :aria-invalid="passwordError || undefined"
                :aria-describedby="passwordError ? `${LOGIN_PASSWORD_ID}-error` : undefined"
                @blur="passwordTouched = true"
              />
              <button
                type="button"
                class="login-card__toggle"
                :aria-pressed="passwordVisible"
                :aria-label="passwordVisible ? copy.hidePassword : copy.showPassword"
                data-testid="password-toggle"
                @click="passwordVisible = !passwordVisible"
              >
                <View v-if="!passwordVisible" aria-hidden="true" />
                <Hide v-else aria-hidden="true" />
              </button>
            </div>
            <p
              :id="`${LOGIN_PASSWORD_ID}-error`"
              class="login-card__error login-card__field-error"
              :class="{ 'login-card__field-error--visible': passwordError }"
              :aria-hidden="!passwordError"
              role="alert"
            >
              {{ passwordError ? copy.passwordRequired : '\u00a0' }}
            </p>
          </div>

          <p v-if="loginError" class="login-card__error login-card__error--login" role="alert">
            {{ loginError }}
          </p>

          <button
            type="submit"
            class="login-card__submit"
            data-testid="login-submit"
            :disabled="submitting || bootstrapBlocked"
          >
            {{ submitting ? copy.submitting : copy.submit }}
          </button>
          <button
            ref="loginMethodToggle"
            type="button"
            class="login-card__method-toggle"
            :aria-label="copy.methodToggle"
            aria-haspopup="dialog"
            :aria-expanded="loginMethodMenuOpen"
            aria-controls="login-method-panel"
            data-testid="login-method-toggle"
            @click.stop="toggleLoginMethodMenu"
            @keydown.esc="closeLoginMethodMenu(true)"
          >
            <Switch aria-hidden="true" />
            <span>{{ copy.methodToggle }}</span>
          </button>
        </form>

        <p v-if="!isHttpAuth" class="login-card__hint">{{ copy.demoCredentials }}</p>
      </div>

      <Transition name="login-method-panel">
        <aside
          v-if="loginMethodMenuOpen"
          class="login-card__method-panel"
          role="dialog"
          aria-labelledby="login-method-panel-title"
          data-testid="login-method-panel"
          data-transition="login-method-panel"
          @click.stop
          @keydown.esc="closeLoginMethodMenu(true)"
        >
          <div class="login-card__method-panel-header">
            <h2 id="login-method-panel-title">{{ copy.methodPanelTitle }}</h2>
            <button
              type="button"
              class="login-card__method-panel-close"
              ref="loginMethodPanelClose"
              data-testid="login-method-panel-close"
              :aria-label="copy.methodPanelClose"
              :title="copy.methodPanelClose"
              @click="closeLoginMethodMenu(true)"
            >
              <Close aria-hidden="true" />
            </button>
          </div>

          <div class="login-card__method-list" role="menu" :aria-label="copy.methodOptionsLabel">
            <button
              type="button"
              class="login-card__method-option login-card__method-option--current"
              role="menuitemradio"
              aria-checked="true"
              data-testid="login-method-password"
              @click="closeLoginMethodMenu()"
            >
              <User class="login-card__method-option-icon" aria-hidden="true" />
              <span class="login-card__method-option-copy">
                <strong>{{ copy.currentAccount }}</strong>
                <small>{{ copy.usernamePassword }}</small>
              </span>
              <CircleCheckFilled class="login-card__method-option-check" aria-hidden="true" />
            </button>
            <button
              type="button"
              class="login-card__method-option"
              role="menuitemradio"
              aria-checked="false"
              disabled
              aria-disabled="true"
              data-testid="login-method-domain"
            >
              <OfficeBuilding class="login-card__method-option-icon" aria-hidden="true" />
              <span class="login-card__method-option-copy">
                <strong>{{ copy.domain }}</strong>
                <small>{{ copy.domainDescription }}</small>
              </span>
            </button>
            <router-link
              v-if="isHttpAuth"
              class="login-card__method-option"
              role="menuitemradio"
              aria-checked="false"
              data-testid="login-method-sso"
              :to="{ name: ROUTE_NAMES.ssoLogin, query: ssoRedirectQuery }"
              @click="loginMethodMenuOpen = false"
            >
              <Stamp class="login-card__method-option-icon" aria-hidden="true" />
              <span class="login-card__method-option-copy">
                <strong>{{ copy.sso }}</strong>
                <small>{{ copy.ssoDescription }}</small>
              </span>
            </router-link>
            <button
              v-else
              type="button"
              class="login-card__method-option"
              role="menuitemradio"
              aria-checked="false"
              disabled
              aria-disabled="true"
              data-testid="login-method-sso"
            >
              <Stamp class="login-card__method-option-icon" aria-hidden="true" />
              <span class="login-card__method-option-copy">
                <strong>{{ copy.sso }}</strong>
                <small>{{ copy.ssoHttpDescription }}</small>
              </span>
            </button>
          </div>
        </aside>
      </Transition>
    </section>
  </main>
</template>

<style scoped>
.login-page {
  box-sizing: border-box;
  display: grid;
  width: 100%;
  height: 100vh;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  overflow: hidden;
  place-items: center;
  background: var(--ip-color-bg-page);
}

.login-card {
  position: relative;
  display: flex;
  align-items: stretch;
  width: min(100%, var(--login-form-width));
  max-width: var(--login-form-width);
  overflow: visible;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-md);
}

.login-card__form-pane {
  box-sizing: border-box;
  display: flex;
  flex: 0 0 auto;
  flex-direction: column;
  gap: var(--ip-space-4);
  width: var(--login-form-width);
  min-width: 0;
  padding: var(--ip-space-8) var(--ip-space-6);
}

.login-card__header {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.login-card__header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}

.login-card__header-row :deep(.ip-brand) {
  max-width: calc(100% - 40px);
}

.login-card__locale {
  flex: 0 0 32px;
}

.login-card__locale :deep(.ip-locale-control) {
  color: var(--ip-color-text-secondary);
}

.login-card__locale :deep(.ip-locale-control:hover),
.login-card__locale :deep(.ip-locale-control:focus-visible) {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

:global([data-ip-color-mode='dark'] .login-card__header-row .ip-brand__image) {
  filter: brightness(0) invert(1);
}

:global([data-ip-color-mode='dark'] .login-card__locale .ip-locale-control) {
  color: var(--ip-color-text-primary);
}

.login-card__bootstrap-pending {
  padding: var(--ip-space-2) var(--ip-space-3);
  background: var(--ip-color-warning-bg, #fdf6ec);
  border: 1px solid var(--ip-color-warning, #e6a23c);
  border-radius: var(--ip-radius-sm);
  color: var(--ip-color-warning, #e6a23c);
  font-size: 13px;
  line-height: 1.5;
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
  gap: var(--ip-space-1);
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

.login-card__label-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
}

.login-card__username-area {
  position: relative;
}

.login-card__method-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-1);
  min-height: 24px;
  margin-top: 10px;
  padding: 0;
  color: var(--ip-color-primary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  font: inherit;
  font-size: var(--ip-font-size-sm);
  cursor: pointer;
}

.login-card__method-toggle:hover {
  color: var(--ip-color-primary-hover);
  text-decoration: underline;
}

.login-card__method-toggle :deep(svg) {
  width: 14px;
  height: 14px;
}

.login-card__method-toggle:focus-visible,
.login-card__method-panel-close:focus-visible,
.login-card__method-option:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.login-card__method-toggle[aria-expanded='true'] {
  background: var(--ip-color-primary-bg);
  text-decoration: none;
}

.login-card__method-panel {
  position: absolute;
  z-index: 10;
  top: -1px;
  bottom: -1px;
  left: calc(100% - 1px);
  box-sizing: border-box;
  flex: 0 0 380px;
  width: min(380px, calc(50vw - var(--login-form-width) / 2 - var(--ip-space-4)));
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
  padding: var(--ip-space-5);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-left: 0;
  border-radius: 0 var(--ip-radius-lg) var(--ip-radius-lg) 0;
  box-shadow: var(--ip-shadow-md);
  transform-origin: left center;
}

.login-card__method-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}

.login-card__method-panel-header h2 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-xl);
}

.login-card__method-panel-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.login-card__method-panel-close:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.login-card__method-list {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  gap: var(--ip-space-2);
}

.login-card__method-option {
  display: grid;
  grid-template-columns: 40px minmax(0, 1fr) auto;
  flex: 1;
  min-height: 0;
  align-items: center;
  gap: var(--ip-space-3);
  padding: var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  font: inherit;
  text-align: left;
  text-decoration: none;
  cursor: pointer;
}

.login-card__method-option:hover:not(:disabled),
.login-card__method-option--current {
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
  border-color: var(--ip-color-primary);
}

.login-card__method-option:disabled {
  color: var(--ip-color-text-disabled);
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
  cursor: not-allowed;
}

.login-card__method-option-icon {
  width: 32px;
  height: 32px;
  color: var(--ip-color-primary);
}

.login-card__method-option:disabled .login-card__method-option-icon {
  color: var(--ip-color-text-disabled);
}

.login-card__method-option-copy {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.login-card__method-option-copy strong {
  font-size: var(--ip-font-size-md);
  font-weight: 600;
}

.login-card__method-option-copy small {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.login-card__method-option:disabled .login-card__method-option-copy small {
  color: var(--ip-color-text-disabled);
}

.login-card__method-option-check {
  width: 24px;
  height: 24px;
  color: var(--ip-color-primary);
}

.login-method-panel-enter-active,
.login-method-panel-leave-active {
  transition:
    opacity 220ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 220ms cubic-bezier(0.16, 1, 0.3, 1);
}

.login-method-panel-enter-from,
.login-method-panel-leave-to {
  opacity: 0;
  transform: translateX(-18px) scale(0.96);
}

@media (prefers-reduced-motion: reduce) {
  .login-card,
  .login-method-panel-enter-active,
  .login-method-panel-leave-active {
    transition: none;
  }
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

.login-card__toggle :deep(svg) {
  width: 18px;
  height: 18px;
}

.login-card__error {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-danger);
}

.login-card__field-error {
  min-height: 16px;
  font-size: var(--ip-font-size-xs);
  line-height: 16px;
  visibility: hidden;
}

.login-card__field-error--visible {
  visibility: visible;
}

.login-card__error--login {
  padding: var(--ip-space-2) var(--ip-space-3);
  background: var(--ip-color-danger-bg);
  border-radius: var(--ip-radius-md);
}

.login-card__submit {
  box-sizing: border-box;
  height: 40px;
  margin-top: var(--ip-space-1);
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

@media (max-width: 960px) {
  .login-card {
    flex-direction: column;
  }

  .login-card__method-panel {
    position: static;
    z-index: 10;
    flex: 0 0 auto;
    width: 100%;
    max-height: none;
    overflow: visible;
    border-top: 1px solid var(--ip-color-border);
    border-left: 0;
    border-radius: 0 0 var(--ip-radius-lg) var(--ip-radius-lg);
  }
}

@media (max-width: 520px) {
  .login-page {
    align-items: start;
    height: auto;
    min-height: 100vh;
    padding: var(--ip-space-4);
    overflow-x: hidden;
    overflow-y: auto;
  }

  .login-card {
    width: 100%;
    max-width: none;
  }

  .login-card__form-pane {
    width: 100%;
  }
}
</style>
