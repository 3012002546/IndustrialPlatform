import { getStudioApi, type StudioResource } from '@/api/studio'

export async function listDashboardResources(kinds: string[]): Promise<StudioResource[]> {
  const groups = await Promise.all(
    kinds.map(async (kind) => {
      const resources: StudioResource[] = []
      let page = 1
      while (true) {
        const result = await getStudioApi().list(kind, { page, pageSize: 200 })
        resources.push(...result.items)
        if (!result.items.length || resources.length >= Number(result.totalCount)) break
        page++
      }
      return resources.filter((item) => item.isEnabled && item.publishedRevisionNId)
    }),
  )
  return groups.flat()
}
