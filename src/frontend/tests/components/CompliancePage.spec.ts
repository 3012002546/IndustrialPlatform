import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import type { CollaborationApi } from '@/api/collaboration'
import CompliancePage from '@/pages/pc/collaboration/CompliancePage.vue'
import { useAuthStore } from '@/stores/authStore'

const mocks = vi.hoisted(() => ({
  api: {
    searchCompliance: vi.fn(),
    createStepUp: vi.fn(),
    createStepUpContext: vi.fn(),
    listLegalHolds: vi.fn(),
    listExports: vi.fn(),
    getRetention: vi.fn(),
  },
}))

vi.mock('@/api/collaborationRegistry', () => ({
  getCollaborationApi: () => mocks.api as unknown as CollaborationApi,
}))

const SlotStub = defineComponent({ template: '<div><slot /><slot name="actions" /></div>' })
const ElementStub = defineComponent({ template: '<span><slot /></span>' })
const PageStub = defineComponent({
  props: { title: { type: String, default: '' } },
  template: '<section><h1>{{ title }}</h1></section>',
})
const QueryPanelStub = defineComponent({
  emits: ['reset', 'submit'],
  template:
    '<form data-testid="controlled-view-query" @submit.prevent="$emit(\'submit\')"><slot /><button type="submit">Search</button></form>',
})
const InputStub = defineComponent({
  inheritAttrs: false,
  props: { modelValue: { type: String, default: '' } },
  emits: ['update:modelValue'],
  template:
    '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
})
const StepUpDrawerStub = defineComponent({
  props: { modelValue: Boolean },
  emits: ['confirm', 'update:modelValue'],
  template:
    '<button v-if="modelValue" data-testid="step-up-confirm" @click="$emit(\'confirm\', \'password\')">Confirm</button>',
})
const DataTableStub = defineComponent({
  props: { rows: { type: Array, default: () => [] } },
  template: '<div data-testid="search-result-count">{{ rows.length }}</div>',
})
const compliancePageSource = readFileSync(
  resolve(process.cwd(), 'src/pages/pc/collaboration/CompliancePage.vue'),
  'utf8',
)

