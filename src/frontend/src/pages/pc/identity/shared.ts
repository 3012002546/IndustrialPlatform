/**
 * Identity 管理页共享辅助(§16):错误呈现与并发冲突处理。
 * 409 ID_CONCURRENCY_CONFLICT → 提示数据已被他人修改并提供重载;其余按 ApiError 文案。
 */

import { ElMessage, ElMessageBox } from 'element-plus'

import { ApiError } from '@/api/errors'
import { localeMessages, platformI18n } from '@/localization/i18n'
import { formatDateTime } from '@/localization/formatters'
import type { SupportedLocale } from '@/localization/types'

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
    const locale = (platformI18n.global.locale as unknown as { value?: unknown }).value === 'en-US'
      ? 'en-US'
      : 'zh-CN'
    const copy = localeMessages[locale].common.errors
    void ElMessageBox.alert(copy.conflictMessage, copy.conflictTitle, {
      type: 'warning',
      confirmButtonText: copy.reload,
    }).then(() => {
      window.location.reload()
    })
    return
  }
  ElMessage.error(errorMessage(error, fallback))
}

/** 时间展示:null/空回退占位。 */
export function formatTime(
  value: string | null | undefined,
  options?: { locale?: SupportedLocale; timeZone?: string },
): string {
  if (value === null || value === undefined || value.length === 0) return '—'
  const locale = options?.locale ?? ((platformI18n.global.locale as unknown as { value?: unknown }).value === 'en-US' ? 'en-US' : 'zh-CN')
  const timeZone = options?.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC'
  return formatDateTime(value, { locale, timeZone }) || '—'
}

/** 下载后端生成的文件，所有管理表格共用同一下载路径。 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
