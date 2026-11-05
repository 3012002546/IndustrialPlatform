import { createApp } from 'vue'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import Harness from './StudioGraphHarness.vue'

createApp(Harness)
  .use(createI18n({ legacy: false, locale: 'en-US', messages: {} }))
  .use(ElementPlus)
  .mount('#app')
