import { contextBridge, ipcRenderer } from "electron";

function invoke<T>(channel: string, ...args: unknown[]): Promise<T> {
  return ipcRenderer.invoke(channel, ...args) as Promise<T>;
}

contextBridge.exposeInMainWorld("industrialPlatformRuntime", {
  capabilities: {
    get: () => invoke("runtime:capabilities"),
  },
  authStorage: {
    load: (key: string) =>
      invoke<string | null>("runtime:auth-storage:load", key),
    save: (key: string, value: string) =>
      invoke<void>("runtime:auth-storage:save", key, value),
    clear: (key: string) => invoke<void>("runtime:auth-storage:clear", key),
  },
  devices: {
    list: () => invoke("runtime:devices:list"),
  },
  config: {
    get: () => invoke("runtime:config:get"),
    update: (input: unknown) => invoke("runtime:config:update", input),
  },
  permissions: {
    openSettings: (capability: string) =>
      invoke("runtime:permissions:open-settings", capability),
  },
  files: {
    pick: (input: unknown) => invoke("runtime:files:pick", input),
    save: (input: unknown) => invoke("runtime:files:save", input),
  },
  printer: {
    getCapabilities: (profileNId: string) =>
      invoke("runtime:printer:get-capabilities", profileNId),
    printTest: (profileNId: string) =>
      invoke("runtime:printer:print-test", profileNId),
  },
  notifications: {
    show: (input: unknown) => invoke("runtime:notifications:show", input),
  },
  diagnostics: {
    export: (input: { consent: true }) =>
      invoke("runtime:diagnostics:export", input),
  },
  lifecycle: {
    exit: () => invoke<void>("runtime:lifecycle:exit"),
    subscribe: (listener: (event: unknown) => void) => {
      const handler = (_event: Electron.IpcRendererEvent, value: unknown) =>
        listener(value);
      ipcRenderer.on("runtime:lifecycle", handler);
      return () => ipcRenderer.removeListener("runtime:lifecycle", handler);
    },
  },
  media: {
    getScreenSources: () => invoke("runtime:media:screen-sources"),
    releaseScreenSource: (sourceNId: string) =>
      invoke<void>("runtime:media:release-screen-source", sourceNId),
  },
  updater: {
    confirmHealthy: (input: { webVersion: string }) =>
      invoke("runtime:updater:confirm-healthy", input),
    check: () => invoke("runtime:updater:check"),
    download: async (
      target: unknown,
      onProgress?: (downloadedBytes: string, totalBytes?: string) => void,
    ) => {
      const handler = (
        _event: Electron.IpcRendererEvent,
        value: { downloadedBytes?: unknown; totalBytes?: unknown },
      ) => {
        if (typeof value?.downloadedBytes !== "string") return;
        onProgress?.(
          value.downloadedBytes,
          typeof value.totalBytes === "string" ? value.totalBytes : undefined,
        );
      };
      if (onProgress !== undefined)
        ipcRenderer.on("runtime:update-progress", handler);
      try {
        await invoke<void>("runtime:updater:download", target);
      } finally {
        if (onProgress !== undefined)
          ipcRenderer.removeListener("runtime:update-progress", handler);
      }
    },
    verify: (target: unknown) => invoke<void>("runtime:updater:verify", target),
    activate: (target: unknown, activationNId: string) =>
      invoke<void>("runtime:updater:activate", target, activationNId),
    rollback: (target: unknown) =>
      invoke<void>("runtime:updater:rollback", target),
  },
});
