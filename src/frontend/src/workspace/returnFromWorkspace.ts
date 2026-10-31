import type { Router, RouteLocationRaw } from 'vue-router'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { buildTabId } from './identity'

/** Navigate through leave guards before removing the page's workspace tab. */
export async function returnFromWorkspace(router: Router, target: RouteLocationRaw): Promise<void> {
  const from = router.currentRoute.value
  const sourceId = buildTabId(String(from.name ?? ''), from.params, from.query)
  const targetPath = router.resolve(target).fullPath
  const tabs = useWorkspaceTabsStore()
  const failure = await router.push(target)
  if (!failure && router.currentRoute.value.fullPath === targetPath) tabs.closeTab(sourceId)
}
