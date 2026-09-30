import assert from "node:assert/strict";
import { generateKeyPairSync, sign } from "node:crypto";
import { createHash } from "node:crypto";
import { promises as fs } from "node:fs";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { test } from "node:test";

const runtimeUpdaterModule =
  process.env.RUNTIME_UPDATER_DIST === undefined
    ? "../dist/runtimeUpdater.js"
    : pathToFileURL(process.env.RUNTIME_UPDATER_DIST).href;
const loaded = await import(runtimeUpdaterModule);
const updater = loaded.default ?? loaded;

function signedManifest(privateKey, overrides = {}) {
  const unsigned = {
    schemaVersion: 1,
    releaseNId: "release-w2",
    version: "2.0.0",
    packageKind: "web",
    artifactHash: "a".repeat(64),
    artifactUrl: "file:///isolated/w2.zip",
    appId: "com.industrialplatform.desktop",
    environmentNId: "dev",
    channel: "stable",
    platform: "windows",
    releaseSequence: "2",
    keyId: "test-key",
    signatureAlgorithm: "ed25519",
    ...overrides,
  };
  const signature = sign(
    null,
    Buffer.from(updater.signedManifestPayload({ ...unsigned, signature: "" })),
    privateKey,
  ).toString("base64url");
  return { ...unsigned, signature };
}

function crc32(buffer) {
  let value = 0xffffffff;
  for (const byte of buffer) {
    value ^= byte;
    for (let bit = 0; bit < 8; bit += 1)
      value = (value >>> 1) ^ (value & 1 ? 0xedb88320 : 0);
  }
  return (value ^ 0xffffffff) >>> 0;
}

