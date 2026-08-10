/**
 * 共享 MSW(node)服务:契约测试用。
 * 各测试通过 server.use(...) 注入 handler;onUnhandledRequest 设为 error,
 * 保证没有遗漏的意外网络请求。
 */
import { setupServer } from 'msw/node'

export const server = setupServer()
