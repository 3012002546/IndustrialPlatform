<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { ElMessage } from 'element-plus'
import { useRoute, useRouter } from 'vue-router'
import { ROUTE_NAMES } from '@/router/routeNames'

import type {
  CollaborationDirectoryUser,
  ConversationSummary,
  Message,
  ReadCursor,
  SendMessageRequest,
} from '@/api/collaboration'
import { getCollaborationRealtime, type CollaborationRealtime } from '@/api/collaborationHub'
import { getCollaborationApi } from '@/api/collaborationRegistry'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationChatStore, type KnownReadCursor } from '@/stores/collaborationChatStore'
import { sha256File } from '@/utils/sha256File'

const props = withDefaults(
  defineProps<{
    surface?: 'page' | 'drawer'
    active?: boolean
    terminal?: 'pda' | 'mobile'
  }>(),
  { surface: 'page', active: true },
)

const api = getCollaborationApi()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const isTerminalList = computed(() => !!props.terminal && !route.params.conversationNId)
const isTerminalDetail = computed(() => !!props.terminal && !!route.params.conversationNId)
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration)
const chatSession = useCollaborationChatStore()
const {
  conversations,
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
  conversationsLoaded,
  quickDrawerOpen,
  realtimeMessageEvent,
  totalUnread,
} = storeToRefs(chatSession)
const {
  draftByConversation,
  knownReadCursors,
  peerReadCursors,
  messageScrollTopByConversation,
  applyReadCursor,
} = chatSession
const conversationScroll = ref<HTMLElement | null>(null)
const conversationFiltersCollapsed = ref(
  typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 640px)').matches,
)
const composer = ref<HTMLTextAreaElement | null>(null)
const actionMenu = ref<HTMLElement | HTMLElement[] | null>(null)
const actionMenuStyle = ref<Record<string, string>>({})
let directoryTimer: ReturnType<typeof setTimeout> | undefined
let realtime: CollaborationRealtime | null = null
let loadVersion = 0
let conversationLoadVersion = 0
let readInFlight = false
let pendingReadSequence = 0
let shouldStickToLatest = true
let messageMutationObserver: MutationObserver | undefined

const isActiveSurface = computed(
  () =>
    props.active && !isTerminalList.value && (props.surface === 'drawer' || !quickDrawerOpen.value),
)

function isCurrentSession(sessionVersion: number): boolean {
  return sessionVersion === chatSession.sessionVersion
}

function requestId(): string {
  return crypto.randomUUID().replaceAll('-', '')
}

function currentUserNId(): string {
  return auth.user?.userId ?? ''
}

function displayConversation(conversation: ConversationSummary): string {
  return conversation.peerDisplayName || conversation.peerUserNId
}

function conversationPreview(conversation: ConversationSummary): string {
  const preview = conversation.lastMessagePreview
  if (preview === null || preview === undefined) return copy.value.noMessages
  if (preview.state === 'Retracted') return copy.value.retracted
  if (preview.messageType !== 'Text') return copy.value.attachment
  return preview.text || copy.value.noMessages
}

function conversationInitial(conversation: ConversationSummary): string {
  return displayConversation(conversation).trim().slice(0, 1).toLocaleUpperCase() || '?'
}

const filteredConversations = computed(() => {
  const keyword = conversationKeyword.value.trim().toLocaleLowerCase()
  return conversations.value.filter((item) => {
    if (hiddenOnly.value && item.visibilityState !== 'Hidden') return false
    if (unreadOnly.value && item.unreadCount <= 0) return false
    if (keyword.length === 0) return true
    return `${item.peerDisplayName} ${item.peerUserNId}`.toLocaleLowerCase().includes(keyword)
  })
})

const canWrite = computed(() => auth.hasPermission(PERMISSIONS.collaborationMessagingWrite))
const canStart = computed(() =>
  auth.hasPermission(PERMISSIONS.collaborationMessagingConversationStart),
)
const canRetract = computed(() => auth.hasPermission(PERMISSIONS.collaborationMessagingRetract))

async function loadConversations(options: { background?: boolean } = {}): Promise<void> {
  const version = ++conversationLoadVersion
  const sessionVersion = chatSession.sessionVersion
  const showLoading = !options.background && !conversationsLoaded.value
  if (showLoading) loadingConversations.value = true
  if (!options.background) errorMessage.value = ''
  try {
    const items: ConversationSummary[] = []
    const cursors = new Set<string>()
    let cursor: string | undefined
    let nextCursor: string | null = null
    do {
      const result = await api.listConversations({
        ...(cursor === undefined ? {} : { cursor }),
        visibility: hiddenOnly.value ? 'Hidden' : 'Visible',
        unreadOnly: unreadOnly.value,
        pageSize: 100,
      })
      if (!isCurrentSession(sessionVersion) || version !== conversationLoadVersion) return
      items.push(...result.items)
      nextCursor = result.nextCursor
      if (nextCursor === null || cursors.has(nextCursor)) break
      cursors.add(nextCursor)
      cursor = nextCursor
    } while (true)
    if (version !== conversationLoadVersion || !isCurrentSession(sessionVersion)) return
    chatSession.replaceConversationProjection(items)
    if (!hiddenOnly.value && !unreadOnly.value)
      chatSession.replaceUnreadConversationProjection(items)
  } catch (error) {
    if (
      version === conversationLoadVersion &&
      isCurrentSession(sessionVersion) &&
      !options.background
    )
      errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed
  } finally {
    if (version === conversationLoadVersion && isCurrentSession(sessionVersion) && showLoading)
      loadingConversations.value = false
  }
}

async function loadMessages(
  conversation: ConversationSummary,
  cursor?: string,
  options: { background?: boolean } = {},
): Promise<void> {
  const version = ++loadVersion
  const sessionVersion = chatSession.sessionVersion
  const scrollBeforeHistory = cursor === undefined ? undefined : conversationScroll.value
  const previousScrollTop = scrollBeforeHistory?.scrollTop ?? 0
  const previousScrollHeight = scrollBeforeHistory?.scrollHeight ?? 0
  const showLoading = !options.background && messages.value.length === 0
  if (showLoading) loadingMessages.value = true
  if (!options.background) messageError.value = ''
  try {
    const result = await api.getMessages(conversation.conversationNId, {
      mode: 'history',
      ...(cursor === undefined ? {} : { cursor }),
      pageSize: 50,
    })
    if (
      version !== loadVersion ||
      selectedConversation.value?.conversationNId !== conversation.conversationNId ||
      !isCurrentSession(sessionVersion)
    )
      return
    messages.value =
      cursor === undefined
        ? options.background
          ? mergeMessages(messages.value, result.items)
          : result.items
        : [...result.items, ...messages.value]
    historyCursor.value = result.nextCursor ?? undefined
    if (cursor !== undefined && scrollBeforeHistory !== undefined) {
      await nextTick()
      const element = conversationScroll.value
      if (element !== null && element === scrollBeforeHistory)
        element.scrollTop = previousScrollTop + element.scrollHeight - previousScrollHeight
    }
    if (cursor === undefined) void markDisplayedRead()
  } catch (error) {
    if (version === loadVersion && isCurrentSession(sessionVersion) && !options.background)
      messageError.value = error instanceof Error ? error.message : copy.value.loadFailed
  } finally {
    if (version === loadVersion && isCurrentSession(sessionVersion) && showLoading)
      loadingMessages.value = false
  }
}

