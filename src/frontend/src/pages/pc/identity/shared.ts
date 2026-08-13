/**
 * Identity 管理页共享辅助(§16):错误呈现与并发冲突处理。
 * 409 ID_CONCURRENCY_CONFLICT → 提示数据已被他人修改并提供重载;其余按 ApiError 文案。
 */

import { ElMessage, ElMessageBox } from 'element-plus'

import { ApiError } from '@/api/errors'

/** 是否后端乐观并发冲突(409 ID_CONCURRENCY_CONFLICT)。 */
export function isConcurrencyConflict(error: unknown): boolean {
  return error instanceof ApiError && error.details.code === 'ID_CONCURRENCY_CONFLICT'
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.details.message.length > 0
    ? error.details.message
    : fallback
}

/**
 * 管理操作失败统一呈现:
 * - 并发冲突:弹窗提示重新加载(用户确认后刷新页面)。
 * - 其他:ElMessage.error 展示后端/兜底文案。
 */
export function reportManagementError(error: unknown, fallback: string): void {
  if (isConcurrencyConflict(error)) {
    void ElMessageBox.alert('数据已被其他管理员修改,请重新加载后重试。', '数据冲突', {
      type: 'warning',
      confirmButtonText: '重新加载',
    }).then(() => {
      window.location.reload()
    })
    return
  }
  ElMessage.error(errorMessage(error, fallback))
}

/** 时间展示:null/空回退占位。 */
export function formatTime(value: string | null | undefined): string {
  if (value === null || value === undefined || value.length === 0) return '—'
  return value.replace('T', ' ').slice(0, 19)
}
