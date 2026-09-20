<script setup lang="ts">
import { computed, ref } from 'vue'

import AppPage from '@/components/base/AppPage.vue'
import AppScanInput from '@/components/runtime/AppScanInput.vue'
import { systemDataPageCopy } from '@/localization/systemData'
import { usePlatformLocale } from '@/localization/localeContext'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'

const props = defineProps<{ pageId: string }>()
const locale = usePlatformLocale()
const copy = computed(() => systemDataPageCopy(locale.value, 'scannerTest'))
const authStore = useAuthStore()
const canConfigure = computed(() => authStore.hasPermission(PERMISSIONS.runtimeConfigure))
const inputValue = ref('')
const lastBarcode = ref('')
const scanInput = ref<InstanceType<typeof AppScanInput> | null>(null)

async function clearResult(): Promise<void> {
  lastBarcode.value = ''
  await scanInput.value?.clear()
}
</script>

<template>
  <AppPage class="scanner-test-page" :title="copy.title" :description="copy.description">
    <section class="scanner-test-page__surface" data-testid="scanner-test-page">
      <AppScanInput
        ref="scanInput"
        v-model="inputValue"
        :title="copy.label"
        :owner-page-id="props.pageId"
        :input-disabled="!canConfigure"
        :show-camera="canConfigure"
        @result="lastBarcode = $event.value"
      />
      <p v-if="!canConfigure" class="scanner-test-page__permission" role="status">
        {{ copy.configureRequired }}
      </p>

      <section class="scanner-test-page__result" :aria-labelledby="`${props.pageId}-result-title`">
        <div class="scanner-test-page__result-header">
          <div>
            <h2 :id="`${props.pageId}-result-title`">{{ copy.result }}</h2>
            <p>{{ copy.resultDescription }}</p>
          </div>
          <button type="button" data-testid="scanner-test-clear" @click="clearResult">
            {{ copy.clear }}
          </button>
        </div>
        <output class="scanner-test-page__result-value" data-testid="scanner-test-result">
          {{ lastBarcode || copy.emptyResult }}
        </output>
      </section>
    </section>
  </AppPage>
</template>

<style scoped>
.scanner-test-page {
  min-width: 0;
}

.scanner-test-page__surface {
  display: grid;
  gap: var(--ip-space-6);
  padding: var(--ip-space-5);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.scanner-test-page__result {
  display: grid;
  gap: var(--ip-space-3);
  padding-top: var(--ip-space-5);
  border-top: 1px solid var(--ip-color-border);
}

.scanner-test-page__permission {
  margin: var(--ip-space-3) 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.scanner-test-page__result-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--ip-space-4);
}

.scanner-test-page__result h2,
.scanner-test-page__result p {
  margin: 0;
}

.scanner-test-page__result h2 {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-lg);
}

.scanner-test-page__result p {
  margin-top: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.scanner-test-page__result-header button {
  min-height: var(--ip-touch-min-size);
  padding: 0 var(--ip-space-4);
  color: var(--ip-color-primary);
  background: transparent;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.scanner-test-page__result-value {
  display: block;
  min-height: 72px;
  padding: var(--ip-space-4);
  overflow-wrap: anywhere;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-page);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  line-height: var(--ip-line-height-normal);
  white-space: pre-wrap;
}

@media (max-width: 600px) {
  .scanner-test-page__surface {
    padding: var(--ip-space-4);
  }

  .scanner-test-page__result-header {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>
