<script setup lang="ts">
import { computed } from 'vue'
import { ElButton, ElCard, ElDescriptions, ElDescriptionsItem } from 'element-plus'
import { useRouter } from 'vue-router'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'

const router = useRouter()
const authStore = useAuthStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.top)
const user = computed(() => authStore.user)

function openChangePassword(): void {
  void router.push({ name: 'change-password' })
}
</script>

<template>
  <section class="ip-profile-page" data-testid="profile-page">
    <header class="ip-profile-page__header">
      <div>
        <p class="ip-profile-page__eyebrow">{{ copy.profile }}</p>
        <h1>{{ user?.displayName || user?.username || '—' }}</h1>
      </div>
      <ElButton type="primary" @click="openChangePassword">{{ copy.changePassword }}</ElButton>
    </header>
    <ElCard shadow="never">
      <ElDescriptions :column="1" border>
        <ElDescriptionsItem :label="copy.profileAccount">{{ user?.username || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="copy.profileName">{{ user?.displayName || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="copy.profileTenant">{{ user?.tenantId || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="copy.profileRoles">{{ user?.roles.join(', ') || '—' }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>
  </section>
</template>

<style scoped>
.ip-profile-page {
  max-width: 820px;
  margin: 0 auto;
}

.ip-profile-page__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--ip-space-4);
  margin-bottom: var(--ip-space-5);
}

.ip-profile-page__eyebrow {
  margin: 0 0 var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

h1 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: clamp(1.5rem, 3vw, 2rem);
}
</style>
