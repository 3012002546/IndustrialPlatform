import { createHash, createPublicKey, verify } from "node:crypto";
import { createReadStream, existsSync } from "node:fs";
import { promises as fs, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import extract from "extract-zip";

export type UpdatePackageKind = "web" | "native";

export interface UpdateManifest {
  releaseNId: string;
  version: string;
  packageKind: UpdatePackageKind;
  artifactHash: string;
  artifactUrl?: string;
  artifactSizeBytes?: string;
  appId?: string;
  environmentNId?: string;
  channel?: string;
  platform?: string;
  releaseSequence?: string;
  keyId?: string;
  signatureAlgorithm?: "ed25519";
  signature?: string;
  nativeMinVersion?: string;
  nativeMaxVersion?: string;
  bridgeContractVersion?: number;
}

export interface RuntimeTrustKey {
  keyId: string;
  algorithm: "ed25519";
  publicKey: string;
}

export interface RuntimePolicy {
  schemaVersion: 1;
  appId: string;
  environmentNId: string;
  channel: string;
  updateManifestUrl?: string;
  updateOrigins: string[];
  trustKeys: RuntimeTrustKey[];
  maxArtifactBytes?: string;
}

export interface SignedReleaseManifest extends UpdateManifest {
  schemaVersion: 1;
  appId: string;
  environmentNId: string;
  channel: string;
  platform: "windows" | "android";
  releaseSequence: string;
  artifactUrl: string;
  keyId: string;
  signatureAlgorithm: "ed25519";
  signature: string;
}

interface ActiveWebSlot {
  schemaVersion: 1;
  slot: string;
  releaseNId: string;
  version: string;
  activationNId: string;
}

interface PendingWebActivation extends ActiveWebSlot {
  healthDeadlineOn: string;
}

interface FailedWebActivation {
  schemaVersion: 1;
  releaseNId: string;
  version: string;
  errorCode: "UPDATE_HEALTH_TIMEOUT";
}

type PendingArtifact = {
  artifactPath: string;
  bundlePath?: string;
  verified?: boolean;
};

const manifestKeys = new Set([
  "schemaVersion",
  "releaseNId",
  "version",
  "packageKind",
  "artifactHash",
  "artifactSizeBytes",
  "artifactUrl",
  "appId",
  "environmentNId",
  "channel",
  "platform",
  "releaseSequence",
  "keyId",
  "signatureAlgorithm",
  "signature",
  "nativeMinVersion",
  "nativeMaxVersion",
  "bridgeContractVersion",
]);

function objectValue(value: unknown): Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value))
    throw new Error("UPDATE_MANIFEST_INVALID");
  return value as Record<string, unknown>;
}

function requiredString(object: Record<string, unknown>, key: string): string {
  const value = object[key];
  if (typeof value !== "string" || value.length === 0)
    throw new Error("UPDATE_MANIFEST_INVALID");
  return value;
}

