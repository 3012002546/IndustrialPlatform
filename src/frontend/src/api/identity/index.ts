/** Identity 认证 + 管理端点统一出口。 */

export { createIdentityAuthApi, IDENTITY_AUTH_PREFIX, type IdentityAuthApi } from './identityApi'
export { mapAuthSession, mapAuthUser } from './mapper'
export { createIdentityManagementApi } from './management'
export type { IdentityManagementApi } from './management'
export { createIdentitySsoApi, getSsoApi, registerSsoApi } from './sso'
export type { IdentitySsoApi, SsoAuthorizeParams } from './sso'
export {
  createIdentitySsoManagementApi,
  getSsoManagementApi,
  registerSsoManagementApi,
} from './ssoManagement'
export type { IdentitySsoManagementApi } from './ssoManagement'
export type {
  SsoBeginResponseDto,
  SsoDiscoveryProviderDto,
  SsoExchangeRequestDto,
  SsoExchangeResponseDto,
  SsoLogoutRequestDto,
  SsoLogoutResponseDto,
} from './sso'
export type {
  IdentityAuthSessionDto,
  IdentityAuthUserDto,
  IdentityLoginRequest,
  IdentityLogoutRequest,
  IdentityRefreshRequest,
} from './types'
