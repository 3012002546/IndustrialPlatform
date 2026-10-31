import { describe, it, expect } from 'vitest'
import { transformReportRect, type ReportRect } from '@/components/studio/report/geometry'

const start: ReportRect = { xMm: 10, yMm: 10, widthMm: 20, heightMm: 10 }
describe('report pointer geometry', () => {
  it('snaps moving edges to peer edges and centres with a screen-derived tolerance', () => {
    const result = transformReportRect(
      start,
      9.3,
      10,
      '',
      [{ xMm: 20, yMm: 40, widthMm: 20, heightMm: 10 }],
      180,
      1,
    )
    expect(result.rect.xMm).toBe(20)
    expect(result.guideX).toBe(20)
    expect(result.rect.yMm).toBe(20)
  })
  it('keeps opposite edges fixed when resizing from the top-left', () => {
    const result = transformReportRect(start, -4, -3, 'nw', [], 180, -1)
    expect(result.rect).toEqual({ xMm: 6, yMm: 7, widthMm: 24, heightMm: 13 })
  })
  it('clamps a resize to positive dimensions and the printable body width', () => {
    expect(transformReportRect(start, 1000, -1000, 'ne', [], 180, -1).rect).toEqual({
      xMm: 10,
      yMm: 0,
      widthMm: 170,
      heightMm: 20,
    })
    expect(transformReportRect(start, -1000, -1000, 'se', [], 180, -1).rect).toEqual({
      xMm: 10,
      yMm: 10,
      widthMm: 1,
      heightMm: 1,
    })
  })
  it('disables snapping with Alt and preserves size when moving against a boundary', () => {
    const result = transformReportRect(
      start,
      9.3,
      0,
      '',
      [{ xMm: 20, yMm: 40, widthMm: 20, heightMm: 10 }],
      180,
      -1,
    )
    expect(result.rect.xMm).toBe(19.3)
    expect(result.guideX).toBeUndefined()
    expect(transformReportRect(start, 500, -500, '', [], 180, 1).rect).toEqual({
      xMm: 160,
      yMm: 0,
      widthMm: 20,
      heightMm: 10,
    })
  })
})