function canonicalize(value: unknown): string {
  if (value === null || typeof value === "boolean" || typeof value === "string")
    return JSON.stringify(value);
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new Error("UPDATE_MANIFEST_INVALID");
    return JSON.stringify(value);
  }
  if (Array.isArray(value))
    return `[${value.map((item) => canonicalize(item)).join(",")}]`;
  const object = objectValue(value);
  return `{${Object.keys(object)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${canonicalize(object[key])}`)
    .join(",")}}`;
}

function unsignedManifest(
  manifest: SignedReleaseManifest,
): Record<string, unknown> {
  const { signature: _signature, ...value } = manifest;
  return value;
}

export function signedManifestPayload(manifest: SignedReleaseManifest): string {
  return canonicalize(unsignedManifest(manifest));
}

function decodeSignature(value: string): Buffer {
  const normalized = value.replace(/-/gu, "+").replace(/_/gu, "/");
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");
  const decoded = Buffer.from(padded, "base64");
  if (decoded.length !== 64) throw new Error("UPDATE_SIGNATURE_INVALID");
  return decoded;
}

function skipWhitespace(text: string, index: number): number {
  while (index < text.length && /\s/u.test(text[index] ?? "")) index += 1;
  return index;
}

function stringEnd(text: string, start: number): number {
  if (text[start] !== '"') throw new Error("UPDATE_MANIFEST_INVALID");
  let index = start + 1;
  while (index < text.length) {
    const character = text[index];
    if (character === '"') return index + 1;
    if (character === "\\") {
      index += 2;
      continue;
    }
    if (character !== undefined && character < " ")
      throw new Error("UPDATE_MANIFEST_INVALID");
    index += 1;
  }
  throw new Error("UPDATE_MANIFEST_INVALID");
}

function jsonValueEnd(text: string, start: number): number {
  const index = skipWhitespace(text, start);
  const character = text[index];
  if (character === '"') return stringEnd(text, index);
  if (character === "[") {
    let cursor = skipWhitespace(text, index + 1);
    if (text[cursor] === "]") return cursor + 1;
    while (true) {
      cursor = jsonValueEnd(text, cursor);
      cursor = skipWhitespace(text, cursor);
      if (text[cursor] === "]") return cursor + 1;
      if (text[cursor] !== ",") throw new Error("UPDATE_MANIFEST_INVALID");
      cursor = skipWhitespace(text, cursor + 1);
    }
  }
  if (character === "{") {
    const keys = new Set<string>();
    let cursor = skipWhitespace(text, index + 1);
    if (text[cursor] === "}") return cursor + 1;
    while (true) {
      const keyStart = cursor;
      const keyEnd = stringEnd(text, keyStart);
      const key = JSON.parse(text.slice(keyStart, keyEnd)) as string;
      if (keys.has(key)) throw new Error("UPDATE_MANIFEST_INVALID");
      keys.add(key);
      cursor = skipWhitespace(text, keyEnd);
      if (text[cursor] !== ":") throw new Error("UPDATE_MANIFEST_INVALID");
      cursor = jsonValueEnd(text, cursor + 1);
      cursor = skipWhitespace(text, cursor);
      if (text[cursor] === "}") return cursor + 1;
      if (text[cursor] !== ",") throw new Error("UPDATE_MANIFEST_INVALID");
      cursor = skipWhitespace(text, cursor + 1);
    }
  }
  let cursor = index;
  while (cursor < text.length && !/[\s,\]}]/u.test(text[cursor] ?? ""))
    cursor += 1;
  if (cursor === index) throw new Error("UPDATE_MANIFEST_INVALID");
  JSON.parse(text.slice(index, cursor)) as unknown;
  return cursor;
}

function parseJson(text: string): unknown {
  try {
    const end = jsonValueEnd(text, 0);
    if (skipWhitespace(text, end) !== text.length)
      throw new Error("UPDATE_MANIFEST_INVALID");
    return JSON.parse(text) as unknown;
  } catch {
    throw new Error("UPDATE_MANIFEST_INVALID");
  }
}

function readSlotMarker(filePath: string): ActiveWebSlot | null {
  try {
    const value = JSON.parse(
      readFileSync(filePath, "utf8"),
    ) as Partial<ActiveWebSlot>;
    if (
      value.schemaVersion !== 1 ||
      typeof value.slot !== "string" ||
      value.slot.length === 0 ||
      path.basename(value.slot) !== value.slot ||
      typeof value.releaseNId !== "string" ||
      typeof value.version !== "string" ||
      typeof value.activationNId !== "string"
    )
      return null;
    return value as ActiveWebSlot;
  } catch {
    return null;
  }
}

function readPendingActivation(
  runtimeRoot: string,
): PendingWebActivation | null {
  try {
    const value = JSON.parse(
      readFileSync(
        path.join(runtimeRoot, "pending-web-activation.json"),
        "utf8",
      ),
    ) as Partial<PendingWebActivation>;
    const slot = readSlotMarker(path.join(runtimeRoot, "active-web-slot.json"));
    if (
      slot === null ||
      value.schemaVersion !== 1 ||
      value.slot !== slot.slot ||
      value.releaseNId !== slot.releaseNId ||
      value.version !== slot.version ||
      value.activationNId !== slot.activationNId ||
      typeof value.healthDeadlineOn !== "string" ||
      !Number.isFinite(Date.parse(value.healthDeadlineOn))
    )
      return null;
    return value as PendingWebActivation;
  } catch {
    return null;
  }
}

function slotRoot(
  runtimeRoot: string,
  marker: ActiveWebSlot | null,
): string | null {
  if (marker === null) return null;
  const candidate = path.resolve(runtimeRoot, "web-slots", marker.slot);
  const root = path.resolve(runtimeRoot, "web-slots");
  if (
    !candidate.startsWith(`${root}${path.sep}`) ||
    !existsSync(path.join(candidate, "index.html"))
  )
    return null;
  return candidate;
}

function parsePolicy(value: unknown): RuntimePolicy {
  const object = objectValue(value);
  if (
    object.schemaVersion !== 1 ||
    typeof object.appId !== "string" ||
    typeof object.environmentNId !== "string"
  )
    throw new Error("UPDATE_CONFIGURATION_MISSING");
  if (
    typeof object.channel !== "string" ||
    !Array.isArray(object.updateOrigins) ||
    !Array.isArray(object.trustKeys)
  )
    throw new Error("UPDATE_CONFIGURATION_MISSING");
  const trustKeys = object.trustKeys.map((item) => {
    const key = objectValue(item);
    if (key.algorithm !== "ed25519")
      throw new Error("UPDATE_CONFIGURATION_MISSING");
    return {
      keyId: requiredString(key, "keyId"),
      algorithm: "ed25519" as const,
      publicKey: requiredString(key, "publicKey"),
    };
  });
  return {
    schemaVersion: 1,
    appId: object.appId,
    environmentNId: object.environmentNId,
    channel: object.channel,
    ...(typeof object.updateManifestUrl === "string"
      ? { updateManifestUrl: object.updateManifestUrl }
      : {}),
    updateOrigins: object.updateOrigins.filter(
      (origin): origin is string => typeof origin === "string",
    ),
    trustKeys,
    ...(typeof object.maxArtifactBytes === "string"
      ? { maxArtifactBytes: object.maxArtifactBytes }
      : {}),
  };
}

export function parseRuntimePolicy(text: string): RuntimePolicy {
  return parsePolicy(parseJson(text));
}

export function parseSignedReleaseManifest(
  text: string,
): SignedReleaseManifest {
  const object = objectValue(parseJson(text));
  for (const key of Object.keys(object))
    if (!manifestKeys.has(key)) throw new Error("UPDATE_MANIFEST_INVALID");
  if (
    object.schemaVersion !== 1 ||
    (object.packageKind !== "web" && object.packageKind !== "native") ||
    object.signatureAlgorithm !== "ed25519" ||
    (object.platform !== "windows" && object.platform !== "android")
  )
    throw new Error("UPDATE_MANIFEST_INVALID");
  const manifest: SignedReleaseManifest = {
    schemaVersion: 1,
    releaseNId: requiredString(object, "releaseNId"),
    version: requiredString(object, "version"),
    packageKind: object.packageKind,
    artifactHash: requiredString(object, "artifactHash"),
    artifactUrl: requiredString(object, "artifactUrl"),
    appId: requiredString(object, "appId"),
    environmentNId: requiredString(object, "environmentNId"),
    channel: requiredString(object, "channel"),
    platform: object.platform,
    releaseSequence: requiredString(object, "releaseSequence"),
    keyId: requiredString(object, "keyId"),
    signatureAlgorithm: "ed25519",
    signature: requiredString(object, "signature"),
    ...(typeof object.artifactSizeBytes === "string"
      ? { artifactSizeBytes: object.artifactSizeBytes }
      : {}),
    ...(typeof object.nativeMinVersion === "string"
      ? { nativeMinVersion: object.nativeMinVersion }
      : {}),
    ...(typeof object.nativeMaxVersion === "string"
      ? { nativeMaxVersion: object.nativeMaxVersion }
      : {}),
    ...(typeof object.bridgeContractVersion === "number"
      ? { bridgeContractVersion: object.bridgeContractVersion }
      : {}),
  };
  if (
    !/^[0-9a-f]{64}$/u.test(manifest.artifactHash) ||
    !/^\d+$/u.test(manifest.releaseSequence)
  )
    throw new Error("UPDATE_MANIFEST_INVALID");
  return manifest;
}

export function verifySignedReleaseManifest(
  manifest: SignedReleaseManifest,
  policy: RuntimePolicy,
): void {
  if (
    manifest.appId !== policy.appId ||
    manifest.environmentNId !== policy.environmentNId ||
    manifest.channel !== policy.channel ||
    manifest.platform !== "windows"
  )
    throw new Error("UPDATE_INCOMPATIBLE");
  const key = policy.trustKeys.find(
    (candidate) => candidate.keyId === manifest.keyId,
  );
  if (key === undefined) throw new Error("UPDATE_SIGNATURE_INVALID");
  try {
    const publicKey = createPublicKey(key.publicKey);
    const valid = verify(
      null,
      Buffer.from(signedManifestPayload(manifest)),
      publicKey,
      decodeSignature(manifest.signature),
    );
    if (!valid) throw new Error("UPDATE_SIGNATURE_INVALID");
  } catch (error) {
    if (error instanceof Error && error.message === "UPDATE_SIGNATURE_INVALID")
      throw error;
    throw new Error("UPDATE_SIGNATURE_INVALID");
  }
}

function updateOriginAllowed(url: URL, policy: RuntimePolicy): boolean {
  if (url.protocol === "file:") return policy.updateOrigins.includes("file:");
  return url.protocol === "https:" && policy.updateOrigins.includes(url.origin);
}

export function assertArtifactUrlAllowed(
  artifactUrl: string,
  policy: RuntimePolicy,
): URL {
  let url: URL;
  try {
    url = new URL(artifactUrl);
  } catch {
    throw new Error("UPDATE_ARTIFACT_URL_INVALID");
  }
  if (!updateOriginAllowed(url, policy))
    throw new Error("UPDATE_ORIGIN_DENIED");
  return url;
}

export async function sha256File(filePath: string): Promise<string> {
  const hash = createHash("sha256");
  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(filePath);
    stream.on("data", (chunk) => hash.update(chunk));
    stream.on("end", () => resolve());
    stream.on("error", reject);
  });
  return hash.digest("hex");
}

async function writeAtomic(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const temporary = `${filePath}.${process.pid}.tmp`;
  await fs.writeFile(temporary, content, { encoding: "utf8", mode: 0o600 });
  await fs.rename(temporary, filePath);
}

export async function downloadArtifact(
  artifactUrl: string,
  destination: string,
  maxBytes: bigint,
  onProgress?: (downloadedBytes: string, totalBytes?: string) => void,
): Promise<void> {
  const url = new URL(artifactUrl);
  await fs.mkdir(path.dirname(destination), { recursive: true });
  if (url.protocol === "file:") {
    const source = fileURLToPath(url);
    const stat = await fs.stat(source);
    if (BigInt(stat.size) > maxBytes) throw new Error("UPDATE_STORAGE_FULL");
    await fs.copyFile(source, destination);
    onProgress?.(String(stat.size), String(stat.size));
    return;
  }
  const response = await fetch(url, { redirect: "manual" });
  if (!response.ok || response.body === null)
    throw new Error("UPDATE_DOWNLOAD_FAILED");
  const total = response.headers.get("content-length");
  if (total !== null && BigInt(total) > maxBytes)
    throw new Error("UPDATE_STORAGE_FULL");
  const body = Buffer.from(await response.arrayBuffer());
  if (BigInt(body.byteLength) > maxBytes)
    throw new Error("UPDATE_STORAGE_FULL");
  await fs.writeFile(destination, body, { mode: 0o600 });
  onProgress?.(String(body.byteLength), total ?? String(body.byteLength));
}

export async function loadSignedManifestSource(
  source: string,
  policy: RuntimePolicy,
): Promise<SignedReleaseManifest> {
  const url = assertArtifactUrlAllowed(source, policy);
  const text =
    url.protocol === "file:"
      ? await fs.readFile(fileURLToPath(url), "utf8")
      : await (async () => {
          const response = await fetch(url, { redirect: "manual" });
          if (!response.ok) throw new Error("UPDATE_CONFIGURATION_MISSING");
          return response.text();
        })();
  const manifest = parseSignedReleaseManifest(text);
  verifySignedReleaseManifest(manifest, policy);
  return manifest;
}

async function assertNoSymlink(root: string, current = root): Promise<void> {
  const entries = await fs.readdir(current, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(current, entry.name);
    const stat = await fs.lstat(fullPath);
    if (stat.isSymbolicLink()) throw new Error("UPDATE_ARTIFACT_INVALID");
    if (entry.isDirectory()) await assertNoSymlink(root, fullPath);
  }
}

export async function extractWebBundle(
  zipPath: string,
  destination: string,
  version: string,
): Promise<void> {
  await fs.rm(destination, { recursive: true, force: true });
  await fs.mkdir(destination, { recursive: true });
  try {
    await extract(zipPath, { dir: destination });
    await assertNoSymlink(destination);
    await fs.access(path.join(destination, "index.html"));
    await writeAtomic(
      path.join(destination, "runtime-version.json"),
      JSON.stringify({ schemaVersion: 1, webVersion: version }, null, 2),
    );
  } catch {
    await fs.rm(destination, { recursive: true, force: true });
    throw new Error("UPDATE_ARTIFACT_INVALID");
  }
}

export function activeWebRoot(
  runtimeRoot: string,
  fallbackRoot: string,
): string {
  const pending = readPendingActivation(runtimeRoot);
  if (pending !== null && Date.parse(pending.healthDeadlineOn) <= Date.now()) {
    return (
      slotRoot(
        runtimeRoot,
        readSlotMarker(path.join(runtimeRoot, "last-good-web-slot.json")),
      ) ?? fallbackRoot
    );
  }
  return (
    slotRoot(
      runtimeRoot,
      readSlotMarker(path.join(runtimeRoot, "active-web-slot.json")),
    ) ?? fallbackRoot
  );
}

export function activeWebVersion(
  runtimeRoot: string,
  fallbackVersion: string,
): string {
  const root = activeWebRoot(runtimeRoot, "");
  if (root.length === 0) return fallbackVersion;
  try {
    const value = JSON.parse(
      readFileSync(path.join(root, "runtime-version.json"), "utf8"),
    ) as { webVersion?: unknown };
    return typeof value.webVersion === "string"
      ? value.webVersion
      : fallbackVersion;
  } catch {
    return fallbackVersion;
  }
}

export async function recoverExpiredWebActivation(
  runtimeRoot: string,
  now = Date.now(),
): Promise<boolean> {
  const pending = readPendingActivation(runtimeRoot);
  if (pending === null || Date.parse(pending.healthDeadlineOn) > now)
    return false;
  const lastGood = readSlotMarker(
    path.join(runtimeRoot, "last-good-web-slot.json"),
  );
  if (slotRoot(runtimeRoot, lastGood) === null) {
    await fs.rm(path.join(runtimeRoot, "active-web-slot.json"), {
      force: true,
    });
  } else {
    await writeAtomic(
      path.join(runtimeRoot, "active-web-slot.json"),
      JSON.stringify(lastGood),
    );
  }
  await writeAtomic(
    path.join(runtimeRoot, "failed-web-activation.json"),
    JSON.stringify({
      schemaVersion: 1,
      releaseNId: pending.releaseNId,
      version: pending.version,
      errorCode: "UPDATE_HEALTH_TIMEOUT",
    } satisfies FailedWebActivation),
  );
  await fs.rm(path.join(runtimeRoot, "pending-web-activation.json"), {
    force: true,
  });
  if (lastGood?.slot !== pending.slot) {
    await fs.rm(path.join(runtimeRoot, "web-slots", pending.slot), {
      recursive: true,
      force: true,
    });
  }
  return true;
}

export class TrustedUpdateStore {
  private readonly stagingRoot: string;
  private readonly slotRoot: string;
  private readonly pending = new Map<string, PendingArtifact>();

  constructor(
    private readonly options: {
      runtimeRoot: string;
      policy: RuntimePolicy;
      currentWebVersion: () => string;
      currentNativeVersion: () => string;
    },
  ) {
    this.stagingRoot = path.join(options.runtimeRoot, "update-staging");
    this.slotRoot = path.join(options.runtimeRoot, "web-slots");
  }

  async check(manifest: SignedReleaseManifest): Promise<UpdateManifest | null> {
    verifySignedReleaseManifest(manifest, this.options.policy);
    try {
      const failed = JSON.parse(
        await fs.readFile(
          path.join(this.options.runtimeRoot, "failed-web-activation.json"),
          "utf8",
        ),
      ) as Partial<FailedWebActivation>;
      if (
        manifest.packageKind === "web" &&
        failed.releaseNId === manifest.releaseNId
      ) {
        throw new Error("UPDATE_HEALTH_TIMEOUT");
      }
      if (failed.releaseNId !== manifest.releaseNId) {
        await fs.rm(
          path.join(this.options.runtimeRoot, "failed-web-activation.json"),
          { force: true },
        );
      }
    } catch (error) {
      if (error instanceof Error && error.message === "UPDATE_HEALTH_TIMEOUT")
        throw error;
    }
    const current =
      manifest.packageKind === "web"
        ? this.options.currentWebVersion()
        : this.options.currentNativeVersion();
    if (manifest.version === current) return null;
    return manifest;
  }

  async download(
    target: SignedReleaseManifest,
    onProgress?: (downloadedBytes: string, totalBytes?: string) => void,
  ): Promise<void> {
    const url = assertArtifactUrlAllowed(
      target.artifactUrl,
      this.options.policy,
    );
    const destination = path.join(
      this.stagingRoot,
      `${target.releaseNId}.artifact`,
    );
    const maxBytes = BigInt(
      this.options.policy.maxArtifactBytes ?? "536870912",
    );
    await downloadArtifact(url.toString(), destination, maxBytes, onProgress);
    this.pending.set(target.releaseNId, { artifactPath: destination });
  }

  async verify(target: SignedReleaseManifest): Promise<void> {
    const pending = this.pending.get(target.releaseNId);
    if (pending === undefined) throw new Error("UPDATE_NOT_DOWNLOADED");
    if ((await sha256File(pending.artifactPath)) !== target.artifactHash)
      throw new Error("UPDATE_ARTIFACT_INVALID");
    if (target.artifactSizeBytes !== undefined) {
      const stat = await fs.stat(pending.artifactPath);
      if (String(stat.size) !== target.artifactSizeBytes)
        throw new Error("UPDATE_ARTIFACT_INVALID");
    }
    if (target.packageKind === "web") {
      const bundlePath = path.join(
        this.stagingRoot,
        `${target.releaseNId}.bundle`,
      );
      await extractWebBundle(pending.artifactPath, bundlePath, target.version);
      pending.bundlePath = bundlePath;
    }
    pending.verified = true;
  }

  async activate(
    target: SignedReleaseManifest,
    activationNId: string,
  ): Promise<void> {
    const pending = this.pending.get(target.releaseNId);
    if (pending === undefined) throw new Error("UPDATE_NOT_VERIFIED");
    if (!pending.verified) throw new Error("UPDATE_NOT_VERIFIED");
    if (target.packageKind !== "web" || pending.bundlePath === undefined)
      throw new Error("UPDATE_NATIVE_RESTART_REQUIRED");
    await fs.mkdir(this.slotRoot, { recursive: true });
    const slot = `${target.releaseNId}-${activationNId}`;
    const destination = path.join(this.slotRoot, slot);
    await fs.rename(pending.bundlePath, destination);
    const marker: ActiveWebSlot = {
      schemaVersion: 1,
      slot,
      releaseNId: target.releaseNId,
      version: target.version,
      activationNId,
    };
    await writeAtomic(
      path.join(this.options.runtimeRoot, "pending-web-activation.json"),
      JSON.stringify({
        ...marker,
        healthDeadlineOn: new Date(Date.now() + 60_000).toISOString(),
      }),
    );
    await writeAtomic(
      path.join(this.options.runtimeRoot, "active-web-slot.json"),
      JSON.stringify(marker),
    );
    await fs.rm(pending.artifactPath, { force: true });
    this.pending.delete(target.releaseNId);
  }

  async confirmHealthy(webVersion: string): Promise<{
    pending: boolean;
    accepted: boolean;
    targetVersion?: string;
  }> {
    const pending = readPendingActivation(this.options.runtimeRoot);
    if (pending === null) return { pending: false, accepted: true };
    if (Date.parse(pending.healthDeadlineOn) <= Date.now()) {
      await recoverExpiredWebActivation(this.options.runtimeRoot);
      return { pending: true, accepted: false, targetVersion: pending.version };
    }
    if (webVersion !== pending.version) {
      return { pending: true, accepted: false, targetVersion: pending.version };
    }
    const previous = readSlotMarker(
      path.join(this.options.runtimeRoot, "last-good-web-slot.json"),
    );
    await writeAtomic(
      path.join(this.options.runtimeRoot, "last-good-web-slot.json"),
      JSON.stringify(pending),
    );
    await fs.rm(
      path.join(this.options.runtimeRoot, "pending-web-activation.json"),
      { force: true },
    );
    await fs.rm(
      path.join(this.options.runtimeRoot, "failed-web-activation.json"),
      { force: true },
    );
    if (previous !== null && previous.slot !== pending.slot) {
      await fs.rm(path.join(this.slotRoot, previous.slot), {
        recursive: true,
        force: true,
      });
    }
    return { pending: true, accepted: true, targetVersion: pending.version };
  }

  async rollback(target: SignedReleaseManifest): Promise<void> {
    const pending = this.pending.get(target.releaseNId);
    if (pending === undefined) return;
    await fs.rm(pending.artifactPath, { force: true });
    if (pending.bundlePath !== undefined)
      await fs.rm(pending.bundlePath, { recursive: true, force: true });
    this.pending.delete(target.releaseNId);
  }

  verifiedArtifactPath(target: SignedReleaseManifest): string {
    const pending = this.pending.get(target.releaseNId);
    if (pending === undefined || !pending.verified)
      throw new Error("UPDATE_NOT_VERIFIED");
    return pending.artifactPath;
  }
}

export async function loadPolicy(policyPath: string): Promise<RuntimePolicy> {
  try {
    return parseRuntimePolicy(await fs.readFile(policyPath, "utf8"));
  } catch (error) {
    if (
      error instanceof Error &&
      error.message === "UPDATE_CONFIGURATION_MISSING"
    )
      throw error;
    throw new Error("UPDATE_CONFIGURATION_MISSING");
  }
}

export async function loadSignedManifest(
  manifestPath: string,
  policy: RuntimePolicy,
): Promise<SignedReleaseManifest> {
  try {
    const manifest = parseSignedReleaseManifest(
      await fs.readFile(manifestPath, "utf8"),
    );
    verifySignedReleaseManifest(manifest, policy);
    return manifest;
  } catch (error) {
    if (
      error instanceof Error &&
      ["UPDATE_INCOMPATIBLE", "UPDATE_SIGNATURE_INVALID"].includes(
        error.message,
      )
    )
      throw error;
    throw new Error("UPDATE_CONFIGURATION_MISSING");
  }
}
