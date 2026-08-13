/** Identity SSO 端点统一出口。 */

export { createIdentitySsoApi } from './ssoApi'
export type { IdentitySsoApi, SsoAuthorizeParams } from './ssoApi'
export { getSsoApi, registerSsoApi } from './ssoRegistry'
export type {
  SsoBeginResponseDto,
  SsoDiscoveryProviderDto,
  SsoExchangeRequestDto,
  SsoExchangeResponseDto,
  SsoLogoutRequestDto,
  SsoLogoutResponseDto,
} from './types'