async function openConversation(conversation: ConversationSummary): Promise<void> {
  if (props.terminal) {
    await router.push({
      name:
        props.terminal === 'pda'
          ? ROUTE_NAMES.pdaCollaborationConversation
          : ROUTE_NAMES.mobileCollaborationConversation,
      params: { conversationNId: conversation.conversationNId },
    })
  } else {
    await selectConversation(conversation)
  }
}

function backToConversations(): void {
  detailsOpen.value = false
  void router.push({
    name:
      props.terminal === 'pda'
        ? ROUTE_NAMES.pdaCollaborationChat
        : ROUTE_NAMES.mobileCollaborationChat,
  })
}

async function selectConversation(conversation: ConversationSummary): Promise<void> {
  const sessionVersion = chatSession.sessionVersion
  const previousConversation = selectedConversation.value
  rememberConversationScroll()
  if (previousConversation !== null && realtime !== null)
    void realtime.leaveConversation(previousConversation.conversationNId).catch(() => undefined)
  if (selectedConversation.value?.conversationNId !== undefined) {
    draftByConversation[selectedConversation.value.conversationNId] = messageDraft.value
  }
  selectedConversation.value = conversation
  messageDraft.value = draftByConversation[conversation.conversationNId] ?? ''
  pendingAttachment.value = null
  selectedFile.value = null
  uploadState.value = ''
  detailsOpen.value = false
  conversationDetail.value = null
  await Promise.all([loadMessages(conversation), loadConversationDetail(conversation)])
  if (!isCurrentSession(sessionVersion)) return
  if (realtime !== null)
    void realtime.joinConversation(conversation.conversationNId).catch(() => undefined)
}

async function loadConversationDetail(conversation: ConversationSummary): Promise<void> {
  const sessionVersion = chatSession.sessionVersion
  try {
    const detail = await api.getConversation(conversation.conversationNId)
    if (
      selectedConversation.value?.conversationNId !== conversation.conversationNId ||
      !isCurrentSession(sessionVersion)
    )
      return
    conversationDetail.value = detail
    peerReadCursors[conversation.conversationNId] = Math.max(
      peerReadCursors[conversation.conversationNId] ?? 0,
      detail.peerMember.lastReadSequence,
    )
  } catch {
    // Message history remains usable when the read-receipt projection is temporarily unavailable.
  }
}

const hiddenMessageNIds = new Set<string>()
const summaryMessageStates = new Map<
  string,
  { messageStateVersion: number; state: Message['state'] }
>()

function messageStateRank(state: Message['state']): number {
  return state === 'Retracted' ? 2 : state === 'Accepted' ? 1 : 0
}

function canApplyConversationPreview(message: Message): boolean {
  if (hiddenMessageNIds.has(message.messageNId)) return false
  const known = summaryMessageStates.get(message.messageNId)
  if (
    known !== undefined &&
    (message.messageStateVersion < known.messageStateVersion ||
      (message.messageStateVersion === known.messageStateVersion &&
        messageStateRank(message.state) <= messageStateRank(known.state)))
  )
    return false
  summaryMessageStates.set(message.messageNId, {
    messageStateVersion: message.messageStateVersion,
    state: message.state,
  })
  return true
}

function mergeMessages(current: Message[], incoming: Message[]): Message[] {
  const next = new Map<string, Message>()
  for (const message of current) next.set(message.messageNId, message)
  for (const message of incoming) {
    if (hiddenMessageNIds.has(message.messageNId)) continue
    const pending = [...next.values()].find(
      (candidate) => candidate.clientMessageNId === message.clientMessageNId,
    )
    if (pending !== undefined && pending.messageNId !== message.messageNId)
      next.delete(pending.messageNId)
    const existing = next.get(message.messageNId)
    if (existing === undefined || message.messageStateVersion >= existing.messageStateVersion)
      next.set(message.messageNId, message)
  }
  return [...next.values()].sort((left, right) => {
    const leftPending = left.state === 'Pending'
    const rightPending = right.state === 'Pending'
    if (leftPending !== rightPending) return leftPending ? 1 : -1
    return left.sequence - right.sequence
  })
}

function previewFromMessage(
  message: Message,
): NonNullable<ConversationSummary['lastMessagePreview']> {
  return {
    messageNId: message.messageNId,
    sequence: message.sequence,
    acceptedOn: message.acceptedOn,
    messageType: message.messageType,
    state: message.state,
    text:
      message.state === 'Accepted' && message.messageType === 'Text'
        ? (message.textContent?.slice(0, 160) ?? null)
        : null,
  }
}

function updateConversationFromMessage(message: Message): void {
  if (!canApplyConversationPreview(message)) return
  conversations.value = conversations.value.map((conversation) =>
    conversation.conversationNId !== message.conversationNId
      ? conversation
      : message.sequence < conversation.lastMessageSequence
        ? conversation
        : {
            ...conversation,
            lastMessageSequence: message.sequence,
            lastMessageNId: message.messageNId,
            lastMessageOn: message.acceptedOn,
            lastMessagePreview: previewFromMessage(message),
          },
  )
}

function redactConversationPreview(conversationNId: string, messageNId: string): void {
  hiddenMessageNIds.add(messageNId)
  conversations.value = conversations.value.map((conversation) =>
    conversation.conversationNId !== conversationNId ||
    conversation.lastMessagePreview?.messageNId !== messageNId
      ? conversation
      : {
          ...conversation,
          lastMessageNId: null,
          lastMessageOn: null,
          lastMessagePreview: null,
        },
  )
}

function onRealtimeMessage(message: Message): void {
  if (selectedConversation.value?.conversationNId === message.conversationNId) {
    messages.value = mergeMessages(messages.value, [message])
  }
  updateConversationFromMessage(message)
  chatSession.recordRealtimeMessage(message.conversationNId)
}

function onRealtimeRetraction(message: Message): void {
  if (selectedConversation.value?.conversationNId === message.conversationNId)
    messages.value = mergeMessages(messages.value, [message])
  updateConversationFromMessage(message)
}

function onRealtimePresence(presence: ConversationSummary['presence']): void {
  if (presence === undefined) return
  conversations.value = conversations.value.map((conversation) =>
    conversation.peerUserNId === presence.userNId ? { ...conversation, presence } : conversation,
  )
  if (selectedConversation.value?.peerUserNId === presence.userNId)
    selectedConversation.value = { ...selectedConversation.value, presence }
}

function readCursorToKnown(cursor: ReadCursor): KnownReadCursor {
  return {
    sequence: cursor.lastReadSequence,
    unreadCount: cursor.unreadCount,
    projectionVersion: cursor.projectionVersion,
  }
}

function canMarkDisplayedRead(): boolean {
  return (
    isActiveSurface.value &&
    document.visibilityState === 'visible' &&
    document.hasFocus() &&
    selectedConversation.value !== null
  )
}

