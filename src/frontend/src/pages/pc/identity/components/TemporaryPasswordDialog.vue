<script setup lang="ts">
/**
 * 一次性临时密码弹窗(§29A.4/§29A.7)。
 * 服务端随机临时密码只在创建用户/重置密码响应中出现一次;本组件只在打开期间
 * 将该密码驻留内存,关闭(含 X/按钮/程序关闭)即不可逆清空,禁止写入任何
 * Store/localStorage/sessionStorage 或日志。
 */
import { ElMessage } from 'element-plus'
import { ref, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** 一次性临时密码:仅在弹窗打开期间驻留内存,关闭即清除。 */
    password: string
    title?: string
    /** 说明文字(如目标用户登录名)。 */
    description?: string
  }>(),
  { title: '临时密码', description: '' },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
}>()

const localPassword = ref('')

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      localPassword.value = props.password
    } else {
      // 关闭(含父组件程序关闭)即不可逆清空内存中的密码。
      localPassword.value = ''
    }
  },
  // immediate:已打开状态下挂载(如测试/热更新)也要立即填充。
  { immediate: true },
)

/** 兼容不支持 Clipboard API 的环境:临时 textarea + execCommand('copy')。 */
function fallbackCopy(text: string): boolean {
  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  textarea.style.position = 'fixed'
  textarea.style.opacity = '0'
  document.body.appendChild(textarea)
  textarea.select()
  let ok = false
  try {
    ok = document.execCommand('copy')
  } catch {
    ok = false
  }
  document.body.removeChild(textarea)
  return ok
}

async function copyPassword(): Promise<void> {
  let ok = false
  if (navigator.clipboard !== undefined) {
    try {
      await navigator.clipboard.writeText(localPassword.value)
      ok = true
    } catch {
      ok = false
    }
  }
  if (!ok) {
    ok = fallbackCopy(localPassword.value)
  }
  if (ok) {
    ElMessage.success('临时密码已复制,请妥善保管')
  } else {
    ElMessage.error('复制失败,请手动记录')
  }
}

/** el-dialog 内部关闭(X/遮罩/ESC)透传;关闭即清空密码。 */
function onUpdateModelValue(value: boolean): void {
  if (!value) {
    localPassword.value = ''
  }
  emit('update:modelValue', value)
}

/** 关闭即不可逆:清空内存中的密码,再次打开需由调用方重新传入。 */
function close(): void {
  localPassword.value = ''
  emit('update:modelValue', false)
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="title"
    width="480px"
    :close-on-click-modal="false"
    :close-on-press-escape="false"
    append-to-body
    class="temporary-password-dialog"
    @update:model-value="onUpdateModelValue"
  >
    <div class="temporary-password-dialog__body">
      <p v-if="description.length > 0" class="temporary-password-dialog__desc">
        {{ description }}
      </p>
      <p class="temporary-password-dialog__tip">
        临时密码仅显示这一次,关闭后无法再次查看。请立即复制并妥善保管;用户首次登录后必须修改密码。
      </p>
      <div class="temporary-password-dialog__box">
        <code class="temporary-password-dialog__password" data-testid="temporary-password">{{
          localPassword
        }}</code>
        <el-button
          type="primary"
          plain
          data-testid="temporary-password-copy"
          @click="copyPassword"
        >
          复制
        </el-button>
      </div>
    </div>
    <template #footer>
      <el-button type="primary" data-testid="temporary-password-confirm" @click="close">
        我已保存,关闭
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.temporary-password-dialog__body {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
}

.temporary-password-dialog__desc {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.temporary-password-dialog__tip {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-warning);
}

.temporary-password-dialog__box {
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
  padding: var(--ip-space-3);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-muted);
}

.temporary-password-dialog__password {
  flex: 1;
  font-family: var(--ip-font-mono);
  font-size: var(--ip-font-size-lg);
  word-break: break-all;
  user-select: all;
}
</style>
