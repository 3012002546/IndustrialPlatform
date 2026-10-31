import { ref } from 'vue'

/** Office的脏状态与保存回执；调用方只把已校验origin/source的消息交进来。 */
export function useOfficeSave(
  send: (id: string, values?: Record<string, unknown>) => void,
  failure: (kind: 'notReady' | 'failed' | 'timeout') => string,
) {
  const ready = ref(false),
    modified = ref(false),
    saving = ref(false)
  let epoch = 0,
    saveFailed = false
  let pending:
    | {
        promise: Promise<void>
        resolve: () => void
        reject: (error: Error) => void
        epoch: number
        timer: ReturnType<typeof setTimeout>
      }
    | undefined
  function reset() {
    if (pending) {
      clearTimeout(pending.timer)
      pending.reject(new Error(failure('notReady')))
    }
    pending = undefined
    ready.value = false
    modified.value = false
    saving.value = false
    saveFailed = false
    epoch = 0
  }
  function message(data: { MessageId?: string; Values?: Record<string, unknown> }) {
    const values = data.Values ?? {}
    if (data.MessageId === 'App_LoadingStatus') {
      if (values.Status === 'Frame_Ready') send('Host_PostmessageReady')
      if (values.Status === 'Document_Loaded' && !ready.value) {
        ready.value = true
        // Forward the editor's Save button/shortcut through the same acknowledged save path.
        send('Disable_Default_UIAction', { action: 'UI_Save', disable: true })
      }
    }
    if (data.MessageId === 'Doc_ModifiedStatus' && typeof values.Modified === 'boolean') {
      if (values.Modified) {
        epoch++
        modified.value = true
      } else if (!saving.value && !saveFailed) modified.value = false
    }
    if (data.MessageId === 'Action_Save_Resp' && pending) {
      const own = pending
      pending = undefined
      clearTimeout(own.timer)
      saving.value = false
      if (values.success === true || values.result === 'unmodified') {
        saveFailed = false
        if (epoch === own.epoch) modified.value = false
        own.resolve()
      } else {
        saveFailed = true
        modified.value = true
        own.reject(new Error(failure('failed')))
      }
    }
  }
  function save(): Promise<void> {
    if (pending) return pending.promise
    if (!ready.value) return Promise.reject(new Error(failure('notReady')))
    let resolve!: () => void, reject!: (error: Error) => void
    const promise = new Promise<void>((done, fail) => {
      resolve = done
      reject = fail
    })
    const timer = setTimeout(() => {
      if (pending?.promise !== promise) return
      pending = undefined
      saving.value = false
      saveFailed = true
      modified.value = true
      reject(new Error(failure('timeout')))
    }, 45000)
    pending = { promise, resolve, reject, epoch, timer }
    saving.value = true
    send('Action_Save', { DontTerminateEdit: false, DontSaveIfUnmodified: true, Notify: true })
    return promise
  }
  return { ready, modified, saving, message, save, reset }
}
