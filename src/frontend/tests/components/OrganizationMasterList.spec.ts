import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it } from 'vitest'

import OrganizationMasterList from '@/components/systemData/OrganizationMasterList.vue'

const nodes = [
  {
    tenantNId: 'tenant-a',
    nId: 'org-root',
    name: '总部',
    type: 'Company',
    status: 'Active',
    parentOrganizationNId: null,
    displayOrder: 0,
    children: [
      {
        tenantNId: 'tenant-a',
        nId: 'org-dept',
        name: '研发部',
        type: 'Department',
        status: 'Active',
        parentOrganizationNId: 'org-root',
        displayOrder: 0,
        children: [
          {
            tenantNId: 'tenant-a',
            nId: 'org-team',
            name: '平台组',
            type: 'Team',
            status: 'Active',
            parentOrganizationNId: 'org-dept',
            displayOrder: 0,
            children: [],
          },
        ],
      },
    ],
  },
]

describe('OrganizationMasterList', () => {
  it('preserves hierarchy and exposes selectable compact cards', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(OrganizationMasterList, {
      props: { nodes, selectedNId: null },
      global: { plugins: [pinia, ElementPlus] },
    })

    expect(wrapper.find('[data-testid="organization-card-org-root"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="organization-card-org-dept"]').text()).toContain('总部')

    await wrapper.get('[data-testid="organization-card-org-dept"]').trigger('click')
    expect(wrapper.emitted('select')?.at(-1)).toEqual(['org-dept'])
  })

  it('hides descendants when an ancestor is collapsed and keeps only search context', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(OrganizationMasterList, {
      props: { nodes, selectedNId: null },
      global: { plugins: [pinia, ElementPlus] },
    })

    await wrapper.get('button').trigger('focus')
    await wrapper.get('.organization-master-list__toolbar button:nth-of-type(3)').trigger('click')
    expect(wrapper.find('[data-testid="organization-card-org-root"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="organization-card-org-dept"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="organization-card-org-team"]').exists()).toBe(false)

    const search = wrapper.get('input')
    await search.setValue('平台组')
    expect(wrapper.find('[data-testid="organization-card-org-root"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="organization-card-org-dept"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="organization-card-org-team"]').exists()).toBe(true)
  })
})