function zipSingleFile(name, content) {
  const nameBytes = Buffer.from(name);
  const data = Buffer.from(content);
  const crc = crc32(data);
  const local = Buffer.alloc(30 + nameBytes.length + data.length);
  local.writeUInt32LE(0x04034b50, 0);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(0, 6);
  local.writeUInt16LE(0, 8);
  local.writeUInt16LE(0, 10);
  local.writeUInt16LE(0, 12);
  local.writeUInt32LE(crc, 14);
  local.writeUInt32LE(data.length, 18);
  local.writeUInt32LE(data.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  nameBytes.copy(local, 30);
  data.copy(local, 30 + nameBytes.length);

  const central = Buffer.alloc(46 + nameBytes.length);
  central.writeUInt32LE(0x02014b50, 0);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(0, 8);
  central.writeUInt16LE(0, 10);
  central.writeUInt16LE(0, 12);
  central.writeUInt16LE(0, 14);
  central.writeUInt32LE(crc, 16);
  central.writeUInt32LE(data.length, 20);
  central.writeUInt32LE(data.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  nameBytes.copy(central, 46);

  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(1, 8);
  end.writeUInt16LE(1, 10);
  end.writeUInt32LE(central.length, 12);
  end.writeUInt32LE(local.length, 16);
  return Buffer.concat([local, central, end]);
}

test("accepts a trusted Ed25519 manifest and rejects a changed signed field", () => {
  const { privateKey, publicKey } = generateKeyPairSync("ed25519");
  const policy = {
    schemaVersion: 1,
    appId: "com.industrialplatform.desktop",
    environmentNId: "dev",
    channel: "stable",
    updateOrigins: ["file:"],
    trustKeys: [
      {
        keyId: "test-key",
        algorithm: "ed25519",
        publicKey: publicKey.export({ type: "spki", format: "pem" }),
      },
    ],
  };
  const manifest = signedManifest(privateKey);

  updater.verifySignedReleaseManifest(manifest, policy);
  assert.equal(
    updater.assertArtifactUrlAllowed(manifest.artifactUrl, policy).protocol,
    "file:",
  );

  assert.throws(
    () =>
      updater.verifySignedReleaseManifest(
        { ...manifest, version: "2.0.1" },
        policy,
      ),
    /UPDATE_SIGNATURE_INVALID/,
  );

  const duplicateVersion = JSON.stringify(manifest).replace(
    '"version":"2.0.0"',
    '"version":"2.0.0","version":"2.0.0"',
  );
  assert.throws(
    () => updater.parseSignedReleaseManifest(duplicateVersion),
    /UPDATE_MANIFEST_INVALID/,
  );
});

test("keeps an already active web release out of the update target", async () => {
  const { privateKey, publicKey } = generateKeyPairSync("ed25519");
  const policy = {
    schemaVersion: 1,
    appId: "com.industrialplatform.desktop",
    environmentNId: "dev",
    channel: "stable",
    updateOrigins: ["file:"],
    trustKeys: [
      {
        keyId: "test-key",
        algorithm: "ed25519",
        publicKey: publicKey.export({ type: "spki", format: "pem" }),
      },
    ],
  };
  const store = new updater.TrustedUpdateStore({
    runtimeRoot: "C:\\temp\\industrial-platform-runtime-test",
    policy,
    currentWebVersion: () => "2.0.0",
    currentNativeVersion: () => "1.0.0",
  });

  assert.equal(await store.check(signedManifest(privateKey)), null);
  assert.equal(
    (await store.check(signedManifest(privateKey, { version: "2.0.1" })))
      .version,
    "2.0.1",
  );
});

test("downloads, hashes, extracts and activates an isolated W2 web bundle", async () => {
  const { privateKey, publicKey } = generateKeyPairSync("ed25519");
  const root = await fs.mkdtemp(
    path.join(os.tmpdir(), "industrial-platform-update-"),
  );
  const artifact = path.join(root, "w2.zip");
  const zip = zipSingleFile("index.html", "<h1>W2</h1>");
  await fs.writeFile(artifact, zip);
  const policy = {
    schemaVersion: 1,
    appId: "com.industrialplatform.desktop",
    environmentNId: "dev",
    channel: "stable",
    updateOrigins: ["file:"],
    trustKeys: [
      {
        keyId: "test-key",
        algorithm: "ed25519",
        publicKey: publicKey.export({ type: "spki", format: "pem" }),
      },
    ],
  };
  const manifest = signedManifest(privateKey, {
    artifactHash: createHash("sha256").update(zip).digest("hex"),
    artifactSizeBytes: String(zip.length),
    artifactUrl: pathToFileURL(artifact).toString(),
  });
  const store = new updater.TrustedUpdateStore({
    runtimeRoot: path.join(root, "runtime"),
    policy,
    currentWebVersion: () => "1.0.0",
    currentNativeVersion: () => "1.0.0",
  });

  await store.download(manifest);
  await store.verify(manifest);
  await store.activate(manifest, "activation-w2");
  assert.equal(
    updater.activeWebVersion(path.join(root, "runtime"), "1.0.0"),
    "2.0.0",
  );
  assert.match(
    await fs.readFile(
      path.join(
        updater.activeWebRoot(path.join(root, "runtime"), ""),
        "index.html",
      ),
      "utf8",
    ),
    /W2/,
  );
  assert.deepEqual(await store.confirmHealthy("1.0.0"), {
    pending: true,
    accepted: false,
    targetVersion: "2.0.0",
  });
  assert.deepEqual(await store.confirmHealthy("2.0.0"), {
    pending: true,
    accepted: true,
    targetVersion: "2.0.0",
  });

  const failedManifest = signedManifest(privateKey, {
    releaseNId: "release-w3",
    releaseSequence: "3",
    version: "3.0.0",
    artifactHash: createHash("sha256").update(zip).digest("hex"),
    artifactSizeBytes: String(zip.length),
    artifactUrl: pathToFileURL(artifact).toString(),
  });
  await store.download(failedManifest);
  await store.verify(failedManifest);
  await store.activate(failedManifest, "activation-w3");
  assert.equal(
    await updater.recoverExpiredWebActivation(
      path.join(root, "runtime"),
      Date.now() + 61_000,
    ),
    true,
  );
  assert.equal(
    updater.activeWebVersion(path.join(root, "runtime"), "1.0.0"),
    "2.0.0",
  );
  await assert.rejects(store.check(failedManifest), /UPDATE_HEALTH_TIMEOUT/);
});
