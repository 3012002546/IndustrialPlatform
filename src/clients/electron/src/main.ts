import { existsSync, promises as fs } from "node:fs";
import { randomUUID } from "node:crypto";
import path from "node:path";
import { pathToFileURL } from "node:url";

import {
  app,
  BrowserWindow,
  type IpcMainInvokeEvent,
  desktopCapturer,
  dialog,
  ipcMain,
  Menu,
  nativeImage,
  Notification,
  protocol,
  safeStorage,
  session,
  shell,
  Tray,
} from "electron";

import {
  activeWebRoot,
  activeWebVersion,
  loadPolicy,
  loadSignedManifestSource,
  parseSignedReleaseManifest,
  recoverExpiredWebActivation,
  TrustedUpdateStore,
  type RuntimePolicy,
  type SignedReleaseManifest,
} from "./runtimeUpdater";

const APP_SCHEME = "app";
const APP_ID = "com.industrialplatform.desktop";
const BASE_WEB_VERSION = process.env.RUNTIME_WEB_VERSION ?? "0.1.0";
const AUTH_FILE = "auth.session.bin";

let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let isQuitting = false;
let updatePolicy: RuntimePolicy | null = null;
let updateStore: TrustedUpdateStore | null = null;
const subjectEpoch = randomUUID().replaceAll("-", "");
let lifecycleSequence = 0;
const lifecycleListeners = new Set<
  (event: {
    state: string;
    subjectEpoch: string;
    lifecycleSequence: string;
  }) => void
>();
const usedNotificationIds = new Set<string>();
const fileHandles = new Map<
  string,
  { sourcePath: string; name: string; sizeBytes: string }
>();
let runtimeConfig = {
  configRevision: "1",
  terminal: "industrial-platform",
  channel: "stable",
  scannerMode: "input" as "input" | "broadcast",
  suffix: "Enter" as "Enter" | "Tab" | "None",
  diagnosticConsent: false,
  environmentNId: "dev",
  deploymentEnvironment: "Development",
};

protocol.registerSchemesAsPrivileged([
  {
    scheme: APP_SCHEME,
    privileges: {
      standard: true,
      secure: true,
      supportFetchAPI: true,
      allowServiceWorkers: true,
    },
  },
]);

function webRoot(): string {
  const fallback = app.isPackaged
    ? path.join(process.resourcesPath, "frontend-dist")
    : path.resolve(__dirname, "../../../frontend/dist");
  return activeWebRoot(runtimeRoot(), fallback);
}

function runtimeRoot(): string {
  return path.join(app.getPath("userData"), "runtime");
}

function bundledRuntimeRoot(): string {
  return app.isPackaged
    ? path.join(process.resourcesPath, "runtime")
    : path.resolve(__dirname, "../resources");
}

function currentWebVersion(): string {
  return activeWebVersion(runtimeRoot(), BASE_WEB_VERSION);
}

function updatePolicyConfigured(): boolean {
  return (
    app.isPackaged &&
    existsSync(path.join(bundledRuntimeRoot(), "runtime-policy.json"))
  );
}

async function trustedUpdateStore(): Promise<TrustedUpdateStore> {
  if (updateStore !== null && updatePolicy !== null) return updateStore;
  const policyPath = path.join(bundledRuntimeRoot(), "runtime-policy.json");
  const policy = await loadPolicy(policyPath);
  updatePolicy = policy;
  updateStore = new TrustedUpdateStore({
    runtimeRoot: runtimeRoot(),
    policy,
    currentWebVersion,
    currentNativeVersion: () => app.getVersion(),
  });
  return updateStore;
}

function signedTarget(value: unknown): SignedReleaseManifest {
  return parseSignedReleaseManifest(JSON.stringify(value));
}

async function availableUpdate(): Promise<SignedReleaseManifest | null> {
  if (!updatePolicyConfigured()) return null;
  const policy =
    updatePolicy ??
    (await loadPolicy(path.join(bundledRuntimeRoot(), "runtime-policy.json")));
  updatePolicy = policy;
  const source =
    policy.updateManifestUrl ??
    pathToFileURL(
      path.join(bundledRuntimeRoot(), "runtime-manifest.json"),
    ).toString();
  const manifest = await loadSignedManifestSource(source, policy);
  return (await (
    await trustedUpdateStore()
  ).check(manifest)) as SignedReleaseManifest | null;
}

