import ElementPlus from 'element-plus'
import VxeUITable from 'vxe-table'
import { createPinia, type Pinia } from 'pinia'
import { createApp, type App as VueApp, type Component, type Plugin } from 'vue'
import type { Router } from 'vue-router'

import 'element-plus/dist/index.css'
import 'vxe-table/lib/style.css'
import '@/styles/tokens.css'
import '@/styles/base.css'

import App from '@/App.vue'
import { createHttpClient, type HttpAuthRefresh } from '@/api/httpClient'
import { createIdentityAuthApi } from '@/api/identity/identityApi'
import { createIdentityManagementApi } from '@/api/identity/management'
import { registerManagementApi } from '@/api/identity/managementRegistry'
import { createIdentitySsoApi, registerSsoApi } from '@/api/identity/sso'
import {
  createIdentitySsoManagementApi,
  registerSsoManagementApi,
} from '@/api/identity/ssoManagement'
import {
  createHttpAuthGateway,
  createMockAuthGateway,
  getCurrentSession,
  setAuthGateway,
} from '@/auth'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { platformI18n } from '@/localization/i18n'
import { createAppRouter } from '@/router'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'
import {
  createSystemDataManagementApi,
  createSystemDataRuntimeApi,
  registerSystemDataManagementApi,
  registerSystemDataRuntimeApi,
} from '@/api/systemData'
import { createSystemDataRuntimePlugin } from '@/systemData/runtime/coordinator'
import { createSystemDataTenantUiDefaultsSource } from '@/systemData/runtime/themeSource'
import { setTenantUiDefaultsSource } from '@/stores/themeStore'

/** 认证专用路径片段:401 不触发刷新重试(登录/刷新/登出),避免无谓循环。 */
const AUTH_ENDPOINT_MARKERS = ['/auth/login', '/auth/refresh', '/auth/logout'] as const

export interface IndustrialAppOptions {
  /** 根组件(默认应用 App.vue);测试可注入轻量根组件。 */
  rootComponent?: Component
  /** 应用级插件,在 Router 之后按序安装(如 Gateway 等)。 */
  plugins?: readonly Plugin[]
}

/**
 * 按运行配置装配认证网关。
 * - http:创建带 401 单飞刷新拦截的真实 HTTP 网关(Gateway 统一入口)。
 * - mock:仅本地开发/测试(生产构建已在 loadRuntimeConfig 抛错,不静默切换)。
 */
function installAuthGateway(pinia: Pinia, router: Router): void {
  const config = loadRuntimeConfig()
  if (config.authMode === 'http') {
    const authRefresh: HttpAuthRefresh = {
      isAuthPath: (path) => AUTH_ENDPOINT_MARKERS.some((marker) => path.includes(marker)),
      refreshSession: () => useAuthStore(pinia).refresh(),
      onSessionExpired: () => {
        // 刷新失败:清理本地会话并回到登录页(尽力而为)。
        void useAuthStore(pinia).logout()
        void router.push({ name: ROUTE_NAMES.login })
      },
    }
    const client = createHttpClient({
      baseUrl: config.apiBaseUrl,
      timeoutMs: config.requestTimeoutMs,
      getToken: () => getCurrentSession()?.accessToken ?? null,
      authRefresh,
      // SSO 浏览器会话 Cookie(§26.4)经网关跨源读写,HttpOnly 句柄不进入前端存储。
      withCredentials: true,
    })
    setAuthGateway(
      createHttpAuthGateway({
        api: createIdentityAuthApi(client),
        getRefreshToken: () => getCurrentSession()?.refreshToken ?? null,
      }),
    )
    // 管理端 API 与认证共用同一 client(令牌注入 + 401 单飞刷新)。
    registerManagementApi(createIdentityManagementApi(client))
    registerSystemDataManagementApi(createSystemDataManagementApi(client))
    const systemDataRuntimeApi = createSystemDataRuntimeApi(client)
    registerSystemDataRuntimeApi(systemDataRuntimeApi)
    setTenantUiDefaultsSource(
      createSystemDataTenantUiDefaultsSource(useSystemDataRuntimeStore(pinia)),
    )
    // SSO 端点与认证/管理共用同一 client(withCredentials 携带 SSO 会话 Cookie)。
    registerSsoApi(createIdentitySsoApi(client))
    // SSO 管理端点(identity.sso.* 权限,共享 client 令牌注入)。
    registerSsoManagementApi(createIdentitySsoManagementApi(client))
    return
  }
  setAuthGateway(createMockAuthGateway({ delayMs: 200 }))
}

/**
 * 统一应用创建入口。
 *
 * 生产入口与测试必须使用同一个工厂,避免测试装配与真实运行不一致。
 * 装配顺序:Pinia + Element Plus → Router → 认证网关(拦截器闭包依赖 Pinia 与 Router)。
 */
export function createIndustrialApp(options: IndustrialAppOptions = {}): VueApp {
  const app = createApp(options.rootComponent ?? App)

  const pinia = createPinia()
  app.use(pinia)
  app.use(ElementPlus)
  app.use(VxeUITable)
  app.use(platformI18n)
  useLocalizationStore(pinia).initialize()

  const router = createAppRouter()
  app.use(router)

  installAuthGateway(pinia, router)
  if (loadRuntimeConfig().authMode === 'http') {
    app.use(createSystemDataRuntimePlugin(pinia))
  }

  for (const plugin of options.plugins ?? []) {
    app.use(plugin)
  }

  return app
}
