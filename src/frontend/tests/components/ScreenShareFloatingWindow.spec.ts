import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import type { ScreenDto } from '@/api/collaborationMedia'
import ScreenShareFloatingWindow from '@/components/collaboration/ScreenShareFloatingWindow.vue'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'

function makeScreen(): ScreenDto {
  return {
    sessionNId: 'screen-1',
    conversationNId: 'conversation-1',
    direction: 'ShareMine',
    initiatorUserNId: 'user-2',
    inviteeUserNId: 'user-1',
    sharerUserNId: 'user-2',
    viewerUserNId: 'user-1',
    state: 'Sharing',
    answer: 'Accept',
    version: 1,
    deadlineOn: '2026-09-13T00:05:00Z',
    startedOn: '2026-09-13T00:00:00Z',
    endedOn: null,
    endReason: null,
    initiatorStoppedReported: false,
    inviteeStoppedReported: false,
  }
}

describe('ScreenShareFloatingWindow', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore()
    auth.session = {
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresAt: '2099-01-01T00:00:00Z',
      user: {
        userId: 'user-1',
        username: 'viewer',
        displayName: 'Viewer',
        tenantId: 'tenant-1',
        roles: ['user'],
        permissions: [],
        mustChangePassword: false,
      },
    }
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1400 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 900 })
  })

  afterEach(() => {
    document.querySelectorAll('.collaboration-screen-float').forEach((element) => element.remove())
  })

  it('keeps a native resized DOM size when dragging the title bar', async () => {
    const media = useCollaborationMediaStore()
    media.screen = makeScreen()
    const wrapper = mount(ScreenShareFloatingWindow, { global: { plugins: [pinia] } })
    await wrapper.vm.$nextTick()

    const panel = wrapper.get('.collaboration-screen-float')
    const element = panel.element as HTMLElement
    Object.defineProperty(element, 'offsetWidth', { configurable: true, value: 760 })
    Object.defineProperty(element, 'offsetHeight', { configurable: true, value: 480 })
    Object.defineProperty(element, 'getBoundingClientRect', {
      configurable: true,
      value: () => ({ left: 860, top: 350, width: 760, height: 480, right: 1620, bottom: 830, x: 860, y: 350, toJSON: () => ({}) }),
    })

    panel.get('.collaboration-screen-float__header').element.dispatchEvent(new PointerEvent('pointerdown', {
      button: 0,
      clientX: 900,
      clientY: 380,
      bubbles: true,
    }))
    await wrapper.vm.$nextTick()
    expect(panel.attributes('style')).toContain('width: 760px')
    expect(panel.attributes('style')).toContain('height: 480px')

    document.dispatchEvent(new PointerEvent('pointermove', { clientX: 920, clientY: 400 }))
    await wrapper.vm.$nextTick()

    expect(panel.attributes('style')).toContain('width: 760px')
    expect(panel.attributes('style')).toContain('height: 480px')
    expect(panel.attributes('style')).toContain('left: 632px')
    expect(panel.attributes('style')).toContain('top: 370px')

    await wrapper.unmount()
  })
})
