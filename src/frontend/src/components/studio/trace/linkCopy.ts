import { useLocalizationStore } from '@/stores/localizationStore'

const labels = {
  openBrowserTab: ['在新浏览器标签页打开', 'Open in new browser tab'],
  expired: [
    '链接已过期，请关闭后从追溯节点重新打开。',
    'This link has expired. Close it and reopen it from the trace node.',
  ],
  fallback: [
    '若页面未显示，可在新浏览器标签页中查看。',
    'If the page does not appear, open it in a new browser tab.',
  ],
  preview: ['附件预览', 'File preview'],
  popupBlocked: [
    '浏览器阻止了新标签页，请允许弹出窗口后重试。',
    'The browser blocked the new tab. Allow popups and try again.',
  ],
  invalid: [
    '预览链接无效，请从追溯节点重新打开。',
    'Invalid preview link. Open it again from the trace node.',
  ],
} as const

export function useTraceLinkCopy() {
  const locale = useLocalizationStore()
  return (key: keyof typeof labels) => labels[key][locale.locale === 'en-US' ? 1 : 0]
}
