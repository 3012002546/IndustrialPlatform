<script setup lang="ts">
import { getAppInfo } from '@/app/appInfo'
</script>

<template>
  <main class="app-shell">
    <h1 class="app-shell__title">{{ getAppInfo().name }}</h1>
    <p class="app-shell__subtitle">
      统一前端工程已初始化 —— PC / PDA / Mobile 三端壳将在后续任务接入。
    </p>
  </main>
</template>

<style scoped>
.app-shell {
  display: grid;
  min-height: 100vh;
  place-content: center;
  gap: 0.5rem;
  text-align: center;
}

.app-shell__title {
  margin: 0;
  font-size: 1.5rem;
}

.app-shell__subtitle {
  margin: 0;
  color: #666;
}
</style>
