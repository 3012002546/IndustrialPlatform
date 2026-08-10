import { createIndustrialApp } from '@/app/createIndustrialApp'

// main.ts 只负责读取配置、创建和挂载应用;装配细节在 createIndustrialApp()。
// 后续 loadRuntimeConfig()(FE-003)在此工厂之前调用。
createIndustrialApp().mount('#app')
