import { describe, expect, it } from 'vitest'

import { createCorrelationId, extractTraceId } from '@/api/correlation'

describe('createCorrelationId', () => {
  it('returns a UUIDv4-shaped string', () => {
    expect(createCorrelationId()).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    )
  })

  it('returns distinct ids per call', () => {
    expect(createCorrelationId()).not.toBe(createCorrelationId())
  })
})

describe('extractTraceId', () => {
  it('extracts from the response body traceId field', () => {
    expect(extractTraceId({ traceId: 'body-trace' }, undefined)).toBe('body-trace')
  })

  it('prefers the body traceId over the header', () => {
    expect(extractTraceId({ traceId: 'body' }, { 'X-Trace-Id': 'header' })).toBe('body')
  })

  it('extracts from the X-Trace-Id header', () => {
    expect(extractTraceId(null, { 'X-Trace-Id': 'h-trace' })).toBe('h-trace')
    expect(extractTraceId(null, { 'x-trace-id': 'h-trace-lower' })).toBe('h-trace-lower')
  })

  it('extracts the trace id segment from traceparent', () => {
    expect(extractTraceId(null, { traceparent: '00-abc123def-00-01' })).toBe('abc123def')
  })

  it('returns undefined when nothing is present', () => {
    expect(extractTraceId(null, undefined)).toBeUndefined()
    expect(extractTraceId({}, { 'content-type': 'application/json' })).toBeUndefined()
  })
})