async function markDisplayedRead(): Promise<void> {
  const conversation = selectedConversation.value
  const last = [...messages.value]
    .reverse()
    .find(
      (message) => message.state !== 'Pending' && Number.isSafeInteger(Number(message.sequence)),
    )
  if (!canMarkDisplayedRead() || conversation === null || last === undefined) return
  const sequence = Number(last.sequence)
  const known = knownReadCursors.get(conversation.conversationNId)?.sequence ?? 0
  if (sequence <= known) return
  if (readInFlight) {
    pendingReadSequence = Math.max(pendingReadSequence, sequence)
    return
  }
  readInFlight = true
  let acknowledged = 0
  let succeeded = false
  try {
    const cursor = await api.markRead(conversation.conversationNId, sequence)
    succeeded = true
    acknowledged = cursor.lastReadSequence
    if (selectedConversation.value?.conversationNId === conversation.conversationNId)
      applyReadCursor(conversation.conversationNId, readCursorToKnown(cursor))
  } catch {
    pendingReadSequence = Math.max(pendingReadSequence, sequence)
  } finally {
    readInFlight = false
    const pending = pendingReadSequence
    pendingReadSequence = 0
    if (succeeded && pending > acknowledged) void markDisplayedRead()
  }
}

function onDocumentFocus(): void {
  void markDisplayedRead()
}

function onRealtimeReadCursor(event: {
  conversationNId: string
  userNId: string
  sequence: number
  projectionVersion?: number
}): void {
  if (event.userNId === currentUserNId()) {
    applyReadCursor(event.conversationNId, {
      sequence: event.sequence,
      ...(event.projectionVersion === undefined
        ? {}
        : { projectionVersion: event.projectionVersion }),
    })
    return
  }
  if (selectedConversation.value?.conversationNId !== event.conversationNId) return
  const known = peerReadCursors[event.conversationNId] ?? 0
  if (event.sequence <= known) return
  peerReadCursors[event.conversationNId] = event.sequence
  if (conversationDetail.value?.conversationNId === event.conversationNId)
    conversationDetail.value = {
      ...conversationDetail.value,
      peerMember: {
        ...conversationDetail.value.peerMember,
        lastReadSequence: event.sequence,
      },
    }
}

function onRealtimePersonalMessageHidden(event: {
  conversationNId: string
  messageNId: string
}): void {
  if (selectedConversation.value?.conversationNId === event.conversationNId)
    messages.value = messages.value.filter((message) => message.messageNId !== event.messageNId)
  redactConversationPreview(event.conversationNId, event.messageNId)
  void loadConversations({ background: true })
}

function scheduleDirectorySearch(): void {
  if (directoryTimer !== undefined) clearTimeout(directoryTimer)
  const keyword = directoryKeyword.value.trim()
  if (keyword.length === 0) {
    directory.value = []
    showDirectory.value = false
    return
  }
  directoryTimer = setTimeout(() => void searchDirectory(), 300)
}

async function searchDirectory(): Promise<void> {
  const keyword = directoryKeyword.value.trim()
  if (keyword.length === 0) return
  directoryLoading.value = true
  showDirectory.value = true
  try {
    const result = await api.searchUsers(keyword, undefined, 50)
    if (directoryKeyword.value.trim() === keyword) directory.value = result.items
  } catch (error) {
    directory.value = []
    ElMessage.error(error instanceof Error ? error.message : copy.value.directoryLoadFailed)
  } finally {
    directoryLoading.value = false
  }
}

