import { describe, expect, it } from 'vitest'

import { operationLaunchers } from '@/operation/launchers'

describe('operationLaunchers', () => {
  it('固定九个入口,八个未来入口没有伪造业务契约', () => {
    expect(operationLaunchers).toHaveLength(9)
    const comingSoon = operationLaunchers.filter((item) => item.state === 'coming-soon')
    expect(comingSoon).toHaveLength(8)
    for (const item of comingSoon) {
      expect(item.routeName).toBeUndefined()
      expect(item.permission).toBeUndefined()
      expect(item.featureNId).toBeUndefined()
      expect(item.titleKey).toMatch(/^operation\./)
    }
    expect(operationLaunchers.find((item) => item.id === 'interface-settings')).toMatchObject({
      state: 'available',
    })
  })
})
