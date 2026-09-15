<template>
  <main class="embedded-session-required" data-testid="embedded-session-required">
    <h1>MES 登录会话不可用</h1>
    <p>请先在 MES 中完成登录，再从 MES 重新打开协作页面。</p>
    <p class="embedded-session-required__hint">平台不会在嵌入页面中索要或保存 MES 密码。</p>
  </main>
</template>

<style scoped>
.embedded-session-required {
  box-sizing: border-box;
  min-height: 100vh;
  display: grid;
  place-content: center;
  gap: 12px;
  padding: 32px;
  color: var(--ip-color-text-primary, #1f2937);
  background: var(--ip-color-bg-page, #f5f7fa);
}

.embedded-session-required h1,
.embedded-session-required p {
  margin: 0;
}

.embedded-session-required__hint {
  color: var(--ip-color-text-secondary, #6b7280);
}
</style>