function contentType(filePath: string): string {
  const extension = path.extname(filePath).toLowerCase();
  return (
    {
      ".html": "text/html; charset=utf-8",
      ".js": "text/javascript; charset=utf-8",
      ".css": "text/css; charset=utf-8",
      ".json": "application/json; charset=utf-8",
      ".svg": "image/svg+xml",
      ".png": "image/png",
      ".woff": "font/woff",
      ".ttf": "font/ttf",
    }[extension] ?? "application/octet-stream"
  );
}

function trustedSender(event: IpcMainInvokeEvent): void {
  if (event.senderFrame === null) throw new Error("RUNTIME_ORIGIN_DENIED");
  const senderUrl = new URL(event.senderFrame.url);
  if (senderUrl.protocol !== `${APP_SCHEME}:` || senderUrl.hostname !== "app")
    throw new Error("RUNTIME_ORIGIN_DENIED");
}

function emitLifecycle(state: string): void {
  lifecycleSequence += 1;
  const event = {
    state,
    subjectEpoch,
    lifecycleSequence: String(lifecycleSequence),
  };
  mainWindow?.webContents.send("runtime:lifecycle", event);
  for (const listener of lifecycleListeners) listener(event);
}

function safeDisplayName(value: string): string {
  const name = path
    .basename(value)
    .replace(/[\u0000-\u001f<>:"/\\|?*]/gu, "_")
    .trim();
  if (name.length === 0 || name === "." || name === "..")
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  return name.slice(0, 200);
}

async function pickFile(
  event: IpcMainInvokeEvent,
  input: unknown,
): Promise<{
  artifactHandle: string;
  name: string;
  sizeBytes: string;
} | null> {
  trustedSender(event);
  if (
    input === null ||
    typeof input !== "object" ||
    (input as { purpose?: unknown }).purpose !== "collaborationAttachment"
  )
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  const result =
    mainWindow === null
      ? await dialog.showOpenDialog({ properties: ["openFile"] })
      : await dialog.showOpenDialog(mainWindow, { properties: ["openFile"] });
  if (result.canceled || result.filePaths[0] === undefined) return null;
  const sourcePath = result.filePaths[0];
  const stat = await fs.stat(sourcePath);
  const handle = randomUUID().replaceAll("-", "");
  const name = safeDisplayName(path.basename(sourcePath));
  fileHandles.set(handle, { sourcePath, name, sizeBytes: String(stat.size) });
  return { artifactHandle: handle, name, sizeBytes: String(stat.size) };
}

async function saveFile(
  event: IpcMainInvokeEvent,
  input: unknown,
): Promise<{ saved: boolean }> {
  trustedSender(event);
  if (input === null || typeof input !== "object")
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  const value = input as { artifactHandle?: unknown; displayName?: unknown };
  if (
    typeof value.artifactHandle !== "string" ||
    typeof value.displayName !== "string"
  )
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  const source = fileHandles.get(value.artifactHandle);
  if (source === undefined) throw new Error("RUNTIME_INVALID_ARGUMENT");
  const saveOptions = { defaultPath: safeDisplayName(value.displayName) };
  const destination =
    mainWindow === null
      ? await dialog.showSaveDialog(saveOptions)
      : await dialog.showSaveDialog(mainWindow, saveOptions);
  if (destination.canceled || destination.filePath === undefined)
    return { saved: false };
  await fs.copyFile(source.sourcePath, destination.filePath);
  fileHandles.delete(value.artifactHandle);
  return { saved: true };
}

async function exportDiagnostics(
  event: IpcMainInvokeEvent,
  input: unknown,
): Promise<{
  artifactHandle: string;
  name: string;
  sizeBytes: string;
}> {
  trustedSender(event);
  if (
    input === null ||
    typeof input !== "object" ||
    (input as { consent?: unknown }).consent !== true
  )
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  const name = `industrial-platform-diagnostics-${Date.now()}.json`;
  const sourcePath = path.join(app.getPath("temp"), name);
  const body = JSON.stringify(
    {
      schemaVersion: 1,
      exportedOn: new Date().toISOString(),
      appId: APP_ID,
      nativeVersion: app.getVersion(),
      webVersion: currentWebVersion(),
      platform: process.platform,
      osVersion: process.getSystemVersion(),
      updateState: "host-local",
    },
    null,
    2,
  );
  await fs.writeFile(sourcePath, body, { encoding: "utf8", mode: 0o600 });
  const handle = randomUUID().replaceAll("-", "");
  fileHandles.set(handle, {
    sourcePath,
    name,
    sizeBytes: String(Buffer.byteLength(body)),
  });
  return {
    artifactHandle: handle,
    name,
    sizeBytes: String(Buffer.byteLength(body)),
  };
}

async function printTest(
  event: IpcMainInvokeEvent,
  profileNId: unknown,
): Promise<{
  submission: "SubmittedToSystem" | "Cancelled";
  receiptLevel: "SystemOnly";
}> {
  trustedSender(event);
  if (typeof profileNId !== "string" || profileNId.length === 0)
    throw new Error("RUNTIME_INVALID_ARGUMENT");
  if (mainWindow === null)
    return { submission: "Cancelled", receiptLevel: "SystemOnly" };
  const printWindow = new BrowserWindow({
    show: false,
    webPreferences: {
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  await printWindow.loadURL(
    `data:text/html;charset=utf-8,${encodeURIComponent('<!doctype html><title>Industrial Platform test label</title><main style="font:24px sans-serif;padding:24px">Industrial Platform test label</main>')}`,
  );
  return new Promise((resolve) => {
    printWindow.webContents.print(
      { silent: false, printBackground: false },
      (success) => {
        printWindow.destroy();
        resolve({
          submission: success ? "SubmittedToSystem" : "Cancelled",
          receiptLevel: "SystemOnly",
        });
      },
    );
  });
}

function safeWebPath(requestUrl: string): string | null {
  const url = new URL(requestUrl);
  const requested =
    decodeURIComponent(url.pathname).replace(/^\/+/, "") || "index.html";
  const root = path.resolve(webRoot());
  const candidate = path.resolve(root, requested);
  if (candidate !== root && !candidate.startsWith(`${root}${path.sep}`))
    return null;
  return candidate;
}

async function registerAppProtocol(): Promise<void> {
  protocol.handle(APP_SCHEME, async (request) => {
    const filePath = safeWebPath(request.url);
    if (filePath === null) return new Response("Not Found", { status: 404 });
    try {
      const body = await fs.readFile(filePath);
      return new Response(body, {
        headers: { "content-type": contentType(filePath) },
      });
    } catch {
      if (path.extname(filePath) === "") {
        try {
          const body = await fs.readFile(path.join(webRoot(), "index.html"));
          return new Response(body, {
            headers: { "content-type": "text/html; charset=utf-8" },
          });
        } catch {
          // Keep the real 404 when the active bundle has no entry page.
        }
      }
      return new Response("Not Found", { status: 404 });
    }
  });
}

async function loadAuth(key: string): Promise<string | null> {
  if (!isAllowedAuthKey(key) || !safeStorage.isEncryptionAvailable())
    throw new Error("RUNTIME_SECURE_STORAGE_UNAVAILABLE");
  try {
    const encoded = await fs.readFile(authPath(key), "utf8");
    return safeStorage.decryptString(Buffer.from(encoded, "base64"));
  } catch {
    return null;
  }
}

async function saveAuth(key: string, value: string): Promise<void> {
  if (!isAllowedAuthKey(key) || !safeStorage.isEncryptionAvailable())
    throw new Error("RUNTIME_SECURE_STORAGE_UNAVAILABLE");
  const destination = authPath(key);
  const directory = path.dirname(destination);
  await fs.mkdir(directory, { recursive: true });
  const encrypted = safeStorage.encryptString(value).toString("base64");
  const temporary = `${destination}.tmp`;
  await fs.writeFile(temporary, encrypted, { encoding: "utf8", mode: 0o600 });
  await fs.rename(temporary, destination);
}

function authPath(key: string): string {
  const suffix =
    key === "industrial-platform.auth.single.v1" ? "single" : "http";
  return path.join(app.getPath("userData"), `${AUTH_FILE}.${suffix}`);
}

function isAllowedAuthKey(key: string): boolean {
  return (
    key === "industrial-platform.auth.http.v1" ||
    key === "industrial-platform.auth.single.v1"
  );
}

async function clearAuth(key: string): Promise<void> {
  if (!isAllowedAuthKey(key)) throw new Error("RUNTIME_INVALID_ARGUMENT");
  await fs.rm(authPath(key), { force: true });
}

function registerRuntimeIpc(): void {
  ipcMain.handle("runtime:capabilities", (event) => {
    trustedSender(event);
    return {
      contractVersion: 1,
      container: "electron",
      os: process.platform,
      osVersion: process.getSystemVersion(),
      nativeVersion: app.getVersion(),
      webVersion: currentWebVersion(),
      bridgeContractVersion: 1,
      capabilities: [
        { name: "scanner.keyboard", availability: "Supported" },
        { name: "scanner.camera", availability: "Supported" },
        {
          name: "scanner.broadcast",
          availability: "Unsupported",
          reasonCode: "CAPABILITY_NOT_VERIFIED",
        },
        {
          name: "bluetooth.ble",
          availability: "Unsupported",
          reasonCode: "CAPABILITY_NOT_VERIFIED",
        },
        { name: "media.screenCapture", availability: "Supported" },
        { name: "media.microphone", availability: "Supported" },
        { name: "files.pick", availability: "Supported" },
        { name: "files.save", availability: "Supported" },
        { name: "printer.systemTest", availability: "Supported" },
        { name: "notifications.local", availability: "Supported" },
        { name: "diagnostics.export", availability: "Supported" },
        {
          name: "updates.webBundle",
          availability: updatePolicyConfigured() ? "Supported" : "Degraded",
          ...(updatePolicyConfigured()
            ? {}
            : { reasonCode: "UPDATE_CHANNEL_NOT_CONFIGURED" }),
        },
        {
          name: "updates.installer",
          availability: updatePolicyConfigured() ? "Supported" : "Degraded",
          ...(updatePolicyConfigured()
            ? {}
            : { reasonCode: "UPDATE_CHANNEL_NOT_CONFIGURED" }),
        },
      ],
      subjectEpoch,
      snapshotSequence: "1",
    };
  });
  ipcMain.handle("runtime:auth-storage:load", (event, key: unknown) => {
    trustedSender(event);
    return loadAuth(String(key ?? ""));
  });
  ipcMain.handle(
    "runtime:auth-storage:save",
    (event, key: unknown, value: unknown) => {
      trustedSender(event);
      return saveAuth(String(key ?? ""), String(value ?? ""));
    },
  );
  ipcMain.handle("runtime:auth-storage:clear", (event, key: unknown) => {
    trustedSender(event);
    return clearAuth(String(key ?? ""));
  });
  ipcMain.handle("runtime:devices:list", (event) => {
    trustedSender(event);
    return [];
  });
  ipcMain.handle("runtime:media:screen-sources", async (event) => {
    trustedSender(event);
    const sources = await desktopCapturer.getSources({
      types: ["screen", "window"],
      thumbnailSize: { width: 320, height: 180 },
      fetchWindowIcons: true,
    });
    return sources.map((source) => ({
      sourceNId: source.id,
      title: source.name,
      thumbnailDataUrl: source.thumbnail.isEmpty()
        ? undefined
        : source.thumbnail.toDataURL(),
    }));
  });
  ipcMain.handle("runtime:media:release-screen-source", (event) => {
    trustedSender(event);
    return undefined;
  });
  ipcMain.handle("runtime:config:get", (event) => {
    trustedSender(event);
    return runtimeConfig;
  });
  ipcMain.handle("runtime:config:update", (event, value: unknown) => {
    trustedSender(event);
    if (value === null || typeof value !== "object")
      throw new Error("RUNTIME_INVALID_ARGUMENT");
    const input = value as {
      expectedRevision?: unknown;
      scannerMode?: unknown;
      suffix?: unknown;
      diagnosticConsent?: unknown;
    };
    if (
      input.expectedRevision !== runtimeConfig.configRevision ||
      (input.scannerMode !== "input" && input.scannerMode !== "broadcast") ||
      (input.suffix !== "Enter" &&
        input.suffix !== "Tab" &&
        input.suffix !== "None") ||
      typeof input.diagnosticConsent !== "boolean"
    )
      throw new Error(
        input.expectedRevision === runtimeConfig.configRevision
          ? "RUNTIME_INVALID_ARGUMENT"
          : "RUNTIME_CONFIG_CONFLICT",
      );
    runtimeConfig = {
      ...runtimeConfig,
      configRevision: String(Number(runtimeConfig.configRevision) + 1),
      scannerMode: input.scannerMode,
      suffix: input.suffix,
      diagnosticConsent: input.diagnosticConsent,
    };
    return runtimeConfig;
  });
  ipcMain.handle(
    "runtime:permissions:open-settings",
    async (event, capability: unknown) => {
      trustedSender(event);
      const urls: Record<string, string> = {
        "scanner.camera": "ms-settings:privacy-webcam",
        "media.microphone": "ms-settings:privacy-microphone",
        "notifications.local": "ms-settings:notifications",
      };
      const url = typeof capability === "string" ? urls[capability] : undefined;
      if (url === undefined) return { opened: false };
      await shell.openExternal(url);
      return { opened: true };
    },
  );
  ipcMain.handle("runtime:files:pick", (event, value: unknown) =>
    pickFile(event, value),
  );
  ipcMain.handle("runtime:files:save", (event, value: unknown) =>
    saveFile(event, value),
  );
  ipcMain.handle("runtime:diagnostics:export", (event, value: unknown) =>
    exportDiagnostics(event, value),
  );
  ipcMain.handle(
    "runtime:printer:get-capabilities",
    (event, profileNId: unknown) => {
      trustedSender(event);
      if (typeof profileNId !== "string" || profileNId.length === 0)
        throw new Error("RUNTIME_INVALID_ARGUMENT");
      return {
        protocol: "system-dialog",
        receiptLevel: "SystemOnly",
        supportedFormats: ["text/plain"],
      };
    },
  );
  ipcMain.handle("runtime:printer:print-test", (event, profileNId: unknown) =>
    printTest(event, profileNId),
  );
  ipcMain.handle("runtime:notifications:show", (event, value: unknown) => {
    trustedSender(event);
    if (value === null || typeof value !== "object")
      throw new Error("RUNTIME_INVALID_ARGUMENT");
    const input = value as {
      eventNId?: unknown;
      titleKey?: unknown;
      target?: unknown;
    };
    if (
      typeof input.eventNId !== "string" ||
      usedNotificationIds.has(input.eventNId)
    )
      return { accepted: false };
    if (
      input.titleKey !== "newMessage" &&
      input.titleKey !== "collaborationInvite"
    )
      throw new Error("RUNTIME_INVALID_ARGUMENT");
    usedNotificationIds.add(input.eventNId);
    if (!Notification.isSupported()) return { accepted: false };
    const notification = new Notification({
      title: input.titleKey === "newMessage" ? "有新消息" : "有协作邀请",
    });
    notification.show();
    return { accepted: true };
  });
  ipcMain.handle("runtime:lifecycle:exit", (event) => {
    trustedSender(event);
    isQuitting = true;
    app.quit();
  });
  ipcMain.handle("runtime:updater:check", (event) => {
    trustedSender(event);
    return availableUpdate();
  });
  ipcMain.handle(
    "runtime:updater:confirm-healthy",
    async (event, value: unknown) => {
      trustedSender(event);
      if (
        value === null ||
        typeof value !== "object" ||
        typeof (value as { webVersion?: unknown }).webVersion !== "string"
      )
        throw new Error("RUNTIME_INVALID_ARGUMENT");
      if (!updatePolicyConfigured()) return { pending: false, accepted: true };
      return (await trustedUpdateStore()).confirmHealthy(
        (value as { webVersion: string }).webVersion,
      );
    },
  );
  ipcMain.handle("runtime:updater:download", async (event, value: unknown) => {
    trustedSender(event);
    const target = signedTarget(value);
    const store = await trustedUpdateStore();
    await store.download(target, (downloadedBytes, totalBytes) => {
      mainWindow?.webContents.send("runtime:update-progress", {
        downloadedBytes,
        totalBytes,
      });
    });
  });
  ipcMain.handle("runtime:updater:verify", async (event, value: unknown) => {
    trustedSender(event);
    await (await trustedUpdateStore()).verify(signedTarget(value));
  });
  ipcMain.handle(
    "runtime:updater:activate",
    async (event, value: unknown, activationNId: unknown) => {
      trustedSender(event);
      const target = signedTarget(value);
      const store = await trustedUpdateStore();
      if (target.packageKind === "web") {
        await store.activate(target, String(activationNId ?? ""));
        setTimeout(() => {
          void recoverExpiredWebActivation(runtimeRoot()).then((recovered) => {
            if (recovered) mainWindow?.reload();
          });
        }, 60_100);
        return;
      }
      const installerPath = store.verifiedArtifactPath(target);
      if (!app.isPackaged || process.platform !== "win32")
        throw new Error("UPDATE_NATIVE_PLATFORM_UNSUPPORTED");
      const error = await shell.openPath(installerPath);
      if (error.length > 0) throw new Error("UPDATE_NATIVE_INSTALL_FAILED");
      isQuitting = true;
      app.quit();
    },
  );
  ipcMain.handle("runtime:updater:rollback", async (event, value: unknown) => {
    trustedSender(event);
    await (await trustedUpdateStore()).rollback(signedTarget(value));
  });
}

function createWindow(): BrowserWindow {
  const window = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1024,
    minHeight: 720,
    webPreferences: {
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      preload: path.join(__dirname, "preload.js"),
    },
  });
  window.on("close", (event) => {
    if (isQuitting || tray === null) return;
    event.preventDefault();
    window.hide();
  });
  void window.loadURL(`${APP_SCHEME}://app/`);
  return window;
}

function createTray(): Tray {
  const icon = nativeImage.createEmpty();
  const value = new Tray(icon);
  value.setContextMenu(
    Menu.buildFromTemplate([
      { label: "打开平台", click: () => mainWindow?.show() },
      { type: "separator" },
      {
        label: "退出",
        click: () => {
          isQuitting = true;
          app.quit();
        },
      },
    ]),
  );
  value.setToolTip("Industrial Platform");
  return value;
}

async function start(): Promise<void> {
  await recoverExpiredWebActivation(runtimeRoot());
  await registerAppProtocol();
  registerRuntimeIpc();
  session.defaultSession.setDisplayMediaRequestHandler(
    async (_request, callback) => {
      // Do not silently select sources[0]. Ask the user in an explicit native picker.
      const sources = await desktopCapturer.getSources({
        types: ["screen", "window"],
        thumbnailSize: { width: 320, height: 180 },
        fetchWindowIcons: true,
      });
      if (mainWindow === null || sources.length === 0) {
        callback({});
        return;
      }
      const choice = await dialog.showMessageBox(mainWindow, {
        type: "question",
        title: "选择要共享的内容",
        message: "仅共享你明确选择的屏幕或窗口。",
        buttons: [...sources.map((source) => source.name), "取消"],
        cancelId: sources.length,
        noLink: true,
      });
      const source = sources[choice.response];
      callback(source === undefined ? {} : { video: source });
    },
  );
  mainWindow = createWindow();
  mainWindow.on("focus", () => emitLifecycle("foreground"));
  mainWindow.on("blur", () => emitLifecycle("background"));
  app.on("before-quit", () => emitLifecycle("terminating"));
  tray = createTray();
}

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on("second-instance", () => {
    if (mainWindow === null) return;
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.show();
    mainWindow.focus();
  });
  app
    .whenReady()
    .then(start)
    .catch((error: unknown) => {
      console.error(error);
      app.quit();
    });
  app.on("window-all-closed", () => {
    if (process.platform !== "darwin") app.quit();
  });
}
