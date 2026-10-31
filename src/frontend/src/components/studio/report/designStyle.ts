import type { CSSProperties } from 'vue'
import type { Json } from '@/api/studio'

// The editor uses 3 px/mm; preserve the renderer's pt/mm proportions at every zoom.
export function reportDesignStyle(style: Json = {}): CSSProperties {
  const css: CSSProperties = {
    fontFamily: style.fontFamily ?? 'sans-serif',
    fontSize: `${((Number(style.fontSizePt ?? 10) * 25.4) / 72) * 3}px`,
    fontWeight: style.fontWeight ?? 400,
    lineHeight: style.lineHeight ?? 1.3,
    color: style.color ?? '#000000',
    backgroundColor: style.backgroundColor ?? 'transparent',
    textAlign:
      style.textAlign === 'Right' ? 'right' : style.textAlign === 'Center' ? 'center' : 'left',
    fontStyle: style.italic ? 'italic' : 'normal',
    verticalAlign:
      style.verticalAlign === 'Middle'
        ? 'middle'
        : style.verticalAlign === 'Bottom'
          ? 'bottom'
          : 'top',
  }
  if (style.paddingMm)
    css.padding = ['top', 'right', 'bottom', 'left']
      .map((side) => `${Number(style.paddingMm[side] ?? 0) * 3}px`)
      .join(' ')
  if (style.border)
    css.border = `${Number(style.border.widthMm ?? 0.2) * 3}px ${style.border.style === 'None' ? 'none' : style.border.style === 'Dashed' ? 'dashed' : 'solid'} ${style.border.color ?? '#000000'}`
  return css
}
