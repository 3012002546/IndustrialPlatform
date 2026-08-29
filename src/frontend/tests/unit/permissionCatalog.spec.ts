import { describe, expect, it } from 'vitest'

import { GENERATED_PERMISSION_NIDS } from '@/permissions/catalog.generated'
import { PERMISSIONS } from '@/permissions/catalog'

describe('platform permission catalog', () => {
  it('matches the generated stable NId directory without duplicates', () => {
    const frontendNIds = Object.values(PERMISSIONS)
    expect(frontendNIds).toEqual([...GENERATED_PERMISSION_NIDS])
    expect(new Set(frontendNIds).size).toBe(frontendNIds.length)
    expect(frontendNIds).toContain('platform.operation.view')
  })

  it('contains SystemData view and operation gates', () => {
    expect(Object.values(PERMISSIONS)).toEqual(
      expect.arrayContaining([
        'systemdata.organization.create',
        'systemdata.organization.update',
        'systemdata.organization.move',
        'systemdata.organization.status',
        'systemdata.position.create',
        'systemdata.position.update',
        'systemdata.position.status',
        'systemdata.assignment.manage',
        'systemdata.navigation.manage',
        'systemdata.navigation.publish',
        'systemdata.navigation.rollback',
        'systemdata.feature.manage',
        'systemdata.service-catalog.manage',
        'systemdata.theme-policy.manage',
        'systemdata.service-initialization.register',
        'systemdata.service-initialization.plan',
        'systemdata.service-initialization.apply',
        'systemdata.service-initialization.approve',
        'systemdata.service-initialization.backup',
        'systemdata.service-initialization.cancel',
      ]),
    )
  })
})
