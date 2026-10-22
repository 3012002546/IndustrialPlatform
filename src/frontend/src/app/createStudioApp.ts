import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import VxeUITable from 'vxe-table'
import { createRouter, createWebHistory } from 'vue-router'
import 'element-plus/dist/index.css'
import 'vxe-table/lib/style.css'
import '@/styles/tokens.css'
import '@/styles/base.css'
import StudioApp from './StudioApp.vue'
import { studioRoutes } from '@/router/studioRoutes'
import { createHttpClient } from '@/api/httpClient'
import { createStudioApi, registerStudioApi } from '@/api/studio'
import { studioCookieClient } from './studioSession'
import { platformI18n } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useThemeStore } from '@/stores/themeStore'
export function createStudioApp() {
  const app = createApp(StudioApp),
    pinia = createPinia()
  app.use(pinia).use(ElementPlus).use(VxeUITable).use(platformI18n)
  useLocalizationStore(pinia).initialize()
  void useThemeStore(pinia).initialize()
  const base = import.meta.env.VITE_API_BASE_URL || ''
  const client = studioCookieClient(
    createHttpClient({ baseUrl: base, timeoutMs: 30000, withCredentials: true }),
  )
  registerStudioApi(createStudioApi(client))
  const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [
      { path: '/', redirect: '/pc/studio/low-code' },
      ...studioRoutes.map((route) => ({ ...route, path: '/pc/' + route.path })),
      { path: '/:pathMatch(.*)*', redirect: '/pc/studio/low-code' },
    ],
  })
  app.use(router)
  return app
}
