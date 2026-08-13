/**
 * 主题色板对比度测试(PF-01 §12.2)。
 * 普通文字 ≥ 4.5:1;顶栏渐变停靠点为大型图形表面,白字按非文字/大图形阈值 ≥ 3:1。
 * 色板值必须与 src/styles/themes.css 保持一致;改动 Token 需同步更新本文件。
 */

import { describe, expect, it } from 'vitest'

import {
  contrastRatio,
  isNonTextContrastPassing,
  isTextContrastPassing,
  parseHexColor,
  relativeLuminance,
} from '@/theme'

describe('parseHexColor', () => {
  it('解析 #rgb 与 #rrggbb', () => {
    expect(parseHexColor('#fff')).toEqual({ r: 255, g: 255, b: 255 })
    expect(parseHexColor('#1f2937')).toEqual({ r: 31, g: 41, b: 55 })
  })

  it('非法输入 → null', () => {
    expect(parseHexColor('')).toBeNull()
    expect(parseHexColor('#12')).toBeNull()
    expect(parseHexColor('#gggggg')).toBeNull()
    expect(parseHexColor('red')).toBeNull()
  })
})

describe('relativeLuminance / contrastRatio', () => {
  it('白/黑极端值', () => {
    expect(relativeLuminance('#ffffff')).toBeCloseTo(1, 5)
    expect(relativeLuminance('#000000')).toBeCloseTo(0, 5)
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 2)
  })

  it('对比度可交换(不区分前景/背景)', () => {
    expect(contrastRatio('#1f2937', '#ffffff')).toBeCloseTo(contrastRatio('#ffffff', '#1f2937'), 5)
  })
})

/** 明亮模式:中性文字 + 状态色在容器/白色底上的对比度。 */
const LIGHT_TEXT_PAIRS = [
  ['#1f2937', '#ffffff'], // text-primary on bg-container
  ['#4b5563', '#ffffff'], // text-secondary on bg-container
  ['#6b7280', '#ffffff'], // text-tertiary on bg-container
  ['#1f2937', '#f3f4f6'], // text-primary on bg-page
  ['#15803d', '#f0fdf4'], // success on success-bg
  ['#b45309', '#fffbeb'], // warning on warning-bg
  ['#b91c1c', '#fef2f2'], // danger on danger-bg
  ['#475569', '#f8fafc'], // info on info-bg
]

/** 暗色模式:中性文字在暗色容器上的对比度。 */
const DARK_TEXT_PAIRS = [
  ['#f9fafb', '#1f2937'], // text-primary on bg-container(dark)
  ['#d1d5db', '#1f2937'], // text-secondary on bg-container(dark)
  ['#9ca3af', '#1f2937'], // text-tertiary on bg-container(dark)
  ['#4ade80', '#1f2937'], // success(dark)
  ['#fbbf24', '#1f2937'], // warning(dark)
  ['#f87171', '#1f2937'], // danger(dark)
  ['#94a3b8', '#1f2937'], // info(dark)
]

/** 三配色主色族:白字在主色上的对比度(亮暗共用,见 themes.css 设计说明)。 */
const PRIMARY_PAIRS = [
  ['#0077a1', '#ffffff'], // industrial-cyan
  ['#2563eb', '#ffffff'], // technology-blue
  ['#4b5563', '#ffffff'], // neutral-gray
]

/** 工业青顶栏渐变五个停靠点对白字(非文字/大图形阈值 3:1,§7.5 已批准渐变)。 */
const TOOLBAR_GRADIENT_STOPS = ['#006487', '#006b91', '#0077a1', '#158dac', '#087c9f']

describe('主题色对比度 — 普通文字 ≥ 4.5:1', () => {
  it.each(LIGHT_TEXT_PAIRS)('%s on %s', (fg, bg) => {
    expect(isTextContrastPassing(fg, bg)).toBe(true)
  })

  it.each(DARK_TEXT_PAIRS)('dark %s on %s', (fg, bg) => {
    expect(isTextContrastPassing(fg, bg)).toBe(true)
  })

  it.each(PRIMARY_PAIRS)('主色 %s 白字', (fg, bg) => {
    expect(isTextContrastPassing(fg, bg)).toBe(true)
  })
})

describe('主题色对比度 — 顶栏渐变停靠点(大图形 ≥ 3:1)', () => {
  it.each(TOOLBAR_GRADIENT_STOPS)('白字在 %s', (stop) => {
    expect(isNonTextContrastPassing('#ffffff', stop)).toBe(true)
  })
})
