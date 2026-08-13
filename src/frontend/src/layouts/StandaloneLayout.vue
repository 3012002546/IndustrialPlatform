<script setup lang="ts">
/** 独立页壳(§26.6):不带导航的整屏页面容器,子路由自行居中排版。 */
</script>

<template>
  <main class="standalone-layout">
    <router-view />
  </main>
</template>

<style scoped>
.standalone-layout {
  min-height: 100vh;
  background: var(--ip-color-bg-page);
}
</style>
