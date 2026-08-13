import skipFormatting from '@vue/eslint-config-prettier/skip-formatting'
import { vueTsConfigs, withVueTs } from '@vue/eslint-config-typescript'
import pluginVue from 'eslint-plugin-vue'

export default withVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },
  {
    name: 'app/files-to-ignore',
    ignores: [
      '**/dist/**',
      '**/coverage/**',
      '**/playwright-report/**',
      '**/playwright-report-real/**',
      '**/test-results/**',
      '**/node_modules/**',
      '**/*.tsbuildinfo',
    ],
  },
  pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,
  skipFormatting,
)
