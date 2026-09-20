// 构建时选择入口，使独立产物不打包平台的路由和管理页面。
if (import.meta.env.MODE === 'collaboration' || import.meta.env.MODE === 'lan-https-collaboration') {
  void import('@/app/createStandaloneCollaborationApp').then(({ createStandaloneCollaborationApp }) =>
    createStandaloneCollaborationApp().mount('#app')).catch((error: unknown) => {
      document.querySelector('#app')!.textContent = error instanceof Error ? error.message : '独立协作入口不可用。'
    })
} else if (new URLSearchParams(window.location.search).getAll('mode').includes('standalone')) {
  document.querySelector('#app')!.textContent = '独立协作页面需要使用独立部署入口。'
} else {
  void import('@/app/createIndustrialApp').then(({ createIndustrialApp }) =>
    createIndustrialApp().mount('#app'))
}
