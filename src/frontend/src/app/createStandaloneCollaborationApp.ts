import ElementPlus from 'element-plus'
import { createPinia } from 'pinia'
import { createApp, type App as VueApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'

import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/base.css'

import CollaborationPage from '@/pages/pc/collaboration/CollaborationPage.vue'
import EmbeddedSessionRequiredPage from '@/pages/public/EmbeddedSessionRequiredPage.vue'
import { ROUTE_NAMES } from '@/router/routeNames'
import { platformI18n } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'
import { createCollaborationRuntimePlugin } from '@/systemData/runtime/collaborationRuntime'
import StandaloneCollaborationApp from './StandaloneCollaborationApp.vue'
import { installEmbeddedCollaboration } from './installEmbeddedCollaboration'

/** 独立构建只登记协作页面，不装配平台壳、菜单及管理路由。 */
export function createStandaloneCollaborationApp(): VueApp {
  const app = createApp(StandaloneCollaborationApp)
  const pinia = createPinia()
  app.use(pinia)
  app.use(ElementPlus)
  app.use(platformI18n)
  useLocalizationStore(pinia).initialize()

  const router = createRouter({
    history: createWebHistory(import.meta.env.BASE_URL),
    routes: [
      { path: '/pc/collaboration', name: ROUTE_NAMES.collaborationChat, component: CollaborationPage },
      { path: '/pc/collaboration/conversations/:conversationNId', name: ROUTE_NAMES.collaborationConversation, component: CollaborationPage },
      { path: '/embedded/session-required', name: ROUTE_NAMES.embeddedSessionRequired, component: EmbeddedSessionRequiredPage },
    ],
  })
  app.use(router)
  installEmbeddedCollaboration(pinia, router)
  app.use(createCollaborationRuntimePlugin(pinia, { router }))
  return app
}
