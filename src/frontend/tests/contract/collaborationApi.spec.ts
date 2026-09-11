import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'

import { createCollaborationApi } from '@/api/collaboration'
import { createHttpClient } from '@/api/httpClient'

import { server } from '../fixtures/mswServer'

const BASE = 'http://localhost:5080'

describe('Collaboration HTTP contract', () => {
  beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())

  it('sends a complete JSON read-cursor body using the lossless sequence contract', async () => {
    let requestBody: unknown
    let contentType: string | null = null
    server.use(
      http.put(
        `${BASE}/collaboration/api/v1/conversations/CV-1/read-cursor`,
        async ({ request }) => {
          requestBody = await request.json()
          contentType = request.headers.get('content-type')
          return HttpResponse.json({
            success: true,
            code: '200',
            message: 'success',
            data: { lastReadSequence: '17', unreadCount: '0' },
          })
        },
      ),
    )

    const api = createCollaborationApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await api.markRead('CV-1', 17)

    expect(requestBody).toEqual({ sequence: '17' })
    expect(contentType).toContain('application/json')
  })

  it('uses the per-user visibility endpoint with an explicit hidden body', async () => {
    let requestBody: unknown
    server.use(
      http.put(
        `${BASE}/collaboration/api/v1/conversations/CV-1/messages/MSG-1/personal-visibility`,
        async ({ request }) => {
          requestBody = await request.json()
          return HttpResponse.json({
            success: true,
            code: '200',
            message: 'success',
            data: { messageNId: 'MSG-1', hidden: true },
          })
        },
      ),
    )

    const api = createCollaborationApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await expect(api.setPersonalMessageVisibility('CV-1', 'MSG-1', true)).resolves.toEqual({
      messageNId: 'MSG-1',
      hidden: true,
    })
    expect(requestBody).toEqual({ hidden: true })
  })

  it('normalizes lossless summary-preview sequences before rendering the conversation list', async () => {
    server.use(
      http.get(`${BASE}/collaboration/api/v1/conversations`, () =>
        HttpResponse.json({
          success: true,
          code: '200',
          message: 'success',
          data: {
            items: [
              {
                conversationNId: 'CV-1',
                peerUserNId: 'U-2',
                peerDisplayName: 'Peer',
                status: 'Active',
                lastMessageSequence: '17',
                lastMessageNId: 'MSG-17',
                lastMessageOn: '2026-09-10T00:00:00.000Z',
                lastMessagePreview: {
                  messageNId: 'MSG-17',
                  sequence: '17',
                  acceptedOn: '2026-09-10T00:00:00.000Z',
                  messageType: 'Text',
                  state: 'Accepted',
                  text: 'safe preview',
                },
                unreadCount: '1',
                optimisticVersion: '1',
                concurrencyVersion: 'conversation-v1',
              },
            ],
            nextCursor: null,
            total: '1',
          },
        }),
      ),
    )

    const api = createCollaborationApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await expect(api.listConversations()).resolves.toMatchObject({
      items: [
        {
          lastMessageSequence: 17,
          lastMessagePreview: { sequence: 17, text: 'safe preview' },
        },
      ],
    })
  })
})
