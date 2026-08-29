import type { Component } from 'vue'

export type PcExperienceMode = 'management' | 'operation'

export interface PcExperienceScope {
  tenantId: string
  userId: string
  device: 'pc'
}

export interface OperationLauncher {
  id: string
  titleKey: string
  fallbackTitle: string
  icon: Component
  state: 'available' | 'coming-soon'
  routeName?: string
  permission?: string
  featureNId?: string
}
