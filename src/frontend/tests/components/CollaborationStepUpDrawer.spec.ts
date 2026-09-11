import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import CollaborationStepUpDrawer from '@/components/collaboration/CollaborationStepUpDrawer.vue'

vi.mock('element-plus/es/components/focus-trap/index', async () => {
  const { defineComponent } = await import('vue')
  return {
    ElFocusTrap: defineComponent({ template: '<div><slot /></div>' }),
    default: defineComponent({ template: '<div><slot /></div>' }),
  }
})

const TeleportStub = defineComponent({
  props: { to: { type: String, required: true } },
  template: '<div><slot /></div>',
})
const InputStub = defineComponent({
  inheritAttrs: false,
  props: { modelValue: { type: String, default: '' }, disabled: Boolean },
  emits: ['update:modelValue'],
  template:
    '<input v-bind="$attrs" :value="modelValue" :disabled="disabled" @input="$emit(\'update:modelValue\', $event.target.value)" />',
})
const ButtonStub = defineComponent({
  inheritAttrs: false,
  props: { disabled: Boolean },
  template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
})

const wrappers: VueWrapper[] = []

describe('CollaborationStepUpDrawer', () => {
  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  })

  it('shows a readable actor/action/scope and clears the password after a failed attempt', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(CollaborationStepUpDrawer, {
      props: {
        modelValue: true,
        actor: 'Alice (U-1)',
        actionLabel: 'Approve export',
        scopeSummary: 'Conversation: C-1',
      },
      global: {
        plugins: [createPinia()],
        stubs: { teleport: TeleportStub, 'el-input': InputStub, 'el-button': ButtonStub },
      },
    })
    wrappers.push(wrapper)

    expect(wrapper.text()).toContain('Alice (U-1)')
    expect(wrapper.text()).toContain('Approve export')
    expect(wrapper.text()).toContain('Conversation: C-1')
    expect(wrapper.text()).not.toContain('compliance.export.approve')
    await wrapper.get('[data-testid="step-up-password"]').setValue('secret')
    await wrapper.setProps({ error: 'authentication failed' })
    expect(
      (wrapper.get('[data-testid="step-up-password"]').element as HTMLInputElement).value,
    ).toBe('')
  })

  it('prevents duplicate submit while busy', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(CollaborationStepUpDrawer, {
      props: {
        modelValue: true,
        actor: 'Alice (U-1)',
        actionLabel: 'Save policy',
        scopeSummary: 'Current authorized scope',
        busy: true,
      },
      global: {
        plugins: [createPinia()],
        stubs: { teleport: TeleportStub, 'el-input': InputStub, 'el-button': ButtonStub },
      },
    })
    wrappers.push(wrapper)

    expect(
      (wrapper.get('[data-testid="step-up-submit"]').element as HTMLButtonElement).disabled,
    ).toBe(true)
    await wrapper.get('[data-testid="step-up-submit"]').trigger('click')
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })
})