describe('CompliancePage controlled view', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      user: {
        userId: 'U-1',
        username: 'reviewer',
        displayName: 'Reviewer',
        tenantId: 'T-1',
        roles: ['reviewer'],
        permissions: ['collaboration.compliance.read', 'collaboration.compliance.view'],
        mustChangePassword: false,
      },
    })
    vi.clearAllMocks()
    mocks.api.createStepUpContext.mockResolvedValue({ binding: 'step-up-binding' })
    mocks.api.createStepUp.mockResolvedValue({ proof: 'step-up-proof' })
    mocks.api.listLegalHolds.mockResolvedValue({ items: [] })
    mocks.api.listExports.mockResolvedValue({ items: [] })
    mocks.api.getRetention.mockResolvedValue(null)
    mocks.api.searchCompliance.mockResolvedValue({
      items: [
        {
          acceptedOn: '2026-09-10T00:00:00Z',
          conversationNId: 'conversation-1',
          messageNId: 'message-1',
          messageType: 'Text',
          senderUserNId: 'user-1',
          sequence: 1,
          state: 'Accepted',
          textContent: null,
        },
      ],
      nextCursor: null,
    })
  })

  it('opens with query guidance only and does not fetch sensitive results or prompt for re-authentication', async () => {
    const wrapper = mount(CompliancePage, {
      props: { page: 'controlled-view' },
      global: {
        plugins: [pinia],
        stubs: {
          AppPage: SlotStub,
          AppQueryPanel: SlotStub,
          AppDataTable: SlotStub,
          AppFormDrawer: SlotStub,
          CollaborationStepUpDrawer: SlotStub,
          PermissionGate: SlotStub,
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('页面不会自动读取敏感消息')
    expect(mocks.api.searchCompliance).not.toHaveBeenCalled()
    expect(mocks.api.createStepUpContext).not.toHaveBeenCalled()
  })

  it('renders the specified Chinese title for each compliance route', () => {
    const titles = [
      ['controlled-view', '受控查看'],
      ['legal-holds', '保全案件'],
      ['exports', '导出记录'],
      ['retention', '保留策略'],
    ] as const

    for (const [page, title] of titles) {
      const wrapper = mount(CompliancePage, {
        props: { page },
        global: {
          plugins: [pinia],
          stubs: {
            AppPage: PageStub,
            AppQueryPanel: SlotStub,
            AppDataTable: SlotStub,
            AppFormDrawer: SlotStub,
            CollaborationStepUpDrawer: SlotStub,
            PermissionGate: SlotStub,
            'el-button': ElementStub,
            'el-checkbox': ElementStub,
            'el-date-picker': ElementStub,
            'el-dropdown': ElementStub,
            'el-dropdown-item': ElementStub,
            'el-dropdown-menu': ElementStub,
            'el-input': ElementStub,
            'el-input-number': ElementStub,
            'el-switch': ElementStub,
          },
        },
      })

      expect(wrapper.get('h1').text()).toBe(title)
      wrapper.unmount()
    }
  })

  it('scopes semantic VXE dark-table tokens to the compliance consumer', () => {
    expect(compliancePageSource).toContain(
      '.collaboration-compliance :deep(.vxe-table--render-default)',
    )
    expect(compliancePageSource).toContain('--vxe-ui-font-color: var(--ip-color-text-primary)')
    expect(compliancePageSource).toContain(
      '--vxe-ui-layout-background-color: var(--ip-color-bg-container)',
    )
    expect(compliancePageSource).toContain(
      '--vxe-ui-table-header-background-color: var(--ip-color-bg-muted)',
    )
    expect(compliancePageSource).toContain(
      '--vxe-ui-table-footer-background-color: var(--ip-color-bg-container)',
    )
    expect(compliancePageSource).toContain('--vxe-ui-table-border-color: var(--ip-color-border)')
    expect(compliancePageSource).toContain(
      '--vxe-ui-input-placeholder-color: var(--ip-color-text-tertiary)',
    )
    expect(compliancePageSource).toMatch(
      /--vxe-ui-table-row-hover-background-color:\s*var\(\s*--collaboration-compliance-table-hover-background/,
    )
    expect(compliancePageSource).toMatch(
      /--vxe-ui-table-row-current-background-color:\s*var\(\s*--collaboration-compliance-table-selection-background/,
    )
    expect(compliancePageSource).toMatch(
      /--vxe-ui-table-row-checkbox-checked-background-color:\s*var\(\s*--collaboration-compliance-table-selection-background/,
    )
    expect(compliancePageSource).toContain('.vxe-table--empty-placeholder)')
  })

  it('uses a silent bound authorization for SYSTEM_ADMIN without displaying a password prompt', async () => {
    useAuthStore().$patch({ session: { user: { roles: ['SYSTEM_ADMIN'] } } })
    const wrapper = mount(CompliancePage, {
      props: { page: 'controlled-view' },
      global: {
        plugins: [pinia],
        stubs: {
          AppPage: SlotStub,
          AppQueryPanel: QueryPanelStub,
          AppDataTable: DataTableStub,
          AppFormDrawer: SlotStub,
          CollaborationStepUpDrawer: StepUpDrawerStub,
          PermissionGate: SlotStub,
          'el-input': InputStub,
        },
      },
    })
    await wrapper.get('[data-testid="controlled-view-query"]').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[data-testid="step-up-confirm"]').exists()).toBe(false)
    expect(mocks.api.createStepUp).toHaveBeenCalledWith({
      binding: 'step-up-binding',
      currentPassword: '',
      password: '',
    })
    expect(mocks.api.searchCompliance).toHaveBeenCalledWith(expect.any(Object), 'step-up-proof')
    wrapper.unmount()
  })

  it('omits a blank keyword from both the Step-Up context and the proof-backed controlled search', async () => {
    const wrapper = mount(CompliancePage, {
      props: { page: 'controlled-view' },
      global: {
        plugins: [pinia],
        stubs: {
          AppPage: SlotStub,
          AppQueryPanel: QueryPanelStub,
          AppDataTable: DataTableStub,
          AppFormDrawer: SlotStub,
          CollaborationStepUpDrawer: StepUpDrawerStub,
          PermissionGate: SlotStub,
          'el-input': InputStub,
        },
      },
    })

    const keywordInput = wrapper.findAll('input')[1]
    if (!keywordInput) throw new Error('Expected the controlled-view keyword input.')
    await keywordInput.setValue('   ')
    await wrapper.get('[data-testid="controlled-view-query"]').trigger('submit')
    await wrapper.get('[data-testid="step-up-confirm"]').trigger('click')
    await flushPromises()

    const contextCall = mocks.api.createStepUpContext.mock.calls[0]
    const searchCall = mocks.api.searchCompliance.mock.calls[0]
    if (!contextCall || !searchCall)
      throw new Error('Expected the Step-Up context and controlled search.')
    const contextRequest = contextCall[0] as Record<string, unknown>
    const searchRequest = searchCall[0] as Record<string, unknown>

    expect(contextRequest).not.toHaveProperty('keyword')
    expect(searchRequest).not.toHaveProperty('keyword')
    expect(searchCall[1]).toBe('step-up-proof')
    expect(wrapper.get('[data-testid="search-result-count"]').text()).toBe('1')
  })
})
