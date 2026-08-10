<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import AppPage from '@/components/base/AppPage.vue'
import { ROUTE_NAMES } from '@/router/routes'

const router = useRouter()
const route = useRoute()

/** 原始路径(§15.4):经 Vue 插值渲染,文本自动进行 HTML 转义,不解析为 HTML。 */
const rawPath = computed(() => route.fullPath)

function goHome(): void {
  void router.push({ name: ROUTE_NAMES.root })
}

/**
 * 是否有可返回的上一条历史。
 * 生产(web history)在首个 entry 时 back 为 null;memory history 的 state 不携带 back 字段,
 * 取值恒为 undefined,一并按“无历史”回落首页,避免调用失效的 back()。
 */
function canGoBack(): boolean {
  return router.options.history.state.back != null
}

function goBack(): void {
  if (canGoBack()) {
    router.back()
  } else {
    goHome()
  }
}
</script>

<template>
  <main class="not-found-page">
    <AppPage title="页面不存在">
      <p class="not-found-page__path">未找到路径:{{ rawPath }}</p>
      <div class="not-found-page__actions">
        <button
          type="button"
          class="not-found-page__btn not-found-page__btn--primary"
          data-testid="go-home"
          @click="goHome"
        >
          返回首页
        </button>
        <button type="button" class="not-found-page__btn" data-testid="go-back" @click="goBack">
          返回上一页
        </button>
      </div>
    </AppPage>
  </main>
</template>

<style scoped>
.not-found-page {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: var(--ip-space-6) var(--ip-space-4);
  background: var(--ip-color-bg-page);
}

.not-found-page__path {
  margin: 0;
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-secondary);
  font-family: ui-monospace, monospace;
}

.not-found-page__actions {
  display: flex;
  gap: var(--ip-space-2);
}

.not-found-page__btn {
  box-sizing: border-box;
  height: 36px;
  padding: 0 var(--ip-space-4);
  border: 1px solid var(--ip-color-border-strong);
  border-radius: var(--ip-radius-md);
  background: var(--ip-color-bg-container);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  cursor: pointer;
}

.not-found-page__btn--primary {
  border-color: var(--ip-color-primary);
  background: var(--ip-color-primary);
  color: #fff;
}
</style>
