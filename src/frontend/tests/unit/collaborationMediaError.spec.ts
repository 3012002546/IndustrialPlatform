import { describe, expect, it } from 'vitest'

import { formatMediaError } from '@/components/collaboration/mediaError'
import { zhCN } from '@/locales/zh-CN'

describe('collaboration media error presentation', () => {
  it('localizes known capability errors and keeps the technical code visible', () => {
    expect(formatMediaError(zhCN.collaboration.media, 'MEDIA_DISABLED')).toBe(
      '当前环境未启用该媒体能力。（MEDIA_DISABLED）',
    )
  })

  it('localizes ICE configuration errors', () => {
    expect(formatMediaError(zhCN.collaboration.media, 'MEDIA_ICE_CONFIGURATION')).toBe(
      '媒体网络配置不可用，请联系管理员检查 ICE/TURN 配置。（MEDIA_ICE_CONFIGURATION）',
    )
  })

  it('falls back to a safe localized message for an unknown code', () => {
    expect(formatMediaError(zhCN.collaboration.media, 'MEDIA_NEW_CODE')).toBe(
      '媒体操作未完成，请稍后重试。（MEDIA_NEW_CODE）',
    )
  })
})
