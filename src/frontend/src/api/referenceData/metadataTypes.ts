import type { PublicationStatus, ReferenceScope, VersionRequest } from './types'

export type MetadataDataType =
  'String' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime' | 'Enum' | 'Reference'

export interface MetadataAttributeWrite {
  nId: string
  name: string
  dataType: MetadataDataType
  required: boolean
  isArray: boolean
  enabled: boolean
  sort: number
  defaultValue: string | null
  minLength: number | null
  maxLength: number | null
  minValue: string | null
  maxValue: string | null
  pattern: string | null
  dictionaryNId: string | null
  referenceTarget: string | null
  precision: number | null
  scale: number | null
  unitDimensionNId: string | null
  defaultUnitNId: string | null
  unitRevision: number | null
  unitSourceScope: ReferenceScope | null
  unitSourceTenantNId: string | null
  description: string | null
}

export interface MetadataAttribute extends MetadataAttributeWrite {
  id: string
  wasPublished: boolean
  isFrozen: boolean
  isLocked: boolean
}

export type RuntimeMetadataAttribute = MetadataAttributeWrite

export interface MetadataSchemaSummary {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  attributeCount: number
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  publishedOn: string | null
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}

export interface MetadataSchema extends Omit<MetadataSchemaSummary, 'attributeCount'> {
  attributes: MetadataAttribute[]
}

export interface CreateMetadataSchema {
  scopeType: ReferenceScope
  nId: string
  name: string
  description: string | null
  attributes: MetadataAttributeWrite[]
}

export interface UpdateMetadataSchema extends VersionRequest {
  name: string
  description: string | null
  attributes: MetadataAttributeWrite[]
}

export interface EffectiveMetadataSchema {
  nId: string
  name: string
  description: string | null
  attributes: RuntimeMetadataAttribute[]
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  publishedOn: string
}

export interface MetadataPublicationIssue {
  code: string
  field: string | null
}

export interface MetadataCompatibilityChange {
  attributeNId: string
  code: string
}

export interface MetadataPublicationCheck {
  previousRevision: number | null
  addedAttributes: string[]
  tightenedAttributes: string[]
  relaxedAttributes: string[]
  disabledAttributes: string[]
  incompatibleChanges: MetadataCompatibilityChange[]
  errors: MetadataPublicationIssue[]
}
