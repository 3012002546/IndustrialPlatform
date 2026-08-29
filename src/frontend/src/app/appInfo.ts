/**
 * 应用级元数据。
 *
 * FE-001 脚手架用:提供稳定的应用名称/版本/描述,并作为首个可单测的纯模块。
 * FE-002 引入 createIndustrialApp() 后,此处可继续作为装配信息的唯一来源。
 */
export interface AppInfo {
  name: string
  version: string
  description: string
  brandAssetBasePath: string
}

export const APP_INFO: AppInfo = {
  name: 'Industrial Platform',
  version: '0.1.0',
  description: '工业平台统一前端(PC / PDA / Mobile)',
  brandAssetBasePath: '/brand',
}

export function getAppInfo(): AppInfo {
  return APP_INFO
}
