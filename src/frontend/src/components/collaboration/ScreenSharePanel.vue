<script setup lang="ts">
import { computed } from 'vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'

defineProps<{ conversationNId: string }>()
const auth = useAuthStore()
const media = useCollaborationMediaStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration.media)

function isViewer(): boolean {
  return media.screen?.viewerUserNId === auth.user?.userId
}

function screenStateLabel(): string {
  if (media.screenOperation === 'Error') return copy.value.failed
  if (media.screen?.state === 'Pending') return copy.value.screen
  if (media.screen?.state === 'Sharing' || media.screenOperation === 'Active') return copy.value.screenActive
  if (media.screen?.state === 'Accepted' || media.screen?.state === 'Connecting') {
    const hasLocalCapture = media.localScreenStream?.getVideoTracks().some((track) => track.readyState === 'live') ?? false
    return hasLocalCapture ? copy.value.connecting : copy.value.authorizing
  }
  return copy.value.connecting
}

function respond(answer: string): void {
  if (media.screen) void media.respondScreenShare(media.screen.sessionNId, answer)
}

function startCapture(): void {
  void media.startScreenCapture()
}
</script>

<template>
  <section
    v-if="media.screen?.conversationNId === conversationNId && (media.screen.state === 'Pending' || media.hasScreen)"
    class="collaboration-screen-panel"
    aria-live="polite"
  >
    <div class="collaboration-screen-panel__state">
      <strong>{{ screenStateLabel() }}</strong>
      <span v-if="media.screen.state === 'Pending'">{{ copy.waitingForResponse }}</span>
    </div>
    <div class="collaboration-screen-panel__actions">
      <button
        v-if="media.screen.state === 'Pending' && media.screen.inviteeUserNId === auth.user?.userId"
        type="button"
        @click="respond('Accept')"
      >
        {{ copy.accept }}
      </button>
      <button
        v-if="media.screen.state === 'Pending' && media.screen.inviteeUserNId === auth.user?.userId"
        type="button"
        @click="respond('Decline')"
      >
        {{ copy.decline }}
      </button>
      <button
        v-if="(media.screen.state === 'Accepted' || media.screen.state === 'Connecting') && media.screen.sharerUserNId === auth.user?.userId && !media.localScreenStream?.getVideoTracks().some((track) => track.readyState === 'live')"
        type="button"
        @click="startCapture"
      >
        {{ copy.selectContent }}
      </button>
      <button v-if="media.hasScreen" type="button" @click="media.endScreenShare()">
        {{ isViewer() ? copy.leaveViewing : copy.stopSharing }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.collaboration-screen-panel { display: grid; gap: 0.5rem; padding: 0.65rem; border-bottom: 1px solid var(--color-border, #d9e0e7); background: color-mix(in srgb, var(--color-primary, #2563eb) 7%, transparent); }
.collaboration-screen-panel__state { display: flex; justify-content: space-between; gap: 0.75rem; font-size: 0.82rem; }
.collaboration-screen-panel__actions { display: flex; flex-wrap: wrap; gap: 0.4rem; }
.collaboration-screen-panel button { border: 1px solid var(--color-border, #d9e0e7); border-radius: 0.4rem; padding: 0.3rem 0.55rem; background: var(--color-surface, #fff); cursor: pointer; }
</style>
