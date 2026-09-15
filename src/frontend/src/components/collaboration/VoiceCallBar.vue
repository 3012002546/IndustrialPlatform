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

function voiceStateLabel(): string {
  if (media.voiceOperation === 'Error') return copy.value.failed
  if (media.voice?.state === 'Ringing') return copy.value.voice
  if (media.voice?.state === 'Active' || media.voiceOperation === 'Active') return copy.value.voiceActive
  if (media.voice?.state === 'Accepted' || media.voice?.state === 'Connecting') {
    return media.hasLocalVoiceCapture ? copy.value.connecting : copy.value.authorizing
  }
  return copy.value.connecting
}

function respond(answer: string): void {
  if (media.voice) void media.respondVoiceCall(media.voice.callNId, answer)
}

function startCapture(): void {
  void media.startVoiceCapture()
}
</script>

<template>
  <section
    v-if="media.voice?.conversationNId === conversationNId && (media.voice.state === 'Ringing' || media.hasVoice)"
    class="collaboration-voice-bar"
    aria-live="polite"
  >
    <strong>{{ voiceStateLabel() }}</strong>
    <button
      v-if="media.voice.state === 'Ringing' && media.voice.calleeUserNId === auth.user?.userId"
      type="button"
      @click="respond('Accept')"
    >
      {{ copy.accept }}
    </button>
    <button
      v-if="media.voice.state === 'Ringing' && media.voice.calleeUserNId === auth.user?.userId"
      type="button"
      @click="respond('Decline')"
    >
      {{ copy.decline }}
    </button>
    <button v-if="(media.voice.state === 'Accepted' || media.voice.state === 'Connecting') && !media.hasLocalVoiceCapture" type="button" @click="startCapture">
      {{ copy.startMicrophone }}
    </button>
    <button v-if="media.voice.state === 'Active'" type="button" @click="media.setVoiceMuted(!media.muted)">
      {{ media.muted ? copy.unmute : copy.mute }}
    </button>
    <button v-if="media.hasVoice" type="button" @click="media.endVoiceCall()">{{ copy.endVoice }}</button>
  </section>
</template>

<style scoped>
.collaboration-voice-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 0.45rem; padding: 0.55rem 0.65rem; border-bottom: 1px solid var(--color-border, #d9e0e7); background: color-mix(in srgb, #16a34a 7%, transparent); font-size: 0.82rem; }
.collaboration-voice-bar button { border: 1px solid var(--color-border, #d9e0e7); border-radius: 0.4rem; padding: 0.28rem 0.5rem; background: var(--color-surface, #fff); cursor: pointer; }
</style>
