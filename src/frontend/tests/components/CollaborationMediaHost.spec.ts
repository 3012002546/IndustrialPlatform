import { ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

const router = {
  currentRoute: ref({ name: 'collaboration-conversation' }),
}
const realtime = {
  stop: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
}
const media = {
  endAll: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  disposeAll: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  setRealtime: vi.fn(),
  mediaSession: null,
  remoteStream: null,
  screenErrorMessage: null,
  voiceErrorMessage: null,
  errorMessage: null,
  voicePlaybackBlocked: false,
  voice: null,
  screen: null,
  voiceOperation: 'Idle',
  screenOperation: 'Idle',
  hasVoice: false,
  hasScreen: false,
  hasLocalVoiceCapture: false,
  localScreenStream: null,
  mediaConversationNId: null,
  respondVoiceCall: vi.fn(),
  respondScreenShare: vi.fn(),
  endVoiceCall: vi.fn(),
  endScreenShare: vi.fn(),
  resumePlayback: vi.fn(),
  markVoicePlayable: vi.fn(),
  markPlaybackBlocked: vi.fn(),
}
const auth = {
  user: { userId: 'user-a', tenantId: 'tenant-a' },
}

vi.mock('vue-router', () => ({ useRouter: () => router }))
vi.mock('@/api/collaborationHub', () => ({ getCollaborationRealtime: () => realtime }))
vi.mock('@/config/runtimeConfig', () => ({ loadRuntimeConfig: () => ({ authMode: 'embedded' }) }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => auth }))
vi.mock('@/stores/collaborationMediaStore', () => ({ useCollaborationMediaStore: () => media }))
vi.mock('@/localization/localeContext', () => ({ usePlatformLocale: () => ref('zh-CN') }))
vi.mock('@/components/collaboration/ScreenShareFloatingWindow.vue', () => ({ default: { template: '<div />' } }))

import CollaborationMediaHost from '@/components/collaboration/CollaborationMediaHost.vue'

describe('CollaborationMediaHost page lifecycle', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('ends page media and stops realtime when pagehide is dispatched', async () => {
    const wrapper = mount(CollaborationMediaHost)
    await wrapper.vm.$nextTick()

    window.dispatchEvent(new Event('pagehide'))

    expect(media.endAll).toHaveBeenCalledOnce()
    expect(realtime.stop).toHaveBeenCalledOnce()
    wrapper.unmount()
  })
})
