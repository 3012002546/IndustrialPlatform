export type ReportRect = { xMm: number; yMm: number; widthMm: number; heightMm: number }
export type ResizeEdge = '' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw'
export const resizeEdges: ResizeEdge[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w']
export const roundMm = (value: number) => Math.round(value * 10) / 10
export const reportUnitMm = { mm: 1, cm: 10, in: 25.4, px: 25.4 / 96 } as const
export type ReportUnit = keyof typeof reportUnitMm
export const reportUnitValue = (mm: number, unit: ReportUnit) =>
  Number((mm / reportUnitMm[unit]).toFixed(4))
export const reportMillimetres = (value: number, unit: ReportUnit) =>
  Number((value * reportUnitMm[unit]).toFixed(4))

// Compare edges and centres within the current band. Tolerance is supplied in mm
// so the caller can keep it constant in screen pixels at every zoom level.
export function transformReportRect(
  start: ReportRect,
  dx: number,
  dy: number,
  edge: ResizeEdge,
  peers: ReportRect[],
  bodyWidth: number,
  tolerance: number,
) {
  const moving = !edge
  let left = start.xMm + (moving || edge.includes('w') ? dx : 0)
  let top = start.yMm + (moving || edge.includes('n') ? dy : 0)
  let right = start.xMm + start.widthMm + (moving || edge.includes('e') ? dx : 0)
  let bottom = start.yMm + start.heightMm + (moving || edge.includes('s') ? dy : 0)
  const xs = [
    0,
    bodyWidth / 2,
    bodyWidth,
    ...peers.flatMap((p) => [p.xMm, p.xMm + p.widthMm / 2, p.xMm + p.widthMm]),
  ]
  const ys = [0, ...peers.flatMap((p) => [p.yMm, p.yMm + p.heightMm / 2, p.yMm + p.heightMm])]
  function nearest(values: number[], targets: number[]) {
    let match: { delta: number; guide: number } | undefined
    for (const value of values)
      for (const target of targets) {
        const delta = target - value
        if (Math.abs(delta) <= tolerance && (!match || Math.abs(delta) < Math.abs(match.delta)))
          match = { delta, guide: target }
      }
    return match
  }
  const x = nearest(
    moving
      ? [left, (left + right) / 2, right]
      : edge.includes('w')
        ? [left]
        : edge.includes('e')
          ? [right]
          : [],
    xs,
  )
  const y = nearest(
    moving
      ? [top, (top + bottom) / 2, bottom]
      : edge.includes('n')
        ? [top]
        : edge.includes('s')
          ? [bottom]
          : [],
    ys,
  )
  if (x) {
    if (moving || edge.includes('w')) left += x.delta
    if (moving || edge.includes('e')) right += x.delta
  }
  if (y) {
    if (moving || edge.includes('n')) top += y.delta
    if (moving || edge.includes('s')) bottom += y.delta
  }
  if (moving) {
    left = Math.max(0, Math.min(bodyWidth - start.widthMm, left))
    right = left + start.widthMm
    top = Math.max(0, top)
    bottom = top + start.heightMm
  } else {
    if (edge.includes('w')) left = Math.max(0, Math.min(right - 1, left))
    if (edge.includes('e')) right = Math.max(left + 1, Math.min(bodyWidth, right))
    if (edge.includes('n')) top = Math.max(0, Math.min(bottom - 1, top))
    if (edge.includes('s')) bottom = Math.max(top + 1, bottom)
  }
  const rect = {
    xMm: roundMm(left),
    yMm: roundMm(top),
    widthMm: roundMm(right - left),
    heightMm: roundMm(bottom - top),
  }
  const close = (value: number, points: number[]) => points.some((p) => Math.abs(p - value) < 0.11)
  return {
    rect,
    guideX: x && close(x.guide, [left, (left + right) / 2, right]) ? x.guide : undefined,
    guideY: y && close(y.guide, [top, (top + bottom) / 2, bottom]) ? y.guide : undefined,
  }
}
