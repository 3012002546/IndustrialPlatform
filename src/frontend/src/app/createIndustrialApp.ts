import ElementPlus from 'element-plus'
import { createPinia } from 'pinia'
import { createApp, type App as VueApp, type Component, type Plugin } from 'vue'

import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/base.css'

import App from '@/App.vue'
import { createMockAuthGateway, setAuthGateway } from '@/auth'
import { loadRuntimeConfig, RuntimeConfigError } from '@/config/runtimeConfig'
import { createAppRouter } from '@/router'

export interface IndustrialAppOptions {
  /** 根组件(默认应用 App.vue);测试可注入轻量根组件。 */
  rootComponent?: Component
  /** 应用级插件,在 Router 之后按序安装(如 Gateway 等)。 */
  plugins?: readonly Plugin[]
}

/** 按运行配置装配认证网关;生产 + mock 已在 loadRuntimeConfig 抛错,不静默切换。 */
function installAuthGateway(): void {
  const config = loadRuntimeConfig()
  if (config.authMode === 'http') {
    throw new RuntimeConfigError('VITE_AUTH_MODE=http 需要 HttpAuthGateway(Phase 3 接入)')
  }
  setAuthGateway(createMockAuthGateway({ delayMs: 200 }))
}

/**
 * 统一应用创建入口。
 *
 * 生产入口与测试必须使用同一个工厂,避免测试装配与真实运行不一致。
 * 装配顺序:Pinia + Element Plus → 认证网关 → Router(全局守卫依赖 Pinia)。
 */
export function createIndustrialApp(options: IndustrialAppOptions = {}): VueApp {
  const app = createApp(options.rootComponent ?? App)

  app.use(createPinia())
  app.use(ElementPlus)

  installAuthGateway()

  app.use(createAppRouter())

  for (const plugin of options.plugins ?? []) {
    app.use(plugin)
  }

  return app
}
