import type { ReferenceScope } from './types'

export type ConfigurationDataType =
  'String' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime' | 'Enum' | 'Json' | 'Reference'
export type ConfigurationValueMode = 'Single' | 'Multi'
export type ConfigurationStatus = 'Active' | 'Disabled'
export interface ConfigurationDomainSummary {
  id: string
  nId: string
  name: string
  scopeType: ReferenceScope
  tenantNId: string | null
  status: ConfigurationStatus
  revision: number
  keyCount: number
  lastUpdatedOn: string
  optimisticVersion: number
  concurrencyVersion: string
  isFrozen: boolean
  isLocked: boolean
}
export interface ConfigurationDomain extends Omit<ConfigurationDomainSummary, 'keyCount'> {
  description: string | null
  keys: ConfigurationKey[]
}
export interface ConfigurationKey {
  id: string
  nId: string
  fullNId: string
  name: string
  description: string | null
  dataType: ConfigurationDataType
  valueMode: ConfigurationValueMode
  value: unknown
  defaultValue: unknown
  valueJson: string | null
  defaultValueJson: string | null
  isMandatory: boolean
  isReadOnly: boolean
  dictionaryNId: string | null
  referenceTarget: string | null
  status: ConfigurationStatus
  sort: number
  multiValues: ConfigurationMultiValue[]
  hasHadValue: boolean
  isFrozen: boolean
  isLocked: boolean
}
export interface ConfigurationMultiValue {
  id: string
  nId: string
  name: string | null
  value: unknown
  valueJson: string
  sort: number
  isDefault: boolean
  enabled: boolean
}
export interface CreateConfigurationDomain {
  scopeType: ReferenceScope
  scopeId: null
  nId: string
  name: string
  description: string | null
  changeReason: string
}
export interface UpdateConfigurationDomain extends CreateConfigurationDomain {
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}
export interface ConfigurationVersion {
  changeReason: string
  expectedAppDomainOptimisticVersion: number
  expectedAppDomainConcurrencyVersion: string
}
export interface WriteConfigurationKey extends ConfigurationVersion {
  nId: string
  name: string
  description: string | null
  dataType: ConfigurationDataType
  valueMode: ConfigurationValueMode
  valueJson: string | null
  defaultValueJson: string | null
  isMandatory: boolean
  isReadOnly: boolean
  dictionaryNId: string | null
  referenceTarget: string | null
  status: ConfigurationStatus
  sort: number
}
export interface WriteConfigurationValue extends ConfigurationVersion {
  nId: string
  name: string | null
  valueJson: string
  sort: number
  isDefault: boolean
  enabled: boolean
}
export interface EffectiveConfiguration {
  appDomainNId: string
  keyNId: string
  fullNId: string
  dataType: ConfigurationDataType
  valueMode: ConfigurationValueMode
  value: unknown
  valueJson: string | null
  multiValues: ConfigurationMultiValue[]
  usesDefaultValue: boolean
  blocksInheritance: boolean
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  lastUpdatedOn: string
}
export interface ConfigurationHistory {
  id: string
  appDomainId: string
  keyId: string | null
  objectType: string
  objectId: string
  fullNId: string
  changeType: string
  changeReason: string
  beforeSummary: string
  afterSummary: string
  revision: number
  userNId: string
  traceId: string
  createdOn: string
}