async function startConversation(user: CollaborationDirectoryUser): Promise<void> {
  if (!user.canStart || !canStart.value) return
  try {
    const conversation = await api.createConversation({
      peerUserNId: user.userNId,
      requestNId: requestId(),
    })
    showDirectory.value = false
    directoryKeyword.value = ''
    await loadConversations()
    await openConversation(conversation)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

function onDraftInput(): void {
  const conversation = selectedConversation.value
  pendingSend.value = null
  if (conversation !== null) draftByConversation[conversation.conversationNId] = messageDraft.value
}

async function sendMessage(): Promise<void> {
  const conversation = selectedConversation.value
  const text = messageDraft.value.trim()
  if (
    conversation === null ||
    !canWrite.value ||
    sending.value ||
    (text.length === 0 && pendingAttachment.value === null)
  )
    return
  if (text.length > 4000) {
    ElMessage.warning(copy.value.messageTooLong)
    return
  }
  sending.value = true
  messageError.value = ''
  try {
    const attachment = pendingAttachment.value
    const messageType: SendMessageRequest['messageType'] =
      attachment === null
        ? 'Text'
        : selectedFile.value?.type.startsWith('image/') === true
          ? 'Image'
          : 'File'
    const retry = pendingSend.value
    const request: SendMessageRequest =
      retry !== null &&
      retry.conversationNId === conversation.conversationNId &&
      retry.textContent === text &&
      retry.attachmentNId === (attachment?.attachmentNId ?? null)
        ? retry.request
        : (() => {
            const clientMessageNId = requestId()
            return {
              clientMessageNId,
              messageType,
              ...(text.length === 0 ? {} : { textContent: text }),
              ...(attachment === null ? {} : { attachmentNId: attachment.attachmentNId }),
              idempotencyKey: clientMessageNId,
            }
          })()
    pendingSend.value = {
      conversationNId: conversation.conversationNId,
      textContent: text,
      attachmentNId: attachment?.attachmentNId ?? null,
      request,
    }
    const optimistic: Message = {
      messageNId: `local:${request.clientMessageNId}`,
      conversationNId: conversation.conversationNId,
      sequence: Number.MAX_SAFE_INTEGER,
      senderUserNId: currentUserNId(),
      senderDisplayName: auth.user?.displayName ?? auth.user?.username ?? currentUserNId(),
      clientMessageNId: request.clientMessageNId,
      messageType: request.messageType,
      textContent: request.textContent ?? null,
      replyToMessageNId: request.replyToMessageNId ?? null,
      attachment: null,
      acceptedOn: new Date().toISOString(),
      retractedOn: null,
      retractionReason: null,
      state: 'Pending',
      messageStateVersion: 0,
    }
    messages.value = mergeMessages(messages.value, [optimistic])
    messageDraft.value = ''
    draftByConversation[conversation.conversationNId] = ''
    pendingAttachment.value = null
    selectedFile.value = null
    uploadState.value = ''
    await nextTick()
    composer.value?.focus()
    scrollToLatest()
    const accepted =
      realtime?.connection.state === 'Connected'
        ? await realtime.sendMessage(conversation.conversationNId, request)
        : await api.sendMessage(conversation.conversationNId, request)
    if (selectedConversation.value?.conversationNId === conversation.conversationNId) {
      messages.value = mergeMessages(messages.value, [accepted])
      if (isActiveSurface.value) {
        await nextTick()
        scrollToLatest()
      }
    }
    updateConversationFromMessage(accepted)
    pendingSend.value = null
  } catch (error) {
    messageError.value = error instanceof Error ? error.message : copy.value.sendFailed
  } finally {
    sending.value = false
  }
}

function onComposerKeydown(event: KeyboardEvent): void {
  // PDA 扫码枪会模拟 Enter；保留输入框原生换行，仅通过发送按钮提交。
  if (props.terminal === 'pda') return
  if (event.isComposing || event.key !== 'Enter' || event.shiftKey) return
  event.preventDefault()
  void sendMessage()
}

async function uploadAttachment(file: File): Promise<void> {
  const conversation = selectedConversation.value
  if (conversation === null || !canWrite.value) return
  if (file.size < 1 || file.size > 50 * 1024 * 1024) {
    ElMessage.warning(copy.value.attachmentSizeInvalid)
    return
  }
  uploadState.value = copy.value.hashing
  try {
    const sha256 = await sha256File(file)
    const intent = await api.createAttachmentIntent(conversation.conversationNId, {
      fileName: file.name.replace(/[\\/]/g, '_').slice(0, 255),
      mediaType: file.type || 'application/octet-stream',
      sizeBytes: file.size,
      sha256,
      requestNId: requestId(),
    })
    if (intent.uploadSessionNId === null || intent.transportId === null)
      throw new Error(copy.value.fileUnavailable)
    let session = await api.getAttachmentUploadSession(intent.attachmentNId)
    if (session.state === 'WaitingForProof')
      session = await api.resumeAttachmentProof(intent.attachmentNId, session.writerEpoch, sha256)
    else if (session.state === 'Paused')
      session = await api.resumeAttachmentUpload(intent.attachmentNId, session.writerEpoch, sha256)
    const chunkSize = 4 * 1024 * 1024
    while (session.offset < file.size) {
      uploadState.value = copy.value.uploading
        .replace('{current}', String(session.offset))
        .replace('{total}', String(file.size))
      session = await api.uploadAttachmentChunk(
        intent.attachmentNId,
        session.transportId,
        file.slice(session.offset, Math.min(session.offset + chunkSize, file.size)),
        session.offset,
        session.writerEpoch,
        session.resumeTicket,
      )
    }
    const completed = await api.completeAttachmentUpload(intent.attachmentNId)
    if (!completed.fileNId) throw new Error(copy.value.fileUnavailable)
    const authorization = await api.authorizeAttachment(
      conversation.conversationNId,
      intent.attachmentNId,
      completed.fileNId,
      requestId(),
    )
    pendingAttachment.value = {
      attachmentNId: authorization.attachmentNId,
      fileName: file.name,
      state: authorization.state,
    }
    selectedFile.value = file
    uploadState.value = copy.value.attachmentReady
  } catch (error) {
    uploadState.value = error instanceof Error ? error.message : copy.value.attachmentFailed
  }
}

function onFileChange(event: Event): void {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (file !== undefined) void uploadAttachment(file)
  ;(event.target as HTMLInputElement).value = ''
}

async function retract(message: Message): Promise<void> {
  const conversation = selectedConversation.value
  if (conversation === null || !canRetract.value || message.senderUserNId !== currentUserNId())
    return
  try {
    const retracted = await api.retractMessage(
      conversation.conversationNId,
      message.messageNId,
      'sender_retract',
    )
    messages.value = mergeMessages(messages.value, [retracted])
    updateConversationFromMessage(retracted)
    actionMenuMessageNId.value = null
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

const actionMenuMessageNId = ref<string | null>(null)

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') actionMenuMessageNId.value = null
}

function onDocumentPointerDown(event: PointerEvent): void {
  const target = event.target as Node | null
  const menus = Array.isArray(actionMenu.value) ? actionMenu.value : [actionMenu.value]
  if (menus.some((menu) => menu?.contains(target))) return
  actionMenuMessageNId.value = null
}

function positionActionMenu(): void {
  if (actionMenuMessageNId.value === null) return
  const menus = Array.isArray(actionMenu.value) ? actionMenu.value : [actionMenu.value]
  const menu = menus.find((candidate): candidate is HTMLElement => candidate !== null)
  const anchor = menu?.parentElement
  if (menu === undefined || anchor == null) return

  const anchorRect = anchor.getBoundingClientRect()
  const menuRect = menu.getBoundingClientRect()
  const width = menuRect.width || menu.offsetWidth || 112
  const height = menuRect.height || menu.offsetHeight || 96
  const edge = 8
  const viewportWidth = document.documentElement.clientWidth || window.innerWidth
  const viewportHeight = document.documentElement.clientHeight || window.innerHeight
  const left = Math.min(
    Math.max(edge, anchorRect.right - width),
    Math.max(edge, viewportWidth - width - edge),
  )
  const above = anchorRect.top - height - 4
  const top =
    above >= edge
      ? above
      : Math.min(
          Math.max(edge, anchorRect.bottom + 4),
          Math.max(edge, viewportHeight - height - edge),
        )
  actionMenuStyle.value = { left: `${left}px`, top: `${top}px`, visibility: 'visible' }
}

function repositionActionMenu(): void {
  if (actionMenuMessageNId.value !== null) void nextTick(positionActionMenu)
}

function onMessageContextMenu(event: MouseEvent, message: Message): void {
  if (message.senderUserNId !== currentUserNId() || message.state === 'Retracted') return
  event.preventDefault()
  openMessageMenu(message)
}

function openMessageMenu(message: Message): void {
  if (message.senderUserNId === currentUserNId() && message.state !== 'Retracted') {
    actionMenuStyle.value = { visibility: 'hidden' }
    actionMenuMessageNId.value = message.messageNId
    void nextTick(positionActionMenu)
  }
}

function onMessageActionKeydown(event: KeyboardEvent, message: Message): void {
  if (event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return
  event.preventDefault()
  openMessageMenu(message)
}

async function copyMessage(message: Message): Promise<void> {
  if (message.state === 'Retracted' || !message.textContent) return
  try {
    await navigator.clipboard.writeText(message.textContent)
    actionMenuMessageNId.value = null
  } catch {
    ElMessage.error(copy.value.actionFailed)
  }
}

async function deleteForCurrentUser(message: Message): Promise<void> {
  const conversation = selectedConversation.value
  if (
    conversation === null ||
    !canWrite.value ||
    message.senderUserNId !== currentUserNId() ||
    message.state === 'Retracted'
  )
    return
  try {
    await api.setPersonalMessageVisibility(conversation.conversationNId, message.messageNId, true)
    messages.value = messages.value.filter(
      (candidate) => candidate.messageNId !== message.messageNId,
    )
    redactConversationPreview(conversation.conversationNId, message.messageNId)
    void loadConversations({ background: true })
    actionMenuMessageNId.value = null
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

function peerReadSequence(conversationNId: string): number {
  return Math.max(
    peerReadCursors[conversationNId] ?? 0,
    conversationDetail.value?.conversationNId === conversationNId
      ? conversationDetail.value.peerMember.lastReadSequence
      : 0,
  )
}

function isPeerRead(message: Message): boolean {
  return (
    message.senderUserNId === currentUserNId() &&
    peerReadSequence(message.conversationNId) >= message.sequence
  )
}

async function toggleHidden(conversation: ConversationSummary): Promise<void> {
  try {
    if (conversation.visibilityState === 'Hidden')
      await api.restoreConversation(conversation.conversationNId)
    else await api.hideConversation(conversation.conversationNId, conversation.lastMessageSequence)
    await loadConversations()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : copy.value.actionFailed)
  }
}

function formatTime(value: string | null | undefined): string {
  if (value === null || value === undefined || value.length === 0) return ''
  return new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' }).format(
    new Date(value),
  )
}

function presenceLabel(conversation: ConversationSummary): string {
  const state = conversation.presence?.state ?? 'Unknown'
  return state === 'Online'
    ? copy.value.online
    : state === 'Offline'
      ? copy.value.offline
      : copy.value.unknown
}

function scrollToLatest(): void {
  const element = conversationScroll.value
  if (element !== null) {
    element.scrollTop = element.scrollHeight
    shouldStickToLatest = true
  }
}

function rememberConversationScroll(): void {
  const conversationNId = selectedConversation.value?.conversationNId
  const element = conversationScroll.value
  if (conversationNId && element !== null) {
    shouldStickToLatest = element.scrollTop + element.clientHeight >= element.scrollHeight - 4
    messageScrollTopByConversation[conversationNId] = element.scrollTop
  }
}

function restoreConversationScroll(): void {
  const conversationNId = selectedConversation.value?.conversationNId
  const element = conversationScroll.value
  if (!conversationNId || element === null) return
  const savedScrollTop = messageScrollTopByConversation[conversationNId]
  element.scrollTop = savedScrollTop ?? element.scrollHeight
  shouldStickToLatest =
    savedScrollTop === undefined ||
    element.scrollTop + element.clientHeight >= element.scrollHeight - 4
}

function keepPinnedToLatest(): void {
  if (isActiveSurface.value && shouldStickToLatest) scrollToLatest()
}

function onMessageMediaLoad(): void {
  void nextTick(keepPinnedToLatest)
}

function beginObservingMessageContent(): void {
  const element = conversationScroll.value
  if (element === null || typeof MutationObserver === 'undefined') return
  element.addEventListener('load', onMessageMediaLoad, true)
  messageMutationObserver = new MutationObserver(() => {
    void nextTick(keepPinnedToLatest)
  })
  messageMutationObserver.observe(element, { childList: true, subtree: true })
}

async function selectRouteConversation(): Promise<void> {
  const conversationNId = String(route.params.conversationNId ?? '').trim()
  if (
    conversationNId.length === 0 ||
    selectedConversation.value?.conversationNId === conversationNId
  )
    return
  const conversation = conversations.value.find((item) => item.conversationNId === conversationNId)
  if (conversation !== undefined) await selectConversation(conversation)
}

onMounted(() => {
  document.addEventListener('keydown', onDocumentKeydown)
  document.addEventListener('pointerdown', onDocumentPointerDown)
  document.addEventListener('scroll', repositionActionMenu, true)
  window.addEventListener('resize', repositionActionMenu)
  window.addEventListener('focus', onDocumentFocus)
  void nextTick(beginObservingMessageContent)
  void (async () => {
    const sessionVersion = chatSession.sessionVersion
    realtime = getCollaborationRealtime()
    if (chatSession.sessionStarted) {
      await selectRouteConversation()
      if (!isCurrentSession(sessionVersion)) return
      if (typeof requestAnimationFrame === 'function')
        requestAnimationFrame(restoreConversationScroll)
      else setTimeout(restoreConversationScroll, 0)
      return
    }
    const unsubscribe = realtime.subscribe?.({
      onMessage: (message) => {
        if (isCurrentSession(sessionVersion)) onRealtimeMessage(message)
      },
      onMessageRetracted: (message) => {
        if (isCurrentSession(sessionVersion)) onRealtimeRetraction(message)
      },
      onPresence: (presence) => {
        if (isCurrentSession(sessionVersion)) onRealtimePresence(presence)
      },
      onReadCursor: (event) => {
        if (isCurrentSession(sessionVersion)) onRealtimeReadCursor(event)
      },
      onPersonalMessageHidden: (event) => {
        if (isCurrentSession(sessionVersion)) onRealtimePersonalMessageHidden(event)
      },
      onReconnected: async () => {
        if (!isCurrentSession(sessionVersion)) return
        const conversation = selectedConversation.value
        await realtime
          ?.setPresence('Online')
          .catch(() => api.setPresence('Online').catch(() => undefined))
        if (!isCurrentSession(sessionVersion)) return
        await loadConversations({ background: true })
        if (!isCurrentSession(sessionVersion)) return
        if (conversation !== null) {
          await realtime?.joinConversation(conversation.conversationNId)
          if (!isCurrentSession(sessionVersion)) return
          await Promise.all([
            loadMessages(conversation, undefined, { background: true }),
            loadConversationDetail(conversation),
          ])
        }
      },
    })
    if (!chatSession.startSession(unsubscribe)) {
      unsubscribe?.()
      return
    }
    await realtime.start()
    if (!isCurrentSession(sessionVersion)) return
    void realtime
      .setPresence('Online')
      .catch(() => api.setPresence('Online').catch(() => undefined))
    await loadConversations()
    if (!isCurrentSession(sessionVersion)) return
    await selectRouteConversation()
    if (!isCurrentSession(sessionVersion)) return
    if (typeof requestAnimationFrame === 'function')
      requestAnimationFrame(restoreConversationScroll)
    else setTimeout(restoreConversationScroll, 0)
  })()
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onDocumentKeydown)
  document.removeEventListener('pointerdown', onDocumentPointerDown)
  document.removeEventListener('scroll', repositionActionMenu, true)
  window.removeEventListener('resize', repositionActionMenu)
  window.removeEventListener('focus', onDocumentFocus)
  conversationScroll.value?.removeEventListener('load', onMessageMediaLoad, true)
  messageMutationObserver?.disconnect()
  messageMutationObserver = undefined
  if (directoryTimer !== undefined) clearTimeout(directoryTimer)
  // The shell-level session owns one SignalR subscription and heartbeat for both the page and quick drawer.
  // Keeping it alive preserves the active conversation, drafts, and scroll state when surfaces switch.
})

watch(
  () => selectedConversation.value?.conversationNId,
  () => {
    actionMenuMessageNId.value = null
    if (typeof requestAnimationFrame === 'function')
      requestAnimationFrame(restoreConversationScroll)
    else setTimeout(restoreConversationScroll, 0)
  },
)

watch(isActiveSurface, (active) => {
  if (active)
    void nextTick(() => {
      restoreConversationScroll()
      void markDisplayedRead()
    })
  else actionMenuMessageNId.value = null
})

watch(
  () => route.params.conversationNId,
  () => {
    if (props.terminal) void selectRouteConversation()
  },
)

watch(realtimeMessageEvent, (event) => {
  if (
    event === null ||
    !isActiveSurface.value ||
    selectedConversation.value?.conversationNId !== event.conversationNId
  )
    return
  void nextTick(() => {
    scrollToLatest()
    void markDisplayedRead()
  })
})
</script>

<template>
  <component
    :is="props.surface === 'page' ? AppPage : 'section'"
    v-bind="props.surface === 'page' ? { title: copy.title, description: copy.description } : {}"
    :class="{ 'collaboration-chat-surface': props.surface === 'drawer' }"
  >
    <section
      class="collaboration-chat"
      :class="{
        'collaboration-chat--drawer': props.surface === 'drawer',
        'collaboration-chat--terminal': !!props.terminal,
      }"
      :aria-label="copy.title"
    >
      <header v-show="!isTerminalDetail" class="collaboration-chat__toolbar">
        <div class="collaboration-chat__unread">{{ copy.unread }} {{ totalUnread }}</div>
        <div class="collaboration-chat__new">
          <label class="sr-only" for="collaboration-directory">{{ copy.searchPeople }}</label>
          <input
            id="collaboration-directory"
            v-model="directoryKeyword"
            :placeholder="copy.searchPeople"
            @input="scheduleDirectorySearch"
            @focus="showDirectory = directory.length > 0"
          />
          <div v-if="showDirectory" class="collaboration-chat__directory" role="listbox">
            <span v-if="directoryLoading" class="collaboration-chat__hint">{{ copy.loading }}</span>
            <button
              v-for="user in directory"
              v-else
              :key="user.userNId"
              type="button"
              :disabled="!user.canStart"
              role="option"
              @click="startConversation(user)"
            >
              <strong>{{ user.displayName }}</strong
              ><small>{{ user.userNId }}</small>
            </button>
            <span
              v-if="!directoryLoading && directory.length === 0"
              class="collaboration-chat__hint"
              >{{ copy.noPeople }}</span
            >
          </div>
        </div>
      </header>

      <div class="collaboration-chat__body">
        <aside
          v-show="!isTerminalDetail"
          class="collaboration-chat__sidebar"
          :aria-label="copy.conversations"
        >
          <AppQueryPanel
            v-model:collapsed="conversationFiltersCollapsed"
            :title="copy.conversations"
            collapsible
          >
            <input
              v-model="conversationKeyword"
              class="collaboration-chat__filter"
              :placeholder="copy.filterConversations"
            />
            <div class="collaboration-chat__filters">
              <label
                ><input
                  v-model="unreadOnly"
                  type="checkbox"
                  @change="() => void loadConversations()"
                />
                {{ copy.unreadOnly }}</label
              >
              <label
                ><input
                  v-model="hiddenOnly"
                  type="checkbox"
                  @change="() => void loadConversations()"
                />
                {{ copy.hidden }}</label
              >
            </div>
          </AppQueryPanel>
          <div v-if="loadingConversations" class="collaboration-chat__hint" role="status">
            {{ copy.loading }}
          </div>
          <p v-if="errorMessage" class="collaboration-chat__error" role="alert">
            {{ errorMessage }}
          </p>
          <div class="collaboration-chat__conversation-list" role="list">
            <button
              v-for="conversation in filteredConversations"
              :key="conversation.conversationNId"
              type="button"
              role="listitem"
              class="collaboration-chat__conversation"
              :class="{
                'is-active': selectedConversation?.conversationNId === conversation.conversationNId,
              }"
              @click="openConversation(conversation)"
            >
              <span class="collaboration-chat__avatar" aria-hidden="true"
                >{{ conversationInitial(conversation)
                }}<i
                  class="collaboration-chat__presence-dot"
                  :class="`is-${(conversation.presence?.state ?? 'Unknown').toLowerCase()}`"
                />
              </span>
              <span class="collaboration-chat__conversation-copy"
                ><strong>{{ displayConversation(conversation) }}</strong
                ><small class="collaboration-chat__conversation-preview">{{
                  conversationPreview(conversation)
                }}</small></span
              >
              <span class="collaboration-chat__conversation-meta">
                <time>{{ formatTime(conversation.lastMessageOn) }}</time>
                <span v-if="conversation.unreadCount > 0" class="collaboration-chat__badge">{{
                  conversation.unreadCount
                }}</span>
              </span>
              <span class="sr-only"
                >{{ presenceLabel(conversation) }} ·
                {{ formatTime(conversation.lastMessageOn) }}</span
              >
              <span
                v-if="conversation.visibilityState === 'Hidden'"
                class="collaboration-chat__hidden"
                >{{ copy.hidden }}</span
              >
            </button>
            <span
              v-if="!loadingConversations && filteredConversations.length === 0"
              class="collaboration-chat__hint"
              >{{ copy.noConversations }}</span
            >
          </div>
        </aside>

        <section
          v-show="!isTerminalList"
          class="collaboration-chat__main"
          aria-live="polite"
          @focusin="() => void markDisplayedRead()"
          @pointerdown="() => void markDisplayedRead()"
        >
          <button
            v-if="props.terminal"
            type="button"
            class="collaboration-chat__back"
            @click="backToConversations"
          >
            ← {{ copy.conversations }}
          </button>
          <template v-if="selectedConversation !== null">
            <header class="collaboration-chat__conversation-header">
              <div>
                <h2>{{ displayConversation(selectedConversation) }}</h2>
                <span class="collaboration-chat__presence"
                  ><i
                    class="collaboration-chat__presence-dot"
                    :class="`is-${(selectedConversation.presence?.state ?? 'Unknown').toLowerCase()}`"
                    aria-hidden="true"
                  />
                  <span>{{ presenceLabel(selectedConversation) }}</span></span
                >
              </div>
              <div class="collaboration-chat__header-actions">
                <button type="button" @click="detailsOpen = !detailsOpen">
                  {{ copy.details }}
                </button>
                <button type="button" @click="toggleHidden(selectedConversation)">
                  {{ selectedConversation.visibilityState === 'Hidden' ? copy.restore : copy.hide }}
                </button>
              </div>
            </header>
            <div
              ref="conversationScroll"
              class="collaboration-chat__messages"
              @scroll="rememberConversationScroll"
            >
              <button
                v-if="historyCursor"
                type="button"
                class="collaboration-chat__history"
                :disabled="loadingMessages"
                @click="loadMessages(selectedConversation!, historyCursor)"
              >
                {{ loadingMessages ? copy.loading : copy.loadEarlier }}
              </button>
              <p v-if="messageError" class="collaboration-chat__error" role="alert">
                {{ messageError }}
              </p>
              <div v-if="loadingMessages && messages.length === 0" class="collaboration-chat__hint">
                {{ copy.loading }}
              </div>
              <article
                v-for="message in messages"
                :key="message.messageNId"
                class="collaboration-chat__message"
                :class="{
                  'is-own': message.senderUserNId === currentUserNId(),
                  'is-retracted': message.state === 'Retracted',
                }"
                :tabindex="
                  message.senderUserNId === currentUserNId() && message.state !== 'Retracted'
                    ? 0
                    : undefined
                "
                @contextmenu="onMessageContextMenu($event, message)"
                @keydown="onMessageActionKeydown($event, message)"
              >
                <div class="collaboration-chat__message-meta">
                  <time>{{ formatTime(message.acceptedOn) }}</time>
                </div>
                <p v-if="message.state === 'Retracted'" class="collaboration-chat__tombstone">
                  {{ copy.retracted }}
                </p>
                <template v-else>
                  <p v-if="message.textContent" class="collaboration-chat__message-text">
                    {{ message.textContent }}
                  </p>
                  <div v-if="message.attachment" class="collaboration-chat__attachment">
                    {{ message.attachment.fileName }} · {{ message.attachment.fileState }}
                  </div>
                </template>
                <span v-if="isPeerRead(message)" class="collaboration-chat__read-receipt">
                  {{ copy.readByPeer }}
                </span>
                <div
                  v-if="message.senderUserNId === currentUserNId() && message.state !== 'Retracted'"
                  class="collaboration-chat__message-actions"
                >
                  <div
                    v-if="actionMenuMessageNId === message.messageNId"
                    ref="actionMenu"
                    class="collaboration-chat__action-menu"
                    role="menu"
                    :style="actionMenuStyle"
                    @keydown.esc="actionMenuMessageNId = null"
                  >
                    <el-button text role="menuitem" @click="copyMessage(message)">
                      {{ copy.copyMessage }}
                    </el-button>
                    <PermissionGate :permission-n-id="PERMISSIONS.collaborationMessagingRetract">
                      <el-button text role="menuitem" type="warning" @click="retract(message)">
                        {{ copy.retract }}
                      </el-button>
                    </PermissionGate>
                    <el-button
                      text
                      role="menuitem"
                      type="danger"
                      @click="deleteForCurrentUser(message)"
                    >
                      {{ copy.deleteForMe }}
                    </el-button>
                  </div>
                </div>
              </article>
              <span
                v-if="!loadingMessages && messages.length === 0"
                class="collaboration-chat__hint"
                >{{ copy.noMessages }}</span
              >
            </div>
            <footer v-if="canWrite" class="collaboration-chat__composer">
              <div
                v-if="pendingAttachment"
                class="collaboration-chat__attachment-queue"
                role="status"
              >
                {{ pendingAttachment.fileName }} · {{ uploadState }}
              </div>
              <div
                v-else-if="uploadState"
                class="collaboration-chat__attachment-queue"
                role="status"
              >
                {{ uploadState }}
              </div>
              <textarea
                ref="composer"
                v-model="messageDraft"
                :maxlength="4000"
                :placeholder="
                  props.terminal === 'pda' ? copy.pdaMessagePlaceholder : copy.messagePlaceholder
                "
                @input="onDraftInput"
                @keydown="onComposerKeydown"
              />
              <div class="collaboration-chat__composer-actions">
                <label class="collaboration-chat__file-button"
                  ><span>{{ copy.attachment }}</span
                  ><input type="file" :disabled="sending" @change="onFileChange"
                /></label>
                <span class="collaboration-chat__counter">{{ messageDraft.length }} / 4000</span>
                <button
                  type="button"
                  :disabled="
                    sending || (messageDraft.trim().length === 0 && pendingAttachment === null)
                  "
                  @click="sendMessage"
                >
                  {{ sending ? copy.sending : copy.send }}
                </button>
              </div>
            </footer>
          </template>
          <div v-else class="collaboration-chat__empty">
            <strong>{{ copy.selectConversation }}</strong
            ><span>{{ copy.selectConversationDescription }}</span>
          </div>
        </section>

        <aside
          v-if="detailsOpen && selectedConversation !== null && !isTerminalList"
          class="collaboration-chat__details"
          :aria-label="copy.details"
        >
          <h2>{{ copy.details }}</h2>
          <dl>
            <dt>{{ copy.person }}</dt>
            <dd>{{ displayConversation(selectedConversation) }}</dd>
            <dt>{{ copy.userNId }}</dt>
            <dd>{{ selectedConversation.peerUserNId }}</dd>
            <dt>{{ copy.presence }}</dt>
            <dd>{{ presenceLabel(selectedConversation) }}</dd>
            <dt>{{ copy.lastMessage }}</dt>
            <dd>{{ formatTime(selectedConversation.lastMessageOn) || copy.noMessages }}</dd>
          </dl>
        </aside>
      </div>
    </section>
  </component>
