import { fileURLToPath, URL } from 'node:url'
import { readFileSync } from 'node:fs'

import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ command, mode }) => {
  const studio = mode === 'studio'
  const collaboration = mode === 'lan-https-collaboration' || mode === 'collaboration'
  // 两个 HTTPS 入口复用 LAN 证书，普通 LAN HTTP 调试不加载证书。
  const httpsRequired = mode === 'lan-https' || mode === 'lan-https-collaboration'
  const env = loadEnv(httpsRequired ? 'lan' : mode, process.cwd(), '')
  if (command === 'build' && !collaboration && !env.VITE_DEPLOYMENT_ENVIRONMENT) {
    throw new Error('构建客户端页面前必须显式设置 VITE_DEPLOYMENT_ENVIRONMENT=DEV|TEST|UAT|PROD。')
  }
  const lan = mode === 'lan' || httpsRequired
  const certPath = env.DEV_HTTPS_CERT
  const keyPath = env.DEV_HTTPS_KEY
  const packageVersion = (
    JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as {
      version: string
    }
  ).version
  const webVersion = process.env.VITE_WEB_VERSION || env.VITE_WEB_VERSION || packageVersion
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/u.test(webVersion)) {
    throw new Error('VITE_WEB_VERSION必须是SemVer，例如1.2.3。')
  }
  if (httpsRequired && (!certPath || !keyPath)) {
    throw new Error(
      'HTTPS证书未配置。先运行 tools/setup-lan-https.ps1，参见 docs/agents/局域网调试入口.md。',
    )
  }
  return {
    // loadEnv只读取配置，不会更改Vite按mode注入的import.meta.env。
    // lan-https也必须把同一LAN API入口注入页面，避免落回.env.local的localhost。
    plugins: [
      vue(),
      ...(studio
        ? [
            {
              name: 'studio-entry',
              transformIndexHtml: {
                order: 'pre' as const,
                handler(html: string) {
                  return html.replace('/src/main.ts', '/src/studio-main.ts')
                },
              },
            },
          ]
        : []),
      {
        name: 'industrial-platform-web-version',
        generateBundle() {
          this.emitFile({
            type: 'asset',
            fileName: 'runtime-version.json',
            source: JSON.stringify({ schemaVersion: 1, webVersion }, null, 2),
          })
        },
      },
    ],
    define: {
      ...(lan || collaboration
        ? {
            'import.meta.env.VITE_API_BASE_URL': JSON.stringify(
              collaboration ? '/_backend' : env.VITE_API_BASE_URL || '/_backend',
            ),
            ...(collaboration
              ? {
                  'import.meta.env.VITE_AUTH_MODE': JSON.stringify('embedded'),
                  'import.meta.env.VITE_DEPLOYMENT_ENVIRONMENT': JSON.stringify(
                    env.VITE_DEPLOYMENT_ENVIRONMENT || 'PROD',
                  ),
                }
              : {}),
          }
        : {}),
      'import.meta.env.VITE_WEB_VERSION': JSON.stringify(webVersion),
    },
    cacheDir: process.env.VITE_CACHE_DIR ?? 'node_modules/.vite',
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: 5173,
      ...(studio
        ? {
            proxy: {
              '/api': {
                target: env.DEV_STUDIO_API_PROXY_TARGET || 'http://localhost:5258',
                changeOrigin: true,
              },
            },
          }
        : {}),
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
      outDir: studio ? 'dist-studio' : mode === 'collaboration' ? 'dist-collaboration' : 'dist',
    },
  }
})
