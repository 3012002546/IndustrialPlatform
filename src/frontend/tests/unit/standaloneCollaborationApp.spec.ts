import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createStandaloneCollaborationApp } from '@/app/createStandaloneCollaborationApp'

describe('standalone collaboration entry', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'embedded')
    vi.stubEnv('MODE', 'collaboration')
    vi.stubEnv('VITE_API_BASE_URL', window.location.origin)
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  it('rejects an entry without mode=standalone before contacting the host', async () => {
    window.history.replaceState(null, '', '/pc/collaboration')
    const fetcher = vi.fn()
    vi.stubGlobal('fetch', fetcher)
    const app = createStandaloneCollaborationApp()
    const mount = document.createElement('div')
    document.body.appendChild(mount)
    app.mount(mount)
    await vi.waitFor(() => expect(mount.textContent).toContain('独立协作入口地址无效'))
    expect(fetcher).not.toHaveBeenCalled()
    expect(mount.querySelector('[data-testid="standalone-collaboration-entry"]')).not.toBeNull()
    app.unmount()
    mount.remove()
  })

  it('rejects an entry without account before contacting the host', async () => {
    window.history.replaceState(null, '', '/pc/collaboration?mode=standalone')
    const fetcher = vi.fn()
    vi.stubGlobal('fetch', fetcher)
    const app = createStandaloneCollaborationApp()
    const mount = document.createElement('div')
    document.body.appendChild(mount)
    app.mount(mount)
    await vi.waitFor(() => expect(mount.textContent).toContain('独立协作入口地址无效'))
    expect(fetcher).not.toHaveBeenCalled()
    app.unmount()
    mount.remove()
  })

  it('shows a Standalone session error when the server rejects the entry', async () => {
    window.history.replaceState(null, '', '/pc/collaboration?mode=standalone&account=operator-9')
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 })))
    const app = createStandaloneCollaborationApp()
    const mount = document.createElement('div')
    document.body.appendChild(mount)
    app.mount(mount)
    await vi.waitFor(() => expect(mount.textContent).toContain('独立协作会话不可用'))
    expect(mount.textContent).not.toContain('平台用户名密码登录')
    app.unmount()
    mount.remove()
  })
})
