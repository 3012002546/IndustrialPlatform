/** 视口信息读取:路由/Store 的唯一取数点,便于测试注入。 */

export interface ViewportInfo {
  width: number
  hasTouch: boolean
}

/** 读取当前视口;SSR 或 matchMedia 不可用时安全降级。 */
export function getViewportInfo(): ViewportInfo {
  if (typeof window === 'undefined') return { width: 1280, hasTouch: false }
  const hasTouch = window.matchMedia?.('(pointer: coarse)').matches ?? false
  return { width: window.innerWidth, hasTouch }
}
