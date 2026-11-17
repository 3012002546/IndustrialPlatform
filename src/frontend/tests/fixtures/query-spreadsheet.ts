// Test-only harness. Production exposes no global workbook or authentication data.
import { createApp, defineComponent, h, ref } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import '@/styles/tokens.css'
import '@/styles/base.css'
import AppQuerySpreadsheet from '@/components/management/AppQuerySpreadsheet.vue'
import type { QueryRequest, SheetCapture } from '@/components/management/querySpreadsheet/types'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useThemeStore } from '@/stores/themeStore'

const count = Number(new URLSearchParams(location.search).get('columns') ?? 8)
const columns = Array.from({ length: count }, (_, i) => ({
  field: 'f' + i,
  title: '字段 ' + (i + 1),
}))
const Component = defineComponent({
  setup() {
    const sheet = ref<InstanceType<typeof AppQuerySpreadsheet>>()
    const visible = ref(true),
      permitted = ref(true)
    const locale = useLocalizationStore(),
      theme = useThemeStore()
    const loader = async ({ pageIndex, pageSize, signal }: QueryRequest) => {
      await new Promise((resolve) => setTimeout(resolve, 50))
      signal.throwIfAborted()
      return {
        items: Array.from(
          { length: Math.min(pageSize, 215 - (pageIndex - 1) * pageSize) },
          (_, row) =>
            Object.fromEntries(
              columns.map((c, col) => [
                c.field,
                col === 0
                  ? '组' + (row % 3)
                  : col === 1
                    ? row + (pageIndex - 1) * pageSize
                    : col === 2
                      ? '=1+2'
                      : '字段值' + col,
              ]),
            ),
        ),
        total: 215,
        pageIndex,
        pageSize,
      }
    }
    Object.assign(window, {
      sheetFixture: {
        capture: () =>
          (sheet.value?.$refs.canvas as { capture(): Promise<SheetCapture> }).capture(),
        reload: () => sheet.value?.reload(),
        unmount: () => {
          visible.value = false
        },
        revoke: () => {
          permitted.value = false
        },
      },
    })
    return () =>
      h('main', [
        h(
          'button',
          { onClick: () => locale.setLocale(locale.locale === 'zh-CN' ? 'en-US' : 'zh-CN') },
          'Language',
        ),
        h(
          'button',
          { onClick: () => theme.setMode(theme.effectiveColorMode === 'dark' ? 'light' : 'dark') },
          'Theme',
        ),
        visible.value
          ? h(AppQuerySpreadsheet, {
              ref: sheet,
              columns,
              loader,
              title: '动态查询验证',
              canExport: true,
              authorizeOutput: async () => {
                if (!permitted.value) throw Error('Export permission revoked')
              },
            })
          : null,
      ])
  },
})
createApp(Component).use(createPinia()).use(ElementPlus).mount('#app')
