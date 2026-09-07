<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import { createNotificationRealtime, type NotificationRealtime } from '@/api/systemData/notificationHub'
import type { NotificationInboxItemDto } from '@/api/systemData/pf04Types'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { platformI18n } from '@/localization/i18n'
import { PERMISSIONS } from '@/permissions'

const router = useRouter()
const authStore = useAuthStore()
const items = ref<NotificationInboxItemDto[]>([])
const loading = ref(false)
const errorMessage = ref('')
const copy = computed(() => {
  const translate = platformI18n.global as unknown as { t: (key: string) => unknown }
  const t = (key: string) => translate.t(`systemData.pages.mobileNotifications.${key}`) as string
  return { title: t('title'), loading: t('loading'), empty: t('empty'), open: t('open'), markRead: t('markRead'), markAllRead: t('markAllRead'), retry: t('retry'), loadFailed: t('loadFailed'), updateFailed: t('updateFailed') }
})
const canReadInbox = computed(() => authStore.hasPermission(PERMISSIONS.systemDataNotificationInboxRead))
const unsafeRouteCharacterPattern = /[\u0000-\u001f\u007f]/
let pollTimer: number | undefined
let realtime: NotificationRealtime | null = null
async function load(): Promise<void> { const api = getPf04Api(); if (api === null || !canReadInbox.value) { items.value = []; return } loading.value = true; try { items.value = (await api.getInbox(1, 50)).items; errorMessage.value = '' } catch (error) { errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed } finally { loading.value = false } }
async function read(item: NotificationInboxItemDto): Promise<void> { const api = getPf04Api(); if (api !== null && canReadInbox.value && !item.isRead) { try { await api.markRead(item.notificationNId); await load() } catch (error) { errorMessage.value = error instanceof Error ? error.message : copy.value.updateFailed } } }
async function markAllRead(): Promise<void> { const api = getPf04Api(); const ids = items.value.filter((item) => !item.isRead).map((item) => item.notificationNId); if (api !== null && canReadInbox.value && ids.length > 0) { try { await api.batchRead(ids); await load() } catch (error) { errorMessage.value = error instanceof Error ? error.message : copy.value.updateFailed } } }
function safeNotificationRoute(route: string | null | undefined): string | null { if (route === undefined || route === null || !route.startsWith('/') || route.startsWith('//') || unsafeRouteCharacterPattern.test(route)) return null; return route }
function openTarget(route: string | null | undefined): void { const safeRoute = safeNotificationRoute(route); if (safeRoute !== null) void router.push(safeRoute) }
onMounted(() => { void load(); if (!canReadInbox.value) return; realtime = createNotificationRealtime({ getAccessToken: () => authStore.session?.accessToken ?? null, onRefresh: load }); void realtime.start(); pollTimer = window.setInterval(() => { if (document.visibilityState === 'visible') void load() }, 15000) })
onBeforeUnmount(() => { if (pollTimer !== undefined) window.clearInterval(pollTimer); if (realtime !== null) void realtime.stop() })
</script>
<template>
  <main class="mobile-notifications" aria-labelledby="mobile-notifications-title">
    <h1 id="mobile-notifications-title">{{ copy.title }}</h1>
    <p v-if="errorMessage" role="alert" class="mobile-notifications-error">{{ errorMessage }} <button type="button" @click="load">{{ copy.retry }}</button></p>
    <p v-if="loading">{{ copy.loading }}</p>
    <button v-if="items.some((item) => !item.isRead)" type="button" @click="markAllRead">{{ copy.markAllRead }}</button>
    <article v-for="item in items" :key="item.notificationNId" class="mobile-notification-card" :class="{ unread: !item.isRead }">
      <h2>{{ item.title }}</h2><p>{{ item.body }}</p><small>{{ new Date(item.deliveredOn).toLocaleString() }}</small><button v-if="!item.isRead" type="button" @click="read(item)">{{ copy.markRead }}</button><button v-if="safeNotificationRoute(item.targetRoute) !== null" type="button" @click="openTarget(item.targetRoute)">{{ copy.open }}</button>
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
