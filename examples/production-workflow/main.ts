import { createApp } from 'vue'
import { createI18n } from 'vue-i18n'
import { ElAlert, ElButton, ElButtonGroup } from 'element-plus'
import 'element-plus/theme-chalk/base.css'
import 'element-plus/theme-chalk/el-alert.css'
import 'element-plus/theme-chalk/el-button.css'
import 'element-plus/theme-chalk/el-button-group.css'
import App from './App.vue'

const app = createApp(App)
app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: {} }))
app.use(ElAlert).use(ElButton).use(ElButtonGroup)
app.mount('#app')
