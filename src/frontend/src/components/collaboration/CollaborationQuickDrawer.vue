<script setup lang="ts">
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import CollaborationChat from '@/components/collaboration/CollaborationChat.vue'
import { computed, watch } from 'vue'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useCollaborationChatStore } from '@/stores/collaborationChatStore'

const props = defineProps<{
  modelValue: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration)
const chatSession = useCollaborationChatStore()

watch(
  () => props.modelValue,
  (open) => chatSession.setQuickDrawerOpen(open),
  { immediate: true },
)
</script>

<template>
  <AppFormDrawer
    :model-value="modelValue"
    :title="copy.quickDrawerTitle"
    size="chat"
    hide-footer
    :allow-mode-switch="false"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <CollaborationChat surface="drawer" :active="modelValue" />
  </AppFormDrawer>
</template>
