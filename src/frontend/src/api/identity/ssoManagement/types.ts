/**
 * SSO 管理端点线上契约(TASK-ID-015,§26.7/§26.8):
 * 全 camelCase;标识一律 NId;双版本(optimisticVersion/concurrencyVersion)供乐观并发回传。
 * 密钥只更新引用(配置节键名),摘要仅暴露 hasSecretReference,绝不回显键名或明文。
 */

// ---------------------------------------------------------------------------
// 企业登录源
// ---------------------------------------------------------------------------

export interface ProviderSummaryDto {
  providerNId: string
  name: string
  /** Oidc | Saml2(SsoProtocol 枚举名)。 */
  protocol: string
  authorityOrMetadataUrl: string
  clientIdOrEntityId: string
  /** 只暴露是否已配置密钥引用,不暴露键名。 */
  hasSecretReference: boolean
  callbackPath: string
  enabled: boolean
  autoRedirect: boolean
  /** Manual | JustInTime(SsoProvisioningMode 枚举名)。 */
  provisioningMode: string
  /** Local | Federated(SsoLogoutMode 枚举名)。 */
  logoutMode: string
  allowedEmailDomains: string[]
  jitDefaultRoleNIds: string[]
  createdOn: string
  lastUpdatedOn: string
  optimisticVersion: number
  concurrencyVersion: string
}

export interface CreateSsoProviderRequestDto {
  name?: string | undefined
  protocol?: string | undefined
  authorityOrMetadataUrl?: string | undefined
  clientIdOrEntityId?: string | undefined
  secretOrCertificateReference?: string | undefined
  callbackPath?: string | undefined
  autoRedirect: boolean
  provisioningMode?: string | undefined
  logoutMode?: string | undefined
  allowedEmailDomains?: string[] | undefined
  jitDefaultRoleNIds?: string[] | undefined
}

export interface UpdateSsoProviderRequestDto {
  name?: string | undefined
  protocol?: string | undefined
  authorityOrMetadataUrl?: string | undefined
  clientIdOrEntityId?: string | undefined
  callbackPath?: string | undefined
  autoRedirect: boolean
  provisioningMode?: string | undefined
  logoutMode?: string | undefined
  allowedEmailDomains?: string[] | undefined
  jitDefaultRoleNIds?: string[] | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 密钥引用更新:只写配置节键名,不接收明文。 */
export interface UpdateSsoProviderSecretRequestDto {
  secretOrCertificateReference?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface SetSsoProviderEnabledRequestDto {
  enabled: boolean
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface ProviderTestResultDto {
  reachable: boolean
  message: string
}

// ---------------------------------------------------------------------------
// 外部账号
// ---------------------------------------------------------------------------

export interface BindSsoAccountRequestDto {
  userNId?: string | undefined
  /** IdP 侧主体标识,仅管理员录入,不回显。 */
  externalSubject?: string | undefined
  externalName?: string | undefined
  externalEmail?: string | undefined
}

/** 外部账号摘要(不暴露 external subject)。 */
export interface ExternalAccountSummaryDto {
  accountNId: string
  providerNId: string
  userNId: string
  userLoginName: string
  userName: string
  externalName: string | null
  externalEmail: string | null
  lastLoginOn: string | null
  optimisticVersion: number
  concurrencyVersion: string
}

// ---------------------------------------------------------------------------
// 平台 SSO Client 与端点
// ---------------------------------------------------------------------------

export interface CreateSsoClientRequestDto {
  name?: string | undefined
  oauthClientId?: string | undefined
}

export interface UpdateSsoClientRequestDto {
  name?: string | undefined
  oauthClientId?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface SetSsoClientEnabledRequestDto {
  enabled: boolean
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

/** 登记端点:Type 为 Redirect | PostLogoutRedirect | Origin(SsoClientEndpointType 枚举名)。 */
export interface AddSsoEndpointRequestDto {
  nId?: string | undefined
  type?: string | undefined
  uri?: string | undefined
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface SetSsoEndpointEnabledRequestDto {
  enabled: boolean
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface SsoEndpointSummaryDto {
  endpointNId: string
  type: string
  uri: string
  enabled: boolean
}

export interface SsoClientSummaryDto {
  clientNId: string
  name: string
  oauthClientId: string
  enabled: boolean
  endpoints: SsoEndpointSummaryDto[]
  createdOn: string
  lastUpdatedOn: string
  optimisticVersion: number
  concurrencyVersion: string
}
