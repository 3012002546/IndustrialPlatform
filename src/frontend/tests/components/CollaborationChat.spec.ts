import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, nextTick, reactive } from 'vue'

import type { CollaborationRealtimeHandlers } from '@/api/collaborationHub'
import type { CollaborationApi, ConversationSummary, Message } from '@/api/collaboration'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationChatStore } from '@/stores/collaborationChatStore'
import CollaborationChat from '@/components/collaboration/CollaborationChat.vue'
import collaborationChatSource from '@/components/collaboration/CollaborationChat.vue?raw'
import { createCollaborationRuntimePlugin } from '@/systemData/runtime/collaborationRuntime'

const mocks = vi.hoisted(() => {
  const state: { handlers?: CollaborationRealtimeHandlers } = {}
  return {
    state,
    route: { params: { conversationNId: 'C-1' as string | undefined } },
    push: vi.fn(),
    api: {
      listConversations: vi.fn(),
      getMessages: vi.fn(),
      markRead: vi.fn(),
      setPresence: vi.fn(),
      getConversation: vi.fn(),
      setPersonalMessageVisibility: vi.fn(),
      retractMessage: vi.fn(),
    },
    realtime: {
      connection: { state: 'Connected' },
      status: 'Connected',
      lastStartError: null,
      start: vi.fn(async () => undefined),
      stop: vi.fn(async () => undefined),
      setPresence: vi.fn(async () => ({ userNId: 'U-1', state: 'Online' })),
      joinConversation: vi.fn(async () => undefined),
      leaveConversation: vi.fn(async () => undefined),
      subscribe: vi.fn((handlers: CollaborationRealtimeHandlers) => {
        state.handlers = handlers
        return vi.fn()
      }),
      sendMessage: vi.fn(),
    },
  }
})

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => ({ push: mocks.push }),
}))

vi.mock('@/api/collaborationRegistry', () => ({
  getCollaborationApi: () => mocks.api as unknown as CollaborationApi,
}))

vi.mock('@/api/collaborationHub', () => ({
  getCollaborationRealtime: () => mocks.realtime,
}))

const PageStub = defineComponent({ template: '<div><slot /></div>' })
const PermissionGateStub = defineComponent({ template: '<div><slot /></div>' })
const ButtonStub = defineComponent({ template: '<button><slot /></button>' })

const conversation: ConversationSummary = {
  conversationNId: 'C-1',
  peerUserNId: 'U-2',
  peerDisplayName: 'Peer',
  status: 'Active',
  lastMessageSequence: 1,
  lastMessageNId: 'M-1',
  lastMessageOn: '2026-09-10T00:00:00.000Z',
  unreadCount: 1,
  visibilityState: 'Visible',
  optimisticVersion: 1,
  concurrencyVersion: 'conversation-v1',
  projectionVersion: 1,
}

const message: Message = {
  messageNId: 'M-1',
  conversationNId: 'C-1',
  sequence: 1,
  senderUserNId: 'U-2',
  senderDisplayName: 'Peer',
  clientMessageNId: '',
  messageType: 'Text',
  textContent: 'hello',
  replyToMessageNId: null,
  attachment: null,
  acceptedOn: '2026-09-10T00:00:00.000Z',
  retractedOn: null,
  retractionReason: null,
  state: 'Accepted',
  messageStateVersion: 1,
}

function makeSession(userId = 'U-1', accessToken = 'access-token') {
  return {
    accessToken,
    refreshToken: 'refresh-token',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: {
      userId,
      username: 'sender',
      displayName: 'Sender',
      tenantId: 'T-1',
      roles: ['user'],
      permissions: [
        PERMISSIONS.collaborationMessagingRead,
        PERMISSIONS.collaborationMessagingWrite,
      ],
      mustChangePassword: false,
    },
  }
}

function messagesPage() {
  return {
    items: [message],
    highWatermarkSequence: 1,
    retentionFloorSequence: 0,
    earliestAvailableSequence: 1,
    nextCursor: null,
    nextAfterSequence: null,
    hasMore: false,
  }
}

