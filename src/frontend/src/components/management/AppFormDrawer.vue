<script setup lang="ts">
/**
 * AppFormDrawer(PF-01 §7.10):表单抽屉。
 * - Element Plus focus trap;关闭后焦点返回触发点。
 * - PC 宽度 narrow/medium/wide = 420/560/720px;PDA/Mobile 强制全宽。
 * - busy 阻止重复提交(submit 按钮禁用并忽略)。
 */

import { computed, watch } from 'vue'
import { useId } from 'vue'
import { ElFocusTrap } from 'element-plus/es/components/focus-trap/index'

import { useDeviceStore } from '@/stores/deviceStore'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    size?: 'narrow' | 'medium' | 'wide'
    busy?: boolean
  }>(),
  { size: 'medium', busy: false },
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  submit: []
  cancel: []
}>()

const deviceStore = useDeviceStore()
const titleId = useId()

const open = computed(() => props.modelValue)
const handheld = computed(() => deviceStore.terminal !== 'pc')

/** 打开前的焦点元素,关闭时归还(§7.10 focus trap)。 */
let lastFocused: HTMLElement | null = null

watch(
  () => props.modelValue,
  (value) => {
    if (value) {
      lastFocused = (document.activeElement as HTMLElement | null) ?? null
    } else if (lastFocused !== null && typeof lastFocused.focus === 'function') {
      lastFocused.focus()
    }
  },
)

function close(kind: 'cancel'): void {
  if (kind === 'cancel') emit('cancel')
  emit('update:modelValue', false)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') close('cancel')
}

function onSubmit(): void {
  if (props.busy) return
  emit('submit')
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="app-form-drawer" :class="`app-form-drawer--${size}`">
      <div
        class="app-form-drawer__backdrop"
        data-testid="form-drawer-backdrop"
        @click="close('cancel')"
      />
      <ElFocusTrap :trapped="open" :loop="true">
        <aside
          class="app-form-drawer__panel"
          :class="{ 'app-form-drawer__panel--handheld': handheld }"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
          @keydown="onKeydown"
        >
          <header class="app-form-drawer__header">
            <h2 :id="titleId" class="app-form-drawer__title">{{ title }}</h2>
            <button
              type="button"
              class="app-form-drawer__close"
              data-testid="form-drawer-close"
              aria-label="关闭"
              @click="close('cancel')"
            >
              <span aria-hidden="true">✕</span>
            </button>
          </header>
          <div class="app-form-drawer__body">
            <slot />
          </div>
          <footer class="app-form-drawer__footer">
            <slot name="footer">
              <button
                type="button"
                class="app-form-drawer__btn"
                data-testid="form-drawer-cancel"
                @click="close('cancel')"
              >
                取消
              </button>
              <button
                type="button"
                class="app-form-drawer__btn app-form-drawer__btn--primary"
                data-testid="form-drawer-submit"
                :disabled="busy"
                @click="onSubmit"
              >
                {{ busy ? '提交中…' : '确定' }}
              </button>
            </slot>
          </footer>
        </aside>
      </ElFocusTrap>
    </div>
  </Teleport>
</template>

<style scoped>
.app-form-drawer {
  position: fixed;
  inset: 0;
  z-index: var(--ip-z-drawer);
  display: flex;
  justify-content: flex-end;
}

.app-form-drawer__backdrop {
  position: absolute;
  inset: 0;
  background: var(--ip-color-bg-overlay);
}

.app-form-drawer__panel {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 560px;
  height: 100%;
  background: var(--ip-color-bg-container);
  box-shadow: var(--ip-shadow-lg);
  color: var(--ip-color-text-primary);
}

.app-form-drawer--narrow .app-form-drawer__panel {
  width: 420px;
}

.app-form-drawer--wide .app-form-drawer__panel {
  width: 720px;
}

.app-form-drawer__panel--handheld {
  width: 100% !important;
}

.app-form-drawer__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  border-bottom: 1px solid var(--ip-color-border);
}

.app-form-drawer__title {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 600;
}

.app-form-drawer__close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
  cursor: pointer;
}

.app-form-drawer__close:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.app-form-drawer__body {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  padding: var(--ip-space-4);
}

.app-form-drawer__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  border-top: 1px solid var(--ip-color-border);
}

.app-form-drawer__btn {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-primary);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.app-form-drawer__btn--primary {
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
  color: var(--ip-color-on-primary);
}

.app-form-drawer__btn--primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.app-form-drawer__btn:focus-visible,
.app-form-drawer__close:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

@media (prefers-reduced-motion: reduce) {
  .app-form-drawer__panel {
    transition: none;
  }
}
</style>
