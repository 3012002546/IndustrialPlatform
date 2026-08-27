<script setup lang="ts">
import AssignmentsAdminPage from './AssignmentsAdminPage.vue'
import FeaturesAdminPage from './FeaturesAdminPage.vue'
import NavigationAdminPage from './NavigationAdminPage.vue'
import OrganizationsAdminPage from './OrganizationsAdminPage.vue'
import ServiceInitializationAdminPage from './ServiceInitializationAdminPage.vue'
import ServicesAdminPage from './ServicesAdminPage.vue'
import ThemesAdminPage from './ThemesAdminPage.vue'
import type { SystemDataAdminKind } from '@/stores/systemData/managementStore'

const props = defineProps<{
  kind: SystemDataAdminKind
  title: string
  description?: string
  permission?: string
}>()
const forwardedPermission = props.permission ?? ''
</script>

<template>
  <OrganizationsAdminPage
    v-if="props.kind === 'organizations'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <AssignmentsAdminPage
    v-else-if="props.kind === 'assignments'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <NavigationAdminPage
    v-else-if="props.kind === 'navigation'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <FeaturesAdminPage
    v-else-if="props.kind === 'features'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <ServicesAdminPage
    v-else-if="props.kind === 'services'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <ThemesAdminPage
    v-else-if="props.kind === 'themes'"
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
  <ServiceInitializationAdminPage
    v-else
    :title="props.title"
    :description="props.description ?? ''"
    :permission="forwardedPermission"
  />
</template>
