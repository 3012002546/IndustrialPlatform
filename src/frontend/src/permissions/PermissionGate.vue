<script setup lang="ts">
/**
 * 按钮/片段级权限控制(§13.2)。
 * - mode='hide'(默认):无权限时不渲染(可提供 #denied 插槽显示替代内容)。
 * - mode='disabled':无权限时仍渲染,但默认插槽收到 disabled=true(消费方绑定到按钮)。
 */
import { computed } from 'vue'

import { useAuthStore } from '@/stores/authStore'

const props = withDefaults(
  defineProps<{
    /** 所需权限 NId(§9.2 目录)。 */
    permissionNId: string
    /** 无权限时的呈现方式。 */
    mode?: 'hide' | 'disabled'
  }>(),
  { mode: 'hide' },
)

const authStore = useAuthStore()
const allowed = computed(() => authStore.hasPermission(props.permissionNId))
</script>

<template>
  <slot v-if="allowed" />
  <slot v-else-if="mode === 'disabled'" :disabled="true" />
  <slot v-else name="denied" />
</template>
