import ElementPlus from 'element-plus'
import { createPinia } from 'pinia'
import { createApp, type App as VueApp, type Component, type Plugin } from 'vue'

import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/base.css'

import App from '@/App.vue'

export interface IndustrialAppOptions {
  /** 根组件(默认应用 App.vue);测试可注入轻量根组件。 */
  rootComponent?: Component
  /** 应用级插件,在 Pinia / Element Plus 之后按序安装(如 Router 等)。 */
  plugins?: readonly Plugin[]
}

/**
 * 统一应用创建入口。
 *
 * 生产入口与测试必须使用同一个工厂,避免测试装配与真实运行不一致。
 * 第一批装配 Pinia + Element Plus + 全局样式;Router / Gateway 在后续任务接入。
 */
export function createIndustrialApp(options: IndustrialAppOptions = {}): VueApp {
  const app = createApp(options.rootComponent ?? App)

  app.use(createPinia())
  app.use(ElementPlus)

  for (const plugin of options.plugins ?? []) {
    app.use(plugin)
  }

  return app
}
