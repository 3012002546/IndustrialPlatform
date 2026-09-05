export type ReferenceScope = 'Tenant' | 'Platform'
export type PublicationStatus = 'Draft' | 'Published' | 'Superseded' | 'Disabled'
export interface VersionRequest {
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
  changeReason?: string | null
}
export interface ReferenceDataQuery {
  pageIndex: number
  pageSize: number
  keyword?: string
  status?: string
  scopeType?: string
  sortField?: string
  descending?: boolean
}
export interface PageResult<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}
export interface DictionaryItem {
  nId: string
  name: string
  description: string | null
  sort: number
  enabled: boolean
}
export interface DictionarySummary {
  id: string
  nId: string
  name: string
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  enabledItemCount: number
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}
export interface DictionaryDetail extends Omit<DictionarySummary, 'enabledItemCount'> {
  description: string | null
  items: DictionaryItem[]
  publishedOn: string | null
}
export interface CreateDictionaryRequest {
  scopeType: ReferenceScope
  scopeId: null
  nId: string
  name: string
  description: string | null
  items: DictionaryItem[]
}
export interface UpdateDictionaryRequest extends VersionRequest {
  name: string
  description: string | null
  items: DictionaryItem[]
}
export interface DictionaryPublicationCheck {
  previousRevision: number | null
  addedItems: string[]
  changedItems: string[]
  disabledItems: string[]
  errors: { code: string; field: string }[]
}
export interface EffectiveDictionary {
  nId: string
  name: string
  items: DictionaryItem[]
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  publishedOn: string
}
