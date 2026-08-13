<script setup lang="ts">
/**
 * DEV-only 工作区标签沙箱(PF-01 §7.9 测试支撑):
 * 仅注册于 `import.meta.env.DEV`,生产构建不含此路由与导航入口。
 * 用 query.slot 区分标签身份,供 12→13 上限阻断/关闭/复用/恢复 E2E 使用;
 * 不渲染任何假 KPI 或业务数据。
 */

import { useRoute } from 'vue-router'

const route = useRoute()
const slot = typeof route.query.slot === 'string' ? route.query.slot : 'base'
</script>

<template>
  <div class="ip-sandbox" data-testid="workspace-tabs-sandbox">
    <h1 class="ip-sandbox__title">工作区沙箱 · {{ slot }}</h1>
    <p class="ip-sandbox__hint">DEV 专用页面:用于业务标签上限/复用/关闭回归,不在生产构建中。</p>
    <nav class="ip-sandbox__nav" aria-label="沙箱槽位切换">
      <RouterLink
        v-for="n in 13"
        :key="n"
        class="ip-sandbox__slot-link"
        :to="{ name: 'workspace-tabs-sandbox', query: { slot: String(n - 1) } }"
      >
        槽 {{ n - 1 }}
      </RouterLink>
    </nav>
  </div>
</template>

<style scoped>
.ip-sandbox {
  padding: var(--ip-space-6);
}

.ip-sandbox__title {
  margin: 0 0 var(--ip-space-3);
  font-size: var(--ip-font-size-xl);
}

.ip-sandbox__hint {
  margin: 0 0 var(--ip-space-4);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-md);
}

.ip-sandbox__nav {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
}

.ip-sandbox__slot-link {
  display: inline-flex;
  align-items: center;
  padding: var(--ip-space-1) var(--ip-space-3);
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  font-size: var(--ip-font-size-sm);
  text-decoration: none;
}

.ip-sandbox__slot-link:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}
</style>
