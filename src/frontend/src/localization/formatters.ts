import type { SupportedLocale } from './types'

export interface DateTimeFormatOptions {
  locale: SupportedLocale
  timeZone: string
  dateOnly?: boolean
}

export function formatDateTime(value: string | number | Date, options: DateTimeFormatOptions): string {
  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(options.locale, {
    timeZone: options.timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    ...(options.dateOnly
      ? {}
      : { hour: 'numeric', minute: '2-digit', hour12: options.locale === 'en-US' }),
  }).format(date)
}

export function formatNumber(value: number, locale: SupportedLocale): string {
  return new Intl.NumberFormat(locale).format(value)
}