describe('CollaborationChat read cursor convergence', () => {
  let wrapper: VueWrapper | undefined
  let wrappers: VueWrapper[]
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    wrappers = []
    pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(makeSession())
    delete mocks.state.handlers
    mocks.route = reactive({ params: { conversationNId: 'C-1' as string | undefined } })
    mocks.push.mockImplementation(async (target: { params?: { conversationNId?: string } }) => {
      mocks.route.params.conversationNId = target.params?.conversationNId
    })
    mocks.realtime.connection.state = 'Connected'
    vi.spyOn(document, 'hasFocus').mockReturnValue(true)
    mocks.api.listConversations.mockResolvedValue({
      items: [conversation],
      nextCursor: null,
      total: 1,
    })
    mocks.api.getMessages.mockResolvedValue(messagesPage())
    mocks.api.markRead.mockResolvedValue({
      lastReadSequence: 1,
      unreadCount: 0,
      projectionVersion: 2,
      concurrencyVersion: 'member-v2',
    })
    mocks.api.getConversation.mockResolvedValue({
      conversationNId: 'C-1',
      status: 'Active',
      currentMember: {
        userNId: 'U-1',
        displayName: 'Sender',
        visibilityState: 'Visible',
        joinedOn: '2026-09-10T00:00:00.000Z',
        lastReadSequence: 1,
        unreadCount: 0,
      },
      peerMember: {
        userNId: 'U-2',
        displayName: 'Peer',
        visibilityState: 'Visible',
        joinedOn: '2026-09-10T00:00:00.000Z',
        lastReadSequence: 0,
        unreadCount: 0,
      },
      lastMessageSequence: 1,
      retentionFloorSequence: 0,
      optimisticVersion: 1,
      concurrencyVersion: 'conversation-v1',
    })
    vi.clearAllMocks()
  })

  afterEach(() => {
    for (const mounted of wrappers) mounted.unmount()
    wrapper = undefined
    sessionStorage.clear()
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  function mountChat(
    surface: 'page' | 'drawer' = 'page',
    active = true,
    terminal?: 'pda' | 'mobile',
  ): VueWrapper {
    wrapper = mount(CollaborationChat, {
      attachTo: document.body,
      props: { surface, active, ...(terminal ? { terminal } : {}) },
      global: {
        plugins: [pinia],
        stubs: {
          AppPage: PageStub,
          AppQueryPanel: false,
          PermissionGate: PermissionGateStub,
          ElButton: ButtonStub,
        },
      },
    })
    wrappers.push(wrapper)
    return wrapper
  }

  it.each(['page', 'drawer'] as const)(
    'can repeatedly collapse and reopen filters on %s without losing the conversation',
    async (surface) => {
      const mounted = mountChat(surface)
      await flushPromises()
      await mounted.get('.collaboration-chat__filter').setValue('Peer')
      const toggle = mounted.get('[data-testid="query-panel-toggle"]')
      for (let index = 0; index < 3; index += 1) {
        await toggle.trigger('click')
        expect(toggle.attributes('aria-expanded')).toBe('false')
        expect(toggle.text()).toBe('展开')
        expect(toggle.isVisible()).toBe(true)
        expect(mounted.get('.app-query-panel__body').isVisible()).toBe(false)
        expect(mounted.get('.collaboration-chat__conversation-list').isVisible()).toBe(true)
        expect(mounted.get('.collaboration-chat__conversation-header').text()).toContain('Peer')
        await toggle.trigger('click')
        expect(toggle.attributes('aria-expanded')).toBe('true')
        expect(mounted.get('.app-query-panel__body').isVisible()).toBe(true)
        expect((mounted.get('.collaboration-chat__filter').element as HTMLInputElement).value).toBe(
          'Peer',
        )
      }
    },
  )

  it('starts narrow-screen filters collapsed while keeping the expand control and conversation list available', async () => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({ matches: true })),
    )
    const mounted = mountChat()
    await flushPromises()
    const toggle = mounted.get('[data-testid="query-panel-toggle"]')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(toggle.isVisible()).toBe(true)
    expect(mounted.get('.collaboration-chat__conversation-list').isVisible()).toBe(true)
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(mounted.get('.app-query-panel__body').isVisible()).toBe(true)
  })

  it.each(['pda', 'mobile'] as const)(
    '%s opens only the list, navigates to a full conversation and returns without losing its draft or marking hidden messages read',
    async (terminal) => {
      mocks.route.params.conversationNId = undefined
      const mounted = mountChat('page', true, terminal)
      await flushPromises()
      expect(mounted.get('.collaboration-chat__sidebar').isVisible()).toBe(true)
      expect(mounted.get('.collaboration-chat__main').isVisible()).toBe(false)
      expect(mocks.api.markRead).not.toHaveBeenCalled()
      await mounted.get('.collaboration-chat__conversation').trigger('click')
      await flushPromises()
      expect(mocks.push).toHaveBeenCalledWith({
        name: `${terminal}-collaboration-conversation`,
        params: { conversationNId: 'C-1' },
      })
      expect(mounted.get('.collaboration-chat__sidebar').isVisible()).toBe(false)
      expect(mounted.get('.collaboration-chat__main').isVisible()).toBe(true)
      await mounted.get('textarea').setValue('unfinished draft')
      await mounted.get('.collaboration-chat__back').trigger('click')
      await flushPromises()
      expect(mocks.push).toHaveBeenLastCalledWith({ name: `${terminal}-collaboration-chat` })
      expect(mounted.get('.collaboration-chat__sidebar').isVisible()).toBe(true)
      expect(mounted.get('.collaboration-chat__main').isVisible()).toBe(false)
      mocks.api.markRead.mockClear()
      mocks.state.handlers?.onMessage?.({ ...message, messageNId: 'M-new', sequence: 2 })
      await flushPromises()
      expect(mocks.api.markRead).not.toHaveBeenCalled()
      await mounted.get('.collaboration-chat__conversation').trigger('click')
      await flushPromises()
      expect(mounted.get('.collaboration-chat__main').isVisible()).toBe(true)
      expect((mounted.get('textarea').element as HTMLTextAreaElement).value).toBe(
        'unfinished draft',
      )
    },
  )

  it('applies the successful read-cursor response to the active conversation and total', async () => {
    const mounted = mountChat()
    await flushPromises()

    expect(mounted.get('.collaboration-chat__unread').text()).toContain('0')
    expect(mounted.find('.collaboration-chat__badge').exists()).toBe(false)
    expect(mocks.api.markRead).toHaveBeenCalledWith('C-1', 1)
  })

  it('does not send or consume scanner Enter on PDA and still sends through the explicit button', async () => {
    mocks.realtime.sendMessage.mockResolvedValue({ ...message, senderUserNId: 'U-1' })
    const mounted = mountChat('page', true, 'pda')
    await flushPromises()
    const composer = mounted.get('textarea')
    await composer.setValue('SCANNED-001')
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    composer.element.dispatchEvent(enter)
    await flushPromises()
    expect(enter.defaultPrevented).toBe(false)
    expect(mocks.realtime.sendMessage).not.toHaveBeenCalled()
    expect(composer.element.value).toBe('SCANNED-001')
    expect(composer.attributes('placeholder')).toBe('输入消息，回车换行，点击发送')
    await mounted.get('.collaboration-chat__composer-actions > button').trigger('click')
    await flushPromises()
    expect(mocks.realtime.sendMessage).toHaveBeenCalledTimes(1)
  })

  it.each([undefined, 'mobile'] as const)(
    'preserves Enter sending outside PDA (%s)',
    async (terminal) => {
      mocks.realtime.sendMessage.mockResolvedValue({ ...message, senderUserNId: 'U-1' })
      const mounted = mountChat('page', true, terminal)
      await flushPromises()
      const composer = mounted.get('textarea')
      await composer.setValue('typed message')
      await composer.trigger('keydown', { key: 'Enter', shiftKey: true })
      await composer.trigger('keydown', { key: 'Enter', isComposing: true })
      expect(mocks.realtime.sendMessage).not.toHaveBeenCalled()
      await composer.trigger('keydown', { key: 'Enter' })
      await flushPromises()
      expect(mocks.realtime.sendMessage).toHaveBeenCalledTimes(1)
    },
  )

  it('ignores an old read event and another user while accepting a newer own cursor', async () => {
    const mounted = mountChat()
    mocks.api.markRead.mockResolvedValueOnce({
      lastReadSequence: 0,
      unreadCount: 1,
      projectionVersion: 3,
      concurrencyVersion: 'member-v3',
    })
    await flushPromises()

    const onReadCursor = mocks.state.handlers?.onReadCursor
    expect(onReadCursor).toBeDefined()
    onReadCursor?.({
      conversationNId: 'C-1',
      userNId: 'U-1',
      sequence: 1,
      projectionVersion: 4,
    })
    await nextTick()
    expect(mounted.find('.collaboration-chat__badge').exists()).toBe(false)

    onReadCursor?.({
      conversationNId: 'C-1',
      userNId: 'U-1',
      sequence: 0,
      projectionVersion: 3,
    })
    onReadCursor?.({
      conversationNId: 'C-1',
      userNId: 'U-2',
      sequence: 0,
      projectionVersion: 1,
    })
    await nextTick()
    expect(mounted.get('.collaboration-chat__unread').text()).toContain('0')
    expect(mounted.find('.collaboration-chat__badge').exists()).toBe(false)
  })

  it('renders a local pending message and merges the hub acknowledgement without reloading history or conversations', async () => {
    mocks.realtime.sendMessage.mockResolvedValue({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      clientMessageNId: 'client-2',
      textContent: 'optimistic',
    })
    const mounted = mountChat()
    await flushPromises()

    const composer = mounted.get('textarea')
    await composer.setValue('optimistic')
    await mounted.get('.collaboration-chat__composer-actions > button').trigger('click')
    await flushPromises()

    expect(composer.element.value).toBe('')
    expect(mounted.text()).toContain('optimistic')
    expect(mocks.api.getMessages).toHaveBeenCalledTimes(1)
    expect(mocks.api.listConversations).toHaveBeenCalledTimes(1)
  })

  it('merges a realtime message into the active conversation without refetching conversation summaries', async () => {
    const mounted = mountChat()
    await flushPromises()

    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the application realtime message handler.')
    onMessage({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      textContent: 'realtime update',
    })
    await nextTick()

    expect(mounted.text()).toContain('realtime update')
    expect(mocks.api.listConversations).toHaveBeenCalledTimes(1)
  })

  it('keeps the latest preview and scroll position when an older message is retracted, then shows a tombstone for the latest retraction', async () => {
    const mounted = mountChat()
    await flushPromises()
    const onMessage = mocks.state.handlers?.onMessage
    const onRetraction = mocks.state.handlers?.onMessageRetracted
    if (!onMessage || !onRetraction) throw new Error('Expected the realtime handlers.')
    onMessage({ ...message, messageNId: 'M-2', sequence: 2, textContent: 'latest text' })
    await flushPromises()

    const scroll = mounted.get('.collaboration-chat__messages').element as HTMLElement
    Object.defineProperty(scroll, 'scrollHeight', { configurable: true, value: 320 })
    Object.defineProperty(scroll, 'clientHeight', { configurable: true, value: 120 })
    Object.defineProperty(scroll, 'scrollTop', { configurable: true, writable: true, value: 80 })
    await mounted.get('.collaboration-chat__messages').trigger('scroll')
    onRetraction({ ...message, state: 'Retracted', textContent: null })
    await flushPromises()

    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('latest text')
    expect(scroll.scrollTop).toBe(80)

    onRetraction({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      state: 'Retracted',
      textContent: null,
      messageStateVersion: 2,
    })
    await flushPromises()

    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('消息已撤回')
  })

  it('keeps same-sequence summary state monotonic across late retraction and personal-hide events', async () => {
    const mounted = mountChat()
    await flushPromises()
    const onMessage = mocks.state.handlers?.onMessage
    const onRetraction = mocks.state.handlers?.onMessageRetracted
    const onHidden = mocks.state.handlers?.onPersonalMessageHidden
    if (!onMessage || !onRetraction || !onHidden) throw new Error('Expected the realtime handlers.')

    onMessage({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      textContent: 'original summary',
      messageStateVersion: 1,
    })
    onMessage({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      textContent: 'updated summary',
      messageStateVersion: 2,
    })
    await flushPromises()
    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('updated summary')

    onRetraction({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      state: 'Retracted',
      textContent: null,
      messageStateVersion: 3,
    })
    onMessage({
      ...message,
      messageNId: 'M-2',
      sequence: 2,
      textContent: 'late original summary',
      messageStateVersion: 2,
    })
    await flushPromises()
    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('消息已撤回')

    let resolveSafeProjection:
      | ((value: { items: ConversationSummary[]; nextCursor: null; total: number }) => void)
      | undefined
    mocks.api.listConversations.mockImplementationOnce(
      () =>
        new Promise<{ items: ConversationSummary[]; nextCursor: null; total: number }>(
          (resolve) => {
            resolveSafeProjection = resolve
          },
        ),
    )
    onMessage({
      ...message,
      messageNId: 'M-hidden',
      sequence: 3,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      textContent: 'hidden summary',
      messageStateVersion: 1,
    })
    onHidden({ conversationNId: 'C-1', messageNId: 'M-hidden' })
    onMessage({
      ...message,
      messageNId: 'M-hidden',
      sequence: 3,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      textContent: 'late hidden summary',
      messageStateVersion: 2,
    })
    await nextTick()
    expect(mounted.text()).not.toContain('late hidden summary')

    resolveSafeProjection?.({
      items: [
        {
          ...conversation,
          lastMessagePreview: {
            messageNId: 'M-1',
            sequence: 1,
            acceptedOn: message.acceptedOn,
            messageType: 'Text',
            state: 'Accepted',
            text: 'hello',
          },
        },
      ],
      nextCursor: null,
      total: 1,
    })
    await flushPromises()
    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('hello')
  })

  it('redacts a hidden latest preview immediately and reconciles the safe previous preview for realtime and own-message deletion', async () => {
    const safePrevious = {
      ...conversation,
      lastMessagePreview: {
        messageNId: 'M-1',
        sequence: 1,
        acceptedOn: message.acceptedOn,
        messageType: 'Text',
        state: 'Accepted',
        text: 'hello',
      },
    }
    const mounted = mountChat()
    await flushPromises()
    const onMessage = mocks.state.handlers?.onMessage
    const onHidden = mocks.state.handlers?.onPersonalMessageHidden
    if (!onMessage || !onHidden) throw new Error('Expected the realtime handlers.')
    onMessage({
      ...message,
      messageNId: 'M-hidden',
      sequence: 2,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      textContent: 'private latest',
    })
    mocks.api.listConversations.mockResolvedValueOnce({
      items: [safePrevious],
      nextCursor: null,
      total: 1,
    })
    onHidden({ conversationNId: 'C-1', messageNId: 'M-hidden' })
    await flushPromises()

    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('hello')
    expect(mounted.text()).not.toContain('private latest')

    onMessage({
      ...message,
      messageNId: 'M-own-hidden',
      sequence: 3,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      textContent: 'delete me',
    })
    await nextTick()
    mocks.api.setPersonalMessageVisibility.mockResolvedValueOnce(undefined)
    mocks.api.listConversations.mockResolvedValueOnce({
      items: [safePrevious],
      nextCursor: null,
      total: 1,
    })
    await mounted.get('.collaboration-chat__message-actions').trigger('contextmenu')
    const deleteAction = mounted
      .get('[role="menu"]')
      .findAll('button')
      .find((button) => button.text() === '仅为我删除')
    if (!deleteAction) throw new Error('Expected the delete-for-me action.')
    await deleteAction.trigger('click')
    await flushPromises()

    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe('hello')
    expect(mounted.text()).not.toContain('delete me')
  })

  it('keeps the context menu inside the viewport by flipping above or below its message trigger', async () => {
    const mounted = mountChat()
    await flushPromises()
    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the realtime message handler.')
    onMessage({
      ...message,
      messageNId: 'M-menu',
      sequence: 2,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
    })
    await nextTick()

    let triggerTop = 2
    const rectangle = (top: number, width: number, height: number) =>
      ({
        x: 0,
        y: top,
        top,
        left: 0,
        right: 900,
        bottom: top + height,
        width,
        height,
        toJSON: () => ({}),
      }) as DOMRect
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
      this: HTMLElement,
    ) {
      if (this.classList.contains('collaboration-chat__message-actions'))
        return rectangle(triggerTop, 100, 20)
      if (this.classList.contains('collaboration-chat__action-menu')) return rectangle(0, 112, 96)
      return rectangle(0, 0, 0)
    })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })

    const actions = mounted.get('.collaboration-chat__message-actions')
    await actions.trigger('contextmenu')
    await nextTick()
    expect(mounted.get('[role="menu"]').attributes('style')).toContain('top: 26px')

    document.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Escape' }))
    triggerTop = 790
    await actions.trigger('keydown', { key: 'ContextMenu' })
    await nextTick()
    const menu = mounted.get('[role="menu"]')
    expect(collaborationChatSource).toMatch(
      /\.collaboration-chat__action-menu\s*\{[\s\S]*?position:\s*fixed;/,
    )
    expect(menu.attributes('style')).toContain('top: 690px')
  })

  it('marks the highest displayed sequence from the right-side header without reading from the left list', async () => {
    const hasFocus = vi.spyOn(document, 'hasFocus')
    hasFocus.mockReturnValue(false)
    const mounted = mountChat()
    await flushPromises()
    mocks.api.markRead.mockClear()
    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the realtime message handler.')
    onMessage({ ...message, messageNId: 'M-header', sequence: 2 })
    await flushPromises()
    expect(mocks.api.markRead).not.toHaveBeenCalled()

    hasFocus.mockReturnValue(true)
    await mounted.get('.collaboration-chat__conversation-header').trigger('pointerdown')
    await flushPromises()
    expect(mocks.api.markRead).toHaveBeenCalledWith('C-1', 2)
  })

  it('renders the server-projected conversation preview without fetching message history per row', async () => {
    mocks.api.listConversations.mockResolvedValueOnce({
      items: [
        {
          ...conversation,
          lastMessagePreview: {
            messageNId: 'M-1',
            sequence: 1,
            acceptedOn: '2026-09-10T00:00:00.000Z',
            messageType: 'Text',
            state: 'Accepted',
            text: 'safe one-line preview',
          },
        },
      ],
      nextCursor: null,
      total: 1,
    })
    const mounted = mountChat()
    await flushPromises()

    expect(mounted.get('.collaboration-chat__conversation-preview').text()).toBe(
      'safe one-line preview',
    )
    expect(mocks.api.getMessages).toHaveBeenCalledTimes(1)
  })

  it('scrolls only the visible surface after a realtime append', async () => {
    const mounted = mountChat()
    await flushPromises()
    const scroll = mounted.get('.collaboration-chat__messages').element as HTMLElement
    Object.defineProperty(scroll, 'scrollHeight', { configurable: true, value: 320 })
    Object.defineProperty(scroll, 'scrollTop', { configurable: true, writable: true, value: 0 })

    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the application realtime message handler.')
    onMessage({ ...message, messageNId: 'M-scroll', sequence: 2, textContent: 'scroll update' })
    await flushPromises()

    expect(scroll.scrollTop).toBe(320)
  })

  it('does not mark read from the obscured page while the quick drawer is open', async () => {
    mountChat()
    await flushPromises()
    mocks.api.markRead.mockClear()
    const chat = useCollaborationChatStore()
    chat.setQuickDrawerOpen(true)
    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the application realtime message handler.')
    onMessage({ ...message, messageNId: 'M-hidden-page', sequence: 2 })
    await flushPromises()

    expect(mocks.api.markRead).not.toHaveBeenCalled()

    chat.setQuickDrawerOpen(false)
    await flushPromises()
    expect(mocks.api.markRead).toHaveBeenCalledWith('C-1', 2)
  })

  it('closes an open own-message menu when Escape is pressed at page level', async () => {
    const mounted = mount(CollaborationChat, {
      global: {
        plugins: [pinia],
        stubs: {
          AppPage: PageStub,
          AppQueryPanel: false,
          PermissionGate: PermissionGateStub,
          ElButton: ButtonStub,
        },
      },
    })
    wrappers.push(mounted)
    await flushPromises()

    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the application realtime message handler.')
    onMessage({
      ...message,
      messageNId: 'M-own',
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      sequence: 2,
    })
    await nextTick()

    await mounted.get('.collaboration-chat__message-actions').trigger('contextmenu')
    expect(mounted.find('[role="menu"]').exists()).toBe(true)

    document.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Escape' }))
    await nextTick()

    expect(mounted.find('[role="menu"]').exists()).toBe(false)
  })

  it('opens the own-message menu by right-clicking its text and closes it on an outside press', async () => {
    const mounted = mountChat()
    await flushPromises()

    const onMessage = mocks.state.handlers?.onMessage
    if (!onMessage) throw new Error('Expected the application realtime message handler.')
    onMessage({ ...message, messageNId: 'M-own-context', senderUserNId: 'U-1', sequence: 2 })
    await nextTick()

    const actions = mounted.get('.collaboration-chat__message-actions')
    expect(actions.find('button').exists()).toBe(false)
    await mounted
      .get('.collaboration-chat__message.is-own .collaboration-chat__message-text')
      .trigger('contextmenu')
    expect(mounted.find('[role="menu"]').exists()).toBe(true)
    expect(mounted.get('[role="menu"]').attributes('style')).toContain('visibility: visible')

    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await nextTick()
    expect(mounted.find('[role="menu"]').exists()).toBe(false)
  })

  it('keeps the composer enabled and preserves new draft text while a delayed send acknowledgement resolves', async () => {
    let accept: ((value: Message) => void) | undefined
    mocks.realtime.sendMessage.mockImplementationOnce(
      () =>
        new Promise<Message>((resolve) => {
          accept = resolve
        }),
    )
    const mounted = mountChat()
    await flushPromises()

    const composer = mounted.get('textarea')
    await composer.setValue('first message')
    await mounted.get('.collaboration-chat__composer-actions > button').trigger('click')
    expect(composer.attributes('disabled')).toBeUndefined()

    await composer.setValue('next draft')
    accept?.({
      ...message,
      messageNId: 'M-ack',
      sequence: 2,
      senderUserNId: 'U-1',
      senderDisplayName: 'Sender',
      clientMessageNId: 'client-ack',
      textContent: 'first message',
    })
    await flushPromises()

    expect((composer.element as HTMLTextAreaElement).value).toBe('next draft')
  })

  it('mounts page and drawer with one application presence heartbeat', async () => {
    vi.useFakeTimers()
    const setIntervalSpy = vi.spyOn(globalThis, 'setInterval')
    createCollaborationRuntimePlugin(pinia).install?.({} as never)

    mountChat('page')
    mountChat('drawer')
    await flushPromises()

    expect(setIntervalSpy).toHaveBeenCalledTimes(1)
    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 20_000)
    expect(mocks.api.listConversations).toHaveBeenCalledTimes(1)
  })

  it('clears A state and stale callbacks before B starts a new chat session', async () => {
    vi.useFakeTimers()
    createCollaborationRuntimePlugin(pinia).install?.({} as never)
    mountChat('page')
    await flushPromises()

    const chat = useCollaborationChatStore()
    chat.messageDraft = 'A private draft'
    expect(chat.conversations).toHaveLength(1)
    expect(chat.messages).toHaveLength(1)
    expect(chat.selectedConversation?.conversationNId).toBe('C-1')
    const chatSubscriptionIndex = mocks.realtime.subscribe.mock.calls.findIndex(
      ([handlers]) => handlers.onMessage !== undefined,
    )
    const unsubscribe = mocks.realtime.subscribe.mock.results[chatSubscriptionIndex]
      ?.value as ReturnType<typeof vi.fn>

    let resolveStaleLoad:
      | ((value: { items: ConversationSummary[]; nextCursor: null; total: number }) => void)
      | undefined
    mocks.api.listConversations.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveStaleLoad = resolve
        }),
    )
    void mocks.state.handlers?.onReconnected?.()
    await flushPromises()
    expect(mocks.api.listConversations).toHaveBeenCalledTimes(2)

    useAuthStore().clearLocalSession()
    await vi.waitFor(() => expect(chat.sessionStarted).toBe(false))
    expect(unsubscribe).toHaveBeenCalledOnce()
    expect(mocks.realtime.leaveConversation).toHaveBeenCalledWith('C-1')
    expect(chat.conversations).toEqual([])
    expect(chat.messages).toEqual([])
    expect(chat.selectedConversation).toBeNull()
    expect(chat.messageDraft).toBe('')

    resolveStaleLoad?.({ items: [conversation], nextCursor: null, total: 1 })
    await flushPromises()
    expect(chat.conversations).toEqual([])
    expect(chat.messages).toEqual([])

    useAuthStore().adoptSession(makeSession('U-3', 'access-token-b'))
    await flushPromises()
    mountChat('page')
    await flushPromises()

    const chatSubscriptions = mocks.realtime.subscribe.mock.calls.filter(
      ([handlers]) => handlers.onMessage !== undefined,
    )
    expect(chatSubscriptions).toHaveLength(2)
    expect(mocks.api.listConversations).toHaveBeenCalledTimes(3)
  })
})
