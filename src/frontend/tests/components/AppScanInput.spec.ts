import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppScanInput from '@/components/runtime/AppScanInput.vue'
import { getScannerRuntime } from '@/platform/runtime/scannerRuntime'

describe('AppScanInput', () => {
  it('keeps the label quiet while editing and emits a result on Enter', async () => {
    const wrapper = mount(AppScanInput, {
      props: {
        ownerPageId: 'pda.runtime.scanner-test',
        modelValue: '',
        showCamera: false,
      },
    })

    const input = wrapper.get('input')
    await input.setValue('000123ABC')
    expect(wrapper.emitted('result')).toBeUndefined()

    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('result')?.[0]?.[0]).toMatchObject({
      value: '000123ABC',
      source: 'keyboard',
    })
  })

  it('still accepts broadcast results when hand input is disabled', async () => {
    const runtime = getScannerRuntime()
    const wrapper = mount(AppScanInput, {
      props: {
        ownerPageId: 'pda.runtime.scanner-test',
        mode: 'broadcast',
        inputDisabled: true,
        showCamera: true,
      },
    })
    const sessionId = wrapper.get('input').attributes('data-session-id')

    expect(wrapper.get('input').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="scan-camera"]').exists()).toBe(false)
    runtime.publish({
      sessionNId: sessionId ?? '',
      subjectEpoch: runtime.subjectEpoch,
      source: 'broadcast',
      value: 'broadcast-value',
    })
    await flushPromises()

    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('broadcast-value')
    expect(wrapper.emitted('result')?.[0]?.[0]).toMatchObject({ value: 'broadcast-value' })
  })

  it('keeps a manual confirmation as a keyboard result in broadcast mode', async () => {
    const wrapper = mount(AppScanInput, {
      props: {
        ownerPageId: 'pda.runtime.scanner-test',
        mode: 'broadcast',
        inputDisabled: false,
        showCamera: true,
      },
    })

    await wrapper.get('input').setValue('manual-broadcast-value')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('result')?.[0]?.[0]).toMatchObject({
      value: 'manual-broadcast-value',
      source: 'keyboard',
    })
  })

  it('clears the value and ignores a result from the previous session', async () => {
    const runtime = getScannerRuntime()
    const wrapper = mount(AppScanInput, {
      props: { ownerPageId: 'pda.runtime.scanner-test', showCamera: false },
    })
    const oldSessionId = wrapper.get('input').attributes('data-session-id') ?? ''
    await wrapper.get('input').setValue('old')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    await wrapper.get('[data-testid="scan-clear"]').trigger('click')
    await flushPromises()

    runtime.publish({
      sessionNId: oldSessionId,
      subjectEpoch: runtime.subjectEpoch,
      source: 'keyboard',
      value: 'stale',
    })
    await flushPromises()

    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
    expect(wrapper.emitted('result')?.map(([result]) => (result as { value: string }).value)).toEqual([
      'old',
    ])
  })
})
