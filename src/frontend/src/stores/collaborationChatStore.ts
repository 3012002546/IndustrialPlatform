import { computed, reactive, ref } from 'vue'
import { defineStore } from 'pinia'

import type {
  CollaborationDirectoryUser,
  ConversationDetail,
  ConversationSummary,
  Message,
  SendMessageRequest,
} from '@/api/collaboration'

export interface KnownReadCursor {
  sequence: number
  unreadCount?: number
  projectionVersion?: number
}

export const useCollaborationChatStore = defineStore('collaboration-chat', () => {
  const conversations = ref<ConversationSummary[]>([])
  const unreadConversations = ref<ConversationSummary[]>([])
  const selectedConversation = ref<ConversationSummary | null>(null)
  const conversationDetail = ref<ConversationDetail | null>(null)
  const messages = ref<Message[]>([])
  const directory = ref<CollaborationDirectoryUser[]>([])
  const loadingConversations = ref(false)
  const loadingMessages = ref(false)
  const sending = ref(false)
  const directoryLoading = ref(false)
  const errorMessage = ref('')
  const messageError = ref('')
  const directoryKeyword = ref('')
  const conversationKeyword = ref('')
  const messageDraft = ref('')
  const pendingAttachment = ref<{ attachmentNId: string; fileName: string; state: string } | null>(
    null,
  )
  const selectedFile = ref<File | null>(null)
  const uploadState = ref('')
  const detailsOpen = ref(false)
  const showDirectory = ref(false)
  const hiddenOnly = ref(false)
  const unreadOnly = ref(false)
  const historyCursor = ref<string | undefined>()
  const pendingSend = ref<{
    conversationNId: string
    textContent: string
    attachmentNId: string | null
    request: SendMessageRequest
  } | null>(null)
  const draftByConversation = reactive<Record<string, string>>({})
  const knownReadCursors = new Map<string, KnownReadCursor>()
  const peerReadCursors = reactive<Record<string, number>>({})
  const messageScrollTopByConversation = reactive<Record<string, number>>({})
  const conversationsLoaded = ref(false)
  const quickDrawerOpen = ref(false)
  const realtimeMessageEvent = ref<{ conversationNId: string; version: number } | null>(null)
  const sessionStarted = ref(false)
  const sessionVersion = ref(0)
  const totalUnread = computed(() =>
    unreadConversations.value.reduce((total, item) => total + item.unreadCount, 0),
  )
  let unsubscribeRealtime: (() => void) | undefined

  function startSession(unsubscribe: (() => void) | undefined): boolean {
    if (sessionStarted.value) return false
    sessionStarted.value = true
    unsubscribeRealtime = unsubscribe
    return true
  }

  function replaceConversationProjection(items: ConversationSummary[]): void {
    const current = new Map(conversations.value.map((item) => [item.conversationNId, item]))
    conversations.value = items.map(mergeKnownReadCursor).map((incoming) => {
      const previous = current.get(incoming.conversationNId)
      if (
        previous !== undefined &&
        (previous.projectionVersion ?? 0) > (incoming.projectionVersion ?? 0)
      )
        return previous
      return incoming
    })
    const selected = selectedConversation.value
    const refreshed =
      selected &&
      conversations.value.find((item) => item.conversationNId === selected.conversationNId)
    if (refreshed !== undefined) selectedConversation.value = refreshed
    conversationsLoaded.value = true
  }

  function isNewerReadCursor(candidate: KnownReadCursor, previous: KnownReadCursor): boolean {
    if (candidate.projectionVersion !== undefined && previous.projectionVersion !== undefined) {
      if (candidate.projectionVersion < previous.projectionVersion) return false
      if (
        candidate.projectionVersion === previous.projectionVersion &&
        candidate.sequence <= previous.sequence
      )
        return false
      return true
    }
    return candidate.sequence > previous.sequence
  }

  function mergeKnownReadCursor(conversation: ConversationSummary): ConversationSummary {
    const known = knownReadCursors.get(conversation.conversationNId)
    if (
      known === undefined ||
      known.unreadCount === undefined ||
      known.sequence < conversation.lastMessageSequence
    )
      return conversation
    if (
      known.projectionVersion !== undefined &&
      known.projectionVersion < (conversation.projectionVersion ?? 0)
    )
      return conversation
    return {
      ...conversation,
      unreadCount: known.unreadCount,
      ...(known.projectionVersion === undefined
        ? {}
        : {
            projectionVersion: Math.max(
              conversation.projectionVersion ?? 0,
              known.projectionVersion,
            ),
          }),
    }
  }

  function replaceUnreadConversationProjection(items: ConversationSummary[]): void {
    const current = new Map(unreadConversations.value.map((item) => [item.conversationNId, item]))
    unreadConversations.value = items
      .map(mergeKnownReadCursor)
      .map((incoming) => {
        const previous = current.get(incoming.conversationNId)
        if (
          previous !== undefined &&
          (previous.projectionVersion ?? 0) > (incoming.projectionVersion ?? 0)
        )
          return previous
        return incoming
      })
      .filter((item) => item.unreadCount > 0)
  }

  function applyReadCursor(conversationNId: string, cursor: KnownReadCursor): void {
    const previous = knownReadCursors.get(conversationNId)
    if (previous !== undefined && !isNewerReadCursor(cursor, previous)) return

    const current =
      conversations.value.find((item) => item.conversationNId === conversationNId) ??
      unreadConversations.value.find((item) => item.conversationNId === conversationNId)
    const unreadCount =
      cursor.unreadCount ??
      (current !== undefined && cursor.sequence >= current.lastMessageSequence
        ? 0
        : current?.unreadCount)
    const known = {
      ...cursor,
      ...(unreadCount === undefined ? {} : { unreadCount }),
    }
    knownReadCursors.set(conversationNId, known)
    if (unreadCount === undefined) return

    const project = (conversation: ConversationSummary): ConversationSummary =>
      conversation.conversationNId !== conversationNId
        ? conversation
        : {
            ...conversation,
            unreadCount,
            ...(known.projectionVersion === undefined
              ? {}
              : {
                  projectionVersion: Math.max(
                    conversation.projectionVersion ?? 0,
                    known.projectionVersion,
                  ),
                }),
          }
    conversations.value = conversations.value.map(project)
    unreadConversations.value = unreadConversations.value
      .map(project)
      .filter((item) => item.unreadCount > 0)
    if (selectedConversation.value?.conversationNId === conversationNId)
      selectedConversation.value = project(selectedConversation.value)
  }

  function setQuickDrawerOpen(open: boolean): void {
    quickDrawerOpen.value = open
  }

  function recordRealtimeMessage(conversationNId: string): void {
    realtimeMessageEvent.value = {
      conversationNId,
      version: (realtimeMessageEvent.value?.version ?? 0) + 1,
    }
  }

  function resetSession(): void {
    unsubscribeRealtime?.()
    unsubscribeRealtime = undefined
    conversations.value = []
    unreadConversations.value = []
    selectedConversation.value = null
    conversationDetail.value = null
    messages.value = []
    directory.value = []
    loadingConversations.value = false
    loadingMessages.value = false
    sending.value = false
    directoryLoading.value = false
    errorMessage.value = ''
    messageError.value = ''
    directoryKeyword.value = ''
    conversationKeyword.value = ''
    messageDraft.value = ''
    pendingAttachment.value = null
    selectedFile.value = null
    uploadState.value = ''
    detailsOpen.value = false
    showDirectory.value = false
    hiddenOnly.value = false
    unreadOnly.value = false
    historyCursor.value = undefined
    pendingSend.value = null
    for (const key of Object.keys(draftByConversation)) delete draftByConversation[key]
    knownReadCursors.clear()
    for (const key of Object.keys(peerReadCursors)) delete peerReadCursors[key]
    for (const key of Object.keys(messageScrollTopByConversation))
      delete messageScrollTopByConversation[key]
    conversationsLoaded.value = false
    quickDrawerOpen.value = false
    realtimeMessageEvent.value = null
    sessionStarted.value = false
    sessionVersion.value += 1
  }

  return {
    conversations,
    unreadConversations,
    totalUnread,
    selectedConversation,
    conversationDetail,
    messages,
    directory,
    loadingConversations,
    loadingMessages,
    sending,
    directoryLoading,
    errorMessage,
    messageError,
    directoryKeyword,
    conversationKeyword,
    messageDraft,
    pendingAttachment,
    selectedFile,
    uploadState,
    detailsOpen,
    showDirectory,
    hiddenOnly,
    unreadOnly,
    historyCursor,
    pendingSend,
    draftByConversation,
    knownReadCursors,
    peerReadCursors,
    messageScrollTopByConversation,
    conversationsLoaded,
    quickDrawerOpen,
    realtimeMessageEvent,
    sessionStarted,
    sessionVersion,
    startSession,
    replaceConversationProjection,
    replaceUnreadConversationProjection,
    applyReadCursor,
    setQuickDrawerOpen,
    recordRealtimeMessage,
    resetSession,
  }
})
