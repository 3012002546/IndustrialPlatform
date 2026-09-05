import type { ConfigurationDataType } from './parameterTypes'
import type { PublicationStatus, ReferenceScope, VersionRequest } from './types'

export interface DynamicFieldWrite {
  nId: string
  name: string
  dataType: ConfigurationDataType
  required: boolean
  enabled: boolean
  sort: number
  defaultValueJson: string | null
  minLength: number | null
  maxLength: number | null
  minValueJson: string | null
  maxValueJson: string | null
  scale: number | null
  pattern: string | null
  dictionaryNId: string | null
  referenceTarget: string | null
  description: string | null
}
export interface DynamicField extends DynamicFieldWrite {
  id: string
  defaultValue: unknown
  minValue: number | null
  maxValue: number | null
  hasHadValue: boolean
  wasPublished: boolean
}
export interface DynamicConfigurationSummary {
  id: string
  nId: string
  name: string
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  fieldCount: number
  recordCount: number
  valueCount: number
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  isFrozen: boolean
  isLocked: boolean
  publishedOn: string | null
}
export interface DynamicConfiguration extends Omit<DynamicConfigurationSummary, 'fieldCount'> {
  description: string | null
  fields: DynamicField[]
  publishedBy: string | null
}
export interface CreateDynamicConfiguration {
  scopeType: ReferenceScope
  scopeId: null
  nId: string
  name: string
  description: string | null
  fields: DynamicFieldWrite[]
}
export interface UpdateDynamicConfiguration extends VersionRequest {
  name: string
  description: string | null
  fields: DynamicFieldWrite[]
}
export interface DynamicRecord {
  id: string
  nId: string
  name: string | null
  category: string | null
  sort: number
  enabled: boolean
  values: Record<string, unknown>
  valuesJson: Record<string, string>
  revision: number
  isFrozen: boolean
  isLocked: boolean
}
export interface DynamicRecordWrite extends VersionRequest {
  nId: string
  name: string | null
  category: string | null
  sort: number
  enabled: boolean
  valuesJson: Record<string, string>
}
export interface DynamicRecordMutation {
  record: DynamicRecord
  optimisticVersion: number
  concurrencyVersion: string
}
export interface DynamicRecordQuery {
  pageIndex: number
  pageSize: number
  keyword?: string
  nId?: string
  category?: string
}
export interface DynamicSchema {
  definitionId: string
  nId: string
  name: string
  fields: DynamicField[]
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  publishedOn: string
}
export interface DynamicPublicationCheck {
  previousRevision: number | null
  addedFields: string[]
  changedFields: string[]
  disabledFields: string[]
  recordCount: number
  valueCount: number
  errors: { code: string; field: string | null }[]
}
