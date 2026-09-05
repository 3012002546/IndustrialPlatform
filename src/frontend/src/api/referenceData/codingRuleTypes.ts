import type { PublicationStatus, ReferenceScope, VersionRequest } from './types'

export type CodingResetPolicy = 'Never' | 'Yearly' | 'Monthly' | 'Daily'

export interface CodingRuleSummary {
  id: string
  nId: string
  name: string
  targetEntityNId: string
  template: string
  resetPolicy: CodingResetPolicy
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  publishedOn: string | null
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}

export interface CodingRuleDetail extends CodingRuleSummary {
  createdOn: string
}

export interface CreateCodingRuleRequest {
  scopeType: ReferenceScope
  nId: string
  name: string
  targetEntityNId: string
  template: string
  resetPolicy: CodingResetPolicy
}

export interface UpdateCodingRuleRequest extends VersionRequest {
  name: string
  targetEntityNId: string
  template: string
  resetPolicy: CodingResetPolicy
}

export interface PreviewCodeRequest {
  sourceScope?: ReferenceScope | null
  sourceTenantNId?: string | null
  ruleRevision?: number | null
  factoryId?: string | null
}

export interface GenerateCodeRequest {
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  ruleRevision: number
  factoryId?: string | null
}

export interface CodePreview {
  codingRuleNId: string
  ruleRevision: number
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  code: string
  sampleSequence: number
  periodKey: string
  previewedOn: string
  consumesSequence: false
}

export interface GeneratedCode {
  codingRuleNId: string
  ruleRevision: number
  code: string
  sequence: number
  periodKey: string
  generatedOn: string
}
