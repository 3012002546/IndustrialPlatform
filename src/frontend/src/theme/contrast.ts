/**
 * WCAG 对比度纯函数(PF-01 §12.2)。
 * 普通文字 ≥ 4.5:1,关键非文字控件/大图形 ≥ 3:1。
 * 视觉回归任务用这些函数验证主题色板与顶栏渐变停靠点。
 */

/** 解析 #rgb 或 #rrggbb 十六进制颜色,非法输入返回 null。 */
export function parseHexColor(hex: string): { r: number; g: number; b: number } | null {
  let normalized = hex.trim().replace(/^#/, '')
  if (normalized.length === 3) {
    normalized = normalized
      .split('')
      .map((ch) => ch + ch)
      .join('')
  }
  if (!/^[0-9a-fA-F]{6}$/.test(normalized)) return null
  const r = Number.parseInt(normalized.slice(0, 2), 16)
  const g = Number.parseInt(normalized.slice(2, 4), 16)
  const b = Number.parseInt(normalized.slice(4, 6), 16)
  return { r, g, b }
}

function channelLinear(value: number): number {
  const s = value / 255
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
}

/** 相对亮度(WCAG 定义)。 */
export function relativeLuminance(hex: string): number {
  const parsed = parseHexColor(hex)
  if (parsed === null) return 0
  return (
    0.2126 * channelLinear(parsed.r) +
    0.7152 * channelLinear(parsed.g) +
    0.0722 * channelLinear(parsed.b)
  )
}

/** 两个颜色的对比度(WCAG 定义,1~21)。 */
export function contrastRatio(foreground: string, background: string): number {
  const l1 = relativeLuminance(foreground)
  const l2 = relativeLuminance(background)
  const lighter = Math.max(l1, l2)
  const darker = Math.min(l1, l2)
  return (lighter + 0.05) / (darker + 0.05)
}

/** 普通文字对比度 ≥ 4.5:1。 */
export function isTextContrastPassing(foreground: string, background: string): boolean {
  return contrastRatio(foreground, background) >= 4.5
}

/** 关键非文字控件/大图形对比度 ≥ 3:1。 */
export function isNonTextContrastPassing(foreground: string, background: string): boolean {
  return contrastRatio(foreground, background) >= 3
}
