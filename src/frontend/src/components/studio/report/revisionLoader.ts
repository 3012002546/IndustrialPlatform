import { inject, provide, watch, type InjectionKey } from 'vue'
import { getStudioApi, type StudioRevisionSummary } from '@/api/studio'
import { useAuthStore } from '@/stores/authStore'

// Scoped to a designer, never shared across users or retained after leaving it.
export function createRevisionLoader(fetch: (id: string) => Promise<StudioRevisionSummary[]>) {
  const entries = new Map<string, { result: Promise<StudioRevisionSummary[]>; expires: number }>()
  return {
    clear: () => entries.clear(),
    load(id: string, publishedRevision?: string | null) {
      const key = JSON.stringify([id, publishedRevision]),
        cached = entries.get(key)
      if (cached && cached.expires > Date.now()) return cached.result
      const entry = { result: Promise.resolve([] as StudioRevisionSummary[]), expires: Infinity }
      entry.result = fetch(id).then(
        (rows) => {
          entry.expires = Date.now() + 60_000
          return rows
        },
        (error: unknown) => {
          if (entries.get(key) === entry) entries.delete(key)
          throw error
        },
      )
      entries.set(key, entry)
      return entry.result
    },
  }
}

const revisionLoaderKey: InjectionKey<ReturnType<typeof createRevisionLoader>> =
  Symbol('reportRevisionLoader')

export function provideRevisionLoader() {
  const loader = createRevisionLoader((id) => getStudioApi().revisions(id)),
    auth = useAuthStore()
  watch(() => auth.session, loader.clear, { flush: 'sync' })
  provide(revisionLoaderKey, loader)
}

export function useRevisionLoader() {
  return inject(
    revisionLoaderKey,
    () => createRevisionLoader((id) => getStudioApi().revisions(id)),
    true,
  )
}
