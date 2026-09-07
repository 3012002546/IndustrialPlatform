<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import { createNotificationRealtime, type NotificationRealtime } from '@/api/systemData/notificationHub'
import type { NotificationInboxItemDto } from '@/api/systemData/pf04Types'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { platformI18n } from '@/localization/i18n'

const api = getPf04Api()
const router = useRouter()
const authStore = useAuthStore()
const items = ref<NotificationInboxItemDto[]>([])
const loading = ref(false)
const errorMessage = ref('')
const copy = computed(() => {
  const translate = platformI18n.global as unknown as { t: (key: string) => unknown }
  const t = (key: string) => translate.t(`systemData.pages.mobileNotifications.${key}`) as string
  return { title: t('title'), loading: t('loading'), empty: t('empty'), open: t('open'), loadFailed: t('loadFailed'), updateFailed: t('updateFailed') }
})
let pollTimer: number | undefined
let realtime: NotificationRealtime | null = null
async function load(): Promise<void> { if (api === null) return; loading.value = true; try { items.value = (await api.getInbox(1, 50)).items; errorMessage.value = '' } catch (error) { errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed } finally { loading.value = false } }
async function read(item: NotificationInboxItemDto): Promise<void> { if (api !== null && !item.isRead) { try { await api.markRead(item.notificationNId); await load() } catch (error) { errorMessage.value = error instanceof Error ? error.message : copy.value.updateFailed } } }
function openTarget(route: string | null | undefined): void { if (route && route.startsWith('/') && !route.startsWith('//') && !/[\r\n]/.test(route)) void router.push(route) }
onMounted(() => { void load(); realtime = createNotificationRealtime({ getAccessToken: () => authStore.session?.accessToken ?? null, onRefresh: load }); void realtime.start(); pollTimer = window.setInterval(() => { if (document.visibilityState === 'visible') void load() }, 15000) })
onBeforeUnmount(() => { if (pollTimer !== undefined) window.clearInterval(pollTimer); if (realtime !== null) void realtime.stop() })
</script>
<template>
  <main class="mobile-notifications" aria-labelledby="mobile-notifications-title">
    <h1 id="mobile-notifications-title">{{ copy.title }}</h1>
    <p v-if="errorMessage" role="alert" class="mobile-notifications-error">{{ errorMessage }}</p>
    <p v-if="loading">{{ copy.loading }}</p>
    <article v-for="item in items" :key="item.notificationNId" class="mobile-notification-card" :class="{ unread: !item.isRead }" @click="read(item)">
      <h2>{{ item.title }}</h2><p>{{ item.body }}</p><small>{{ new Date(item.deliveredOn).toLocaleString() }}</small><button v-if="item.targetRoute" type="button" @click.stop="openTarget(item.targetRoute)">{{ copy.open }}</button>
    </article>
    <p v-if="!loading && !items.length">{{ copy.empty }}</p>
  </main>
</template>
<style scoped>
.mobile-notifications { padding: var(--ip-space-4); }
.mobile-notifications h1 { margin: 0 0 var(--ip-space-4); font-size: var(--ip-font-size-xl); }
.mobile-notification-card { margin-bottom: var(--ip-space-3); padding: var(--ip-space-4); border: 1px solid var(--ip-color-border); border-radius: var(--ip-radius-md); background: var(--ip-color-bg-container); }
.mobile-notification-card.unread { border-color: var(--ip-color-primary); }
.mobile-notification-card h2 { margin: 0 0 var(--ip-space-2); font-size: var(--ip-font-size-lg); }
.mobile-notification-card p { margin: 0 0 var(--ip-space-2); white-space: pre-wrap; }
</style>