</template>

<style scoped>
.collaboration-chat {
  position: relative;
  display: flex;
  flex-direction: column;
  height: max(520px, calc(100dvh - 210px));
  min-height: 0;
  gap: var(--ip-space-3);
  overflow: hidden;
}
.collaboration-chat-surface,
.collaboration-chat--drawer {
  height: 100%;
  min-height: 0;
}
.collaboration-chat--drawer {
  gap: 0;
}
.collaboration-chat__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}
.collaboration-chat__unread {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.collaboration-chat__new {
  position: relative;
  width: min(360px, 100%);
}
.collaboration-chat__new input,
.collaboration-chat__filter {
  box-sizing: border-box;
  width: 100%;
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
}
.collaboration-chat__directory {
  position: absolute;
  z-index: 10;
  top: calc(100% + 4px);
  right: 0;
  left: 0;
  display: grid;
  max-height: 260px;
  overflow: auto;
  padding: var(--ip-space-1);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}
.collaboration-chat__directory button {
  display: flex;
  justify-content: space-between;
  gap: var(--ip-space-2);
  padding: var(--ip-space-2);
  text-align: left;
  border: 0;
  background: transparent;
  color: var(--ip-color-text-primary);
  cursor: pointer;
}
.collaboration-chat__directory button:hover,
.collaboration-chat__directory button:focus-visible {
  background: var(--ip-color-bg-muted);
}
.collaboration-chat__directory small {
  color: var(--ip-color-text-secondary);
}
.collaboration-chat__body {
  display: grid;
  grid-template-columns: minmax(240px, 300px) minmax(0, 1fr);
  min-height: 0;
  flex: 1;
  overflow: hidden;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  background: var(--ip-color-bg-container);
}
.collaboration-chat__sidebar {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  border-right: 1px solid var(--ip-color-border);
  overflow: hidden;
}
.collaboration-chat__filters {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__conversation-list {
  display: grid;
  min-height: 0;
  flex: 1;
  gap: var(--ip-space-1);
  overflow: auto;
}
.collaboration-chat__conversation {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
  height: 72px;
  min-height: 72px;
  padding: var(--ip-space-2);
  border: 1px solid transparent;
  border-radius: var(--ip-radius-md);
  background: transparent;
  color: var(--ip-color-text-primary);
  text-align: left;
  cursor: pointer;
}
.collaboration-chat__conversation:hover,
.collaboration-chat__conversation:focus-visible,
.collaboration-chat__conversation.is-active {
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}
.collaboration-chat__conversation-copy {
  display: grid;
  min-width: 0;
  flex: 1;
  gap: 3px;
}
.collaboration-chat__avatar {
  position: relative;
  display: inline-flex;
  width: 40px;
  height: 40px;
  flex: 0 0 40px;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--ip-color-bg-muted);
  color: var(--ip-color-text-primary);
  font-weight: 600;
}
.collaboration-chat__avatar .collaboration-chat__presence-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  border: 2px solid var(--ip-color-bg-container);
}
.collaboration-chat__conversation-meta {
  display: grid;
  flex: 0 0 auto;
  justify-items: end;
  gap: 4px;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__conversation-meta time {
  max-width: 74px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.collaboration-chat__conversation-copy strong,
.collaboration-chat__conversation-copy small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.collaboration-chat__conversation-copy small,
.collaboration-chat__message-meta time,
.collaboration-chat__conversation-header span {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__presence {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.collaboration-chat__presence-dot {
  width: 8px;
  height: 8px;
  flex: 0 0 8px;
  border-radius: 50%;
  background: var(--ip-color-text-secondary);
}
.collaboration-chat__presence-dot.is-online {
  background: var(--ip-color-success);
}
.collaboration-chat__presence-dot.is-offline {
  background: var(--ip-color-text-secondary);
}
.collaboration-chat__presence-dot.is-unknown {
  border: 1px solid var(--ip-color-border-strong);
  background: transparent;
}
.collaboration-chat__badge {
  display: inline-flex;
  min-width: 20px;
  justify-content: center;
  padding: 2px 6px;
  border-radius: 999px;
  background: var(--ip-color-primary);
  color: var(--ip-color-text-on-primary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__hidden {
  color: var(--ip-color-text-secondary);
  font-size: 10px;
}
.collaboration-chat__main {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
}
.collaboration-chat__conversation-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  padding: var(--ip-space-3) var(--ip-space-4);
  border-bottom: 1px solid var(--ip-color-border);
}
.collaboration-chat__conversation-header h2 {
  margin: 0 0 4px;
  overflow-wrap: anywhere;
  font-size: var(--ip-font-size-lg);
}
.collaboration-chat__conversation-header div:first-child {
  min-width: 0;
}
.collaboration-chat__header-actions,
.collaboration-chat__composer-actions {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
}
.collaboration-chat button {
  font: inherit;
}
.collaboration-chat__header-actions button,
.collaboration-chat__back,
.collaboration-chat__history,
.collaboration-chat__retract {
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-secondary);
  cursor: pointer;
}
.collaboration-chat__read-receipt {
  display: block;
  margin-top: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  text-align: right;
}
.collaboration-chat__message-actions {
  position: relative;
  display: flex;
  justify-content: flex-end;
  margin-top: var(--ip-space-1);
}
.collaboration-chat__action-menu {
  position: fixed;
  z-index: 4;
  top: 0;
  left: 0;
  display: grid;
  min-width: 112px;
  padding: var(--ip-space-1);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  box-shadow: var(--ip-shadow-md);
}
.collaboration-chat__messages {
  display: flex;
  min-height: 0;
  flex: 1;
  flex-direction: column;
  gap: var(--ip-space-3);
  overflow: auto;
  padding: var(--ip-space-4);
}
.collaboration-chat__history {
  align-self: center;
}
.collaboration-chat__message {
  max-width: min(680px, 80%);
  align-self: flex-start;
  padding: var(--ip-space-3);
  border-radius: var(--ip-radius-lg);
  background: var(--ip-color-bg-muted);
}
.collaboration-chat__message.is-own {
  align-self: flex-end;
  background: color-mix(in srgb, var(--ip-color-primary) 14%, var(--ip-color-bg-container));
}
.collaboration-chat__message-meta {
  display: flex;
  align-items: baseline;
  gap: var(--ip-space-2);
}
.collaboration-chat__message-meta time {
  margin-left: auto;
}
.collaboration-chat__message-text {
  margin: var(--ip-space-2) 0 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.collaboration-chat__attachment,
.collaboration-chat__attachment-queue {
  margin-top: var(--ip-space-2);
  padding: var(--ip-space-2);
  border: 1px dashed var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
}
.collaboration-chat__tombstone {
  margin: var(--ip-space-2) 0 0;
  color: var(--ip-color-text-secondary);
  font-style: italic;
}
.collaboration-chat__retract {
  margin-top: var(--ip-space-2);
  min-height: 28px;
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__composer {
  display: grid;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3) var(--ip-space-4);
  border-top: 1px solid var(--ip-color-border);
}
.collaboration-chat__composer textarea {
  width: 100%;
  min-height: 76px;
  box-sizing: border-box;
  resize: vertical;
  padding: var(--ip-space-2);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font: inherit;
}
.collaboration-chat__composer-actions {
  justify-content: flex-end;
}
.collaboration-chat__file-button,
.collaboration-chat__composer-actions > button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 40px;
  padding: 0 var(--ip-space-3);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  cursor: pointer;
}
.collaboration-chat__file-button input {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
.collaboration-chat__composer-actions > button {
  border-color: var(--ip-color-primary);
  background: var(--ip-color-primary);
  color: var(--ip-color-text-on-primary);
}
.collaboration-chat__composer-actions > button:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}
.collaboration-chat__counter {
  margin-right: auto;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__details {
  width: 240px;
  padding: var(--ip-space-4);
  border-left: 1px solid var(--ip-color-border);
  overflow: auto;
}
.collaboration-chat__details h2 {
  margin: 0 0 var(--ip-space-4);
  font-size: var(--ip-font-size-md);
}
.collaboration-chat__details dl {
  display: grid;
  gap: var(--ip-space-2);
  margin: 0;
}
.collaboration-chat__details dt {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.collaboration-chat__details dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.collaboration-chat__empty,
.collaboration-chat__hint {
  display: grid;
  place-items: center;
  min-height: 80px;
  padding: var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  text-align: center;
}
.collaboration-chat__empty {
  flex: 1;
  gap: var(--ip-space-2);
}
.collaboration-chat__empty strong {
  color: var(--ip-color-text-primary);
}
.collaboration-chat__error {
  margin: 0;
  padding: var(--ip-space-2);
  color: var(--ip-color-danger);
  background: color-mix(in srgb, var(--ip-color-danger) 8%, transparent);
  border-radius: var(--ip-radius-md);
}
.collaboration-chat__conversation-list,
.collaboration-chat__messages,
.collaboration-chat__details {
  scrollbar-color: transparent transparent;
  scrollbar-width: thin;
}
.collaboration-chat__conversation-list::-webkit-scrollbar,
.collaboration-chat__messages::-webkit-scrollbar,
.collaboration-chat__details::-webkit-scrollbar {
  width: 8px;
}
.collaboration-chat__conversation-list::-webkit-scrollbar-thumb,
.collaboration-chat__messages::-webkit-scrollbar-thumb,
.collaboration-chat__details::-webkit-scrollbar-thumb {
  border-radius: 999px;
  background: transparent;
}
.collaboration-chat__conversation-list:hover,
.collaboration-chat__conversation-list:focus-within,
.collaboration-chat__messages:hover,
.collaboration-chat__messages:focus-within,
.collaboration-chat__details:hover,
.collaboration-chat__details:focus-within {
  scrollbar-color: var(--ip-color-border-strong) transparent;
}
.collaboration-chat__conversation-list:hover::-webkit-scrollbar-thumb,
.collaboration-chat__conversation-list:focus-within::-webkit-scrollbar-thumb,
.collaboration-chat__messages:hover::-webkit-scrollbar-thumb,
.collaboration-chat__messages:focus-within::-webkit-scrollbar-thumb,
.collaboration-chat__details:hover::-webkit-scrollbar-thumb,
.collaboration-chat__details:focus-within::-webkit-scrollbar-thumb {
  background: var(--ip-color-border-strong);
}
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
@media (max-width: 820px) {
  .collaboration-chat__body {
    grid-template-columns: minmax(180px, 34%) minmax(0, 1fr);
  }
  .collaboration-chat__details {
    position: absolute;
    inset: 100px 12px 12px auto;
    z-index: 5;
    box-shadow: var(--ip-shadow-md);
  }
  .collaboration-chat__message {
    max-width: 92%;
  }
}
@media (max-width: 640px) {
  .collaboration-chat:not(.collaboration-chat--drawer) {
    min-height: calc(100dvh - 170px);
  }
  .collaboration-chat__toolbar {
    align-items: stretch;
    flex-direction: column;
  }
  .collaboration-chat__new {
    width: 100%;
  }
  .collaboration-chat__body {
    grid-template-columns: minmax(180px, 34%) minmax(0, 1fr);
  }
  .collaboration-chat__sidebar {
    border-right: 0;
    border-bottom: 1px solid var(--ip-color-border);
  }
  .collaboration-chat__conversation-header {
    padding: var(--ip-space-3);
    flex-wrap: wrap;
    gap: var(--ip-space-2);
  }
  .collaboration-chat__conversation-header h2 {
    font-size: var(--ip-font-size-md);
  }
  .collaboration-chat__header-actions button {
    min-height: 40px;
  }
  .collaboration-chat__composer {
    padding: var(--ip-space-3);
  }
  .collaboration-chat__header-actions,
  .collaboration-chat__composer-actions {
    flex-wrap: wrap;
  }
  .collaboration-chat__details {
    inset: 12px;
    width: auto;
    max-width: none;
  }
  .collaboration-chat__file-button,
  .collaboration-chat__composer-actions > button {
    min-height: var(--ip-touch-min-size-mobile);
  }
}
.collaboration-chat--terminal .collaboration-chat__body {
  grid-template-columns: minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
}
.collaboration-chat--terminal .collaboration-chat__sidebar {
  border: 0;
}
.collaboration-chat__back {
  flex-shrink: 0;
  align-self: flex-start;
  min-height: 48px;
  margin: var(--ip-space-2);
}
@media (prefers-reduced-motion: reduce) {
  * {
    scroll-behavior: auto !important;
  }
}
</style>
