import { fileURLToPath, URL } from 'node:url'
import { readFileSync } from 'node:fs'

import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const collaboration = mode === 'lan-https-collaboration'
  // 两个 HTTPS 入口复用 LAN 证书，普通 LAN HTTP 调试不加载证书。
  const httpsRequired = mode === 'lan-https' || collaboration
  const env = loadEnv(httpsRequired ? 'lan' : mode, process.cwd(), '')
  const lan = mode === 'lan' || httpsRequired
  const certPath = env.DEV_HTTPS_CERT
  const keyPath = env.DEV_HTTPS_KEY
  if (httpsRequired && (!certPath || !keyPath)) {
    throw new Error(
      'HTTPS证书未配置。先运行 tools/setup-lan-https.ps1，参见 docs/agents/局域网调试入口.md。',
    )
  }
  return {
    // loadEnv只读取配置，不会更改Vite按mode注入的import.meta.env。
    // lan-https也必须把同一LAN API入口注入页面，避免落回.env.local的localhost。
    ...(lan
      ? {
          define: {
            'import.meta.env.VITE_API_BASE_URL': JSON.stringify(
              collaboration ? '/_backend' : env.VITE_API_BASE_URL || '/_backend',
            ),
            // 独立宿主使用自己的会话，不能继承平台 .env.local 的 http 登录方式。
            ...(collaboration
              ? { 'import.meta.env.VITE_AUTH_MODE': JSON.stringify('embedded') }
              : {}),
          },
        }
      : {}),
    plugins: [vue()],
    cacheDir: process.env.VITE_CACHE_DIR ?? 'node_modules/.vite',
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      ...(lan
        ? {
            host: '0.0.0.0',
            strictPort: true,
            // HTTP is Vite's default; the certificate paths below are only
            // applied when the selected mode explicitly requires HTTPS.
            ...(httpsRequired && certPath && keyPath
              ? { https: { cert: readFileSync(certPath), key: readFileSync(keyPath) } }
              : {}),
            proxy: {
              '/_backend': {
                // 独立宿主单独覆盖端口，避免平台代理配置串到独立调试中。
                target: collaboration
                  ? env.DEV_COLLABORATION_API_PROXY_TARGET || 'http://localhost:56365'
                  : env.DEV_API_PROXY_TARGET || 'http://localhost:5041',
                changeOrigin: true,
                ws: true,
                rewrite: (path: string) => path.replace(/^\/_backend(?=\/|$)/, ''),
              },
            },
          }
        : {}),
    },
    preview: {
      port: 4173,
    },
    build: {
      outDir: 'dist',
    },
  }
})
