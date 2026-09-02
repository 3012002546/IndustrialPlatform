/** Identity SSO 管理端点统一出口。 */

export { createIdentitySsoManagementApi } from './ssoManagementApi'
export type { IdentitySsoManagementApi } from './ssoManagementApi'
export type { SsoExportParams } from './ssoManagementApi'
export { getSsoManagementApi, registerSsoManagementApi } from './ssoManagementRegistry'
export type {
  AddSsoEndpointRequestDto,
  BindSsoAccountRequestDto,
  CreateSsoClientRequestDto,
  CreateSsoProviderRequestDto,
  ExternalAccountSummaryDto,
  ProviderSummaryDto,
  ProviderTestResultDto,
  SetSsoClientEnabledRequestDto,
  SetSsoEndpointEnabledRequestDto,
  SetSsoProviderEnabledRequestDto,
  SsoClientSummaryDto,
  SsoEndpointSummaryDto,
  UpdateSsoClientRequestDto,
  UpdateSsoProviderRequestDto,
  UpdateSsoProviderSecretRequestDto,
} from './types'
