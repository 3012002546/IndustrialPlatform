<script setup lang="ts">
import { computed, onMounted, ref, watch, type DefineComponent } from 'vue'
import { ElConfigProvider } from 'element-plus'
import elementPlusEn from 'element-plus/es/locale/lang/en'
import elementPlusZhCn from 'element-plus/es/locale/lang/zh-cn'
import type { Language } from 'element-plus/es/locale'
import { useRoute } from 'vue-router'

import CollaborationMediaHost from '@/components/collaboration/CollaborationMediaHost.vue'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const route = useRoute()
const auth = useAuthStore()
const localization = useLocalizationStore()
const ready = ref(false)
const entry = new URLSearchParams(window.location.search)
const modes = entry.getAll('mode')
const accounts = entry.getAll('account')
const validEntry = modes.length === 1 && modes[0] === 'standalone'
  && accounts.length === 1 && accounts[0]?.trim() !== ''
  && !entry.has('token') && !entry.has('ticket')
const PlatformConfigProvider = ElConfigProvider as unknown as DefineComponent<{ locale?: Language }>
const elementLocale = computed(() => localization.locale === 'en-US' ? elementPlusEn : elementPlusZhCn)

watch(() => route.name, () => { document.title = '协作聊天' }, { immediate: true })
onMounted(async () => {
  if (validEntry) await auth.restore()
  ready.value = true
})
</script>

<template>
  <PlatformConfigProvider :locale="elementLocale">
    <main class="standalone-collaboration" data-testid="standalone-collaboration-entry">
      <p v-if="!validEntry || route.matched.length === 0" role="alert">独立协作入口地址无效。</p>
      <p v-else-if="!ready" role="status">正在验证 MES 登录…</p>
      <div v-else-if="!auth.isAuthenticated" class="standalone-collaboration__unavailable" role="alert">
        <h1>独立协作会话不可用</h1>
        <p>请检查当前账号、页面地址和独立服务连接后重新打开。</p>
      </div>
      <template v-else>
        <CollaborationMediaHost />
        <RouterView />
      </template>
    </main>
  </PlatformConfigProvider>
</template>

<style scoped>
.standalone-collaboration {
  min-height: 100vh;
  box-sizing: border-box;
  background: var(--ip-color-bg-page);
  color: var(--ip-color-text-primary);
}

.standalone-collaboration__unavailable {
  min-height: 100vh;
  display: grid;
  place-content: center;
  gap: 12px;
  padding: 32px;
}

.standalone-collaboration__unavailable h1,
.standalone-collaboration__unavailable p {
  margin: 0;
}
</style>
