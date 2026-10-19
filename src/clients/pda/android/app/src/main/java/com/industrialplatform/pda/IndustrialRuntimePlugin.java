package com.industrialplatform.pda;

import android.Manifest;
import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.provider.Settings;
import android.util.Base64;
import android.util.JsonReader;
import android.util.JsonToken;

import androidx.activity.result.ActivityResult;
import androidx.core.content.FileProvider;
import androidx.core.content.ContextCompat;
import android.media.projection.MediaProjectionManager;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.ActivityCallback;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.nio.charset.StandardCharsets;
import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.StringReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.file.Files;
import java.security.KeyFactory;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Signature;
import java.security.spec.X509EncodedKeySpec;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.List;
import java.util.UUID;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

import org.json.JSONArray;
import org.json.JSONObject;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

@CapacitorPlugin(name = "IndustrialRuntime")
public class IndustrialRuntimePlugin extends Plugin {
    private static final String KEY_ALIAS = "IndustrialPlatform.Runtime.Auth.v1";
    private static final String PREFS = "industrial_platform_runtime_secure";
    private static final String SEPARATOR = ":";
    private static final String UPDATE_CACHE = "industrial-platform-update";
    private static final String UPDATE_POLICY = "runtime-policy.json";
    private static final String APP_ID = "com.industrialplatform.pda";
    private static final String ENVIRONMENT_NID = "dev";
    private static final String CHANNEL = "stable";
    private JSONObject pendingManifest;
    private File pendingArtifact;
    private boolean pendingVerified;
    private final String subjectEpoch = UUID.randomUUID().toString().replace("-", "");

    @PluginMethod
    public void getCapabilities(PluginCall call) {
        JSObject result = new JSObject();
        result.put("contractVersion", 1);
        result.put("container", "capacitor");
        result.put("os", "android");
        result.put("osVersion", Build.VERSION.RELEASE == null ? "unknown" : Build.VERSION.RELEASE);
        result.put("nativeVersion", BuildConfig.VERSION_NAME);
        result.put("webVersion", currentWebVersion());
        result.put("bridgeContractVersion", 1);
        result.put("subjectEpoch", subjectEpoch);
        result.put("snapshotSequence", "1");

        JSArray capabilities = new JSArray();
        capabilities.put(capability("scanner.keyboard", "Supported", null));
        capabilities.put(capability(
                "scanner.camera",
                hasCameraPermission() ? "Supported" : "PermissionRequired",
                hasCameraPermission() ? null : "CAMERA_PERMISSION_REQUIRED"));
        capabilities.put(capability("scanner.broadcast", "Unsupported", "CAPABILITY_NOT_VERIFIED"));
        capabilities.put(capability("bluetooth.ble", "Unsupported", "CAPABILITY_NOT_VERIFIED"));
        capabilities.put(capability("media.microphone", "Supported", null));
        capabilities.put(capability("media.screenCapture", "Supported", null));
        capabilities.put(capability("files.pick", "Supported", null));
        capabilities.put(capability("files.save", "Supported", null));
        capabilities.put(capability("printer.systemTest", "Degraded", "PRINTER_SYSTEM_DIALOG_NOT_VERIFIED"));
        capabilities.put(capability("notifications.local", "PermissionRequired", "POST_NOTIFICATIONS_PERMISSION_REQUIRED"));
        capabilities.put(capability("diagnostics.export", "Supported", null));
        capabilities.put(capability("updates.webBundle", "Degraded", "UPDATE_CHANNEL_NOT_CONFIGURED"));
        capabilities.put(capability("updates.installer", "Supported", null));
        result.put("capabilities", capabilities);
        call.resolve(result);
    }

    @PluginMethod
    public void loadAuth(PluginCall call) {
        String key = call.getString("key", "");
        if (!isAllowedAuthKey(key)) {
            call.reject("RUNTIME_INVALID_ARGUMENT");
            return;
        }
        try {
            String encoded = securePreferences().getString(key, null);
            JSObject result = new JSObject();
            if (encoded != null) result.put("value", decrypt(encoded));
            call.resolve(result);
        } catch (Exception error) {
            call.reject("RUNTIME_SECURE_STORAGE_UNAVAILABLE");
        }
    }

    @PluginMethod
    public void saveAuth(PluginCall call) {
        String key = call.getString("key", "");
        String value = call.getString("value", null);
        if (!isAllowedAuthKey(key) || value == null || value.length() > 65536) {
            call.reject("RUNTIME_INVALID_ARGUMENT");
            return;
        }
        try {
            securePreferences().edit().putString(key, encrypt(value)).apply();
            call.resolve();
        } catch (Exception error) {
            call.reject("RUNTIME_SECURE_STORAGE_UNAVAILABLE");
        }
    }

    @PluginMethod
    public void clearAuth(PluginCall call) {
        String key = call.getString("key", "");
        if (!isAllowedAuthKey(key)) {
            call.reject("RUNTIME_INVALID_ARGUMENT");
            return;
        }
        securePreferences().edit().remove(key).apply();
        call.resolve();
    }

    @PluginMethod
    public void listDevices(PluginCall call) {
        JSObject result = new JSObject();
        result.put("items", new JSArray());
        call.resolve(result);
    }

    @PluginMethod
    public void pickFile(PluginCall call) {
        try {
            JSONArray mimeTypes = call.getArray("mimeTypes");
            long maxBytes = Long.parseLong(call.getString("maxBytes", "10485760"));
            if (maxBytes <= 0 || maxBytes > 52428800L) throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType(mimeTypes == null || mimeTypes.length() == 0 ? "*/*" : mimeTypes.length() == 1 ? mimeTypes.getString(0) : "*/*");
            if (mimeTypes != null && mimeTypes.length() > 1) {
                String[] filters = new String[mimeTypes.length()];
                for (int index = 0; index < mimeTypes.length(); index++) filters[index] = mimeTypes.getString(index);
                intent.putExtra(Intent.EXTRA_MIME_TYPES, filters);
            }
            call.setKeepAlive(true);
            call.getData().put("maxBytes", maxBytes);
            startActivityForResult(call, intent, "pickFileResult");
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @ActivityCallback
    private void pickFileResult(PluginCall call, ActivityResult result) {
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null || result.getData().getData() == null) {
            call.resolve();
            return;
        }
        try {
            long maxBytes = call.getData().optLong("maxBytes", 10485760L);
            File directory = new File(getContext().getCacheDir(), "runtime-files");
            if (!directory.exists() && !directory.mkdirs()) throw new IllegalStateException("RUNTIME_STORAGE_UNAVAILABLE");
            File destination = new File(directory, UUID.randomUUID().toString() + ".attachment");
            long total = copyUriToFile(result.getData().getData(), destination, maxBytes);
            JSObject response = new JSObject();
            response.put("artifactHandle", destination.getName());
            response.put("name", "attachment");
            response.put("sizeBytes", String.valueOf(total));
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void saveFile(PluginCall call) {
        try {
            File source = resolveRuntimeFile(call.getString("artifactHandle", ""));
            String displayName = safeDisplayName(call.getString("displayName", "download"));
            Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("application/octet-stream");
            intent.putExtra(Intent.EXTRA_TITLE, displayName);
            call.setKeepAlive(true);
            call.getData().put("sourcePath", source.getCanonicalPath());
            startActivityForResult(call, intent, "saveFileResult");
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @ActivityCallback
    private void saveFileResult(PluginCall call, ActivityResult result) {
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null || result.getData().getData() == null) {
            JSObject response = new JSObject();
            response.put("saved", false);
            call.resolve(response);
            return;
        }
        try (InputStream input = new FileInputStream(new File(call.getData().getString("sourcePath"))); OutputStream output = getContext().getContentResolver().openOutputStream(result.getData().getData())) {
            if (output == null) throw new IllegalStateException("RUNTIME_STORAGE_UNAVAILABLE");
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            JSObject response = new JSObject();
            response.put("saved", true);
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void openSettings(PluginCall call) {
        String capability = call.getString("capability", "");
        if (!capability.equals("scanner.camera") && !capability.equals("media.microphone") && !capability.equals("notifications.local") && !capability.equals("updates.installer")) {
            call.reject("RUNTIME_INVALID_ARGUMENT");
            return;
        }
        try {
            Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getContext().getPackageName()));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            getContext().startActivity(intent);
            JSObject response = new JSObject();
            response.put("opened", true);
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void startScreenCapture(PluginCall call) {
        if (Build.VERSION.SDK_INT < 21) {
            call.reject("MEDIA_UNSUPPORTED");
            return;
        }
        try {
            MediaProjectionManager manager = (MediaProjectionManager) getContext()
                    .getSystemService(Context.MEDIA_PROJECTION_SERVICE);
            startActivityForResult(call, manager.createScreenCaptureIntent(), "screenCaptureConsentResult");
        } catch (Exception error) {
            call.reject("MEDIA_CAPTURE_FAILED");
        }
    }

    @ActivityCallback
    private void screenCaptureConsentResult(PluginCall call, ActivityResult result) {
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null) {
            call.reject("MEDIA_SCREEN_CAPTURE_CANCELLED");
            return;
        }
        ScreenCaptureService.listener = new ScreenCaptureService.Listener() {
            @Override
            public void onFrame(String base64Jpeg, int width, int height) {
                JSObject frame = new JSObject();
                frame.put("jpeg", base64Jpeg);
                frame.put("width", width);
                frame.put("height", height);
                notifyListeners("screenFrame", frame);
            }

            @Override
            public void onStopped(String errorCode) {
                JSObject event = new JSObject();
                if (errorCode != null) event.put("errorCode", errorCode);
                notifyListeners("screenCaptureStopped", event);
            }
        };
        try {
            Intent intent = new Intent(getContext(), ScreenCaptureService.class);
            intent.setAction(ScreenCaptureService.ACTION_START);
            intent.putExtra(ScreenCaptureService.EXTRA_RESULT_CODE, result.getResultCode());
            intent.putExtra(ScreenCaptureService.EXTRA_RESULT_DATA, result.getData());
            ContextCompat.startForegroundService(getContext(), intent);
            call.resolve();
        } catch (Exception error) {
            ScreenCaptureService.listener = null;
            call.reject("MEDIA_CAPTURE_FAILED");
        }
    }

    @PluginMethod
    public void stopScreenCapture(PluginCall call) {
        try {
            Intent intent = new Intent(getContext(), ScreenCaptureService.class);
            intent.setAction(ScreenCaptureService.ACTION_STOP);
            getContext().startService(intent);
            call.resolve();
        } catch (Exception error) {
            call.reject("MEDIA_CAPTURE_FAILED");
        }
    }

    @PluginMethod
    public void getPrinterCapabilities(PluginCall call) {
        JSObject result = new JSObject();
        result.put("protocol", "system");
        result.put("receiptLevel", "SystemOnly");
        result.put("supportedFormats", new JSArray());
        call.resolve(result);
    }

    @PluginMethod
    public void printTest(PluginCall call) {
        JSObject result = new JSObject();
        result.put("submission", "Cancelled");
        result.put("receiptLevel", "SystemOnly");
        call.resolve(result);
    }

    @PluginMethod
    public void checkUpdate(PluginCall call) {
        new Thread(() -> {
            try {
                if (!hasUpdatePolicy()) {
                    call.resolve();
                    return;
                }
                JSONObject policy = readPolicy();
                JSONObject manifest = readJsonFromHttps(policy.getString("updateManifestUrl"));
                verifyManifest(manifest, policy);
                File failedFile = new File(getContext().getFilesDir(), "failed-web-activation.json");
                if (manifest.getString("packageKind").equals("web") && failedFile.isFile()) {
                    JSONObject failed = readJsonFile(failedFile);
                    if (manifest.getString("releaseNId").equals(failed.optString("releaseNId")))
                        throw new IllegalStateException("UPDATE_HEALTH_TIMEOUT");
                    failedFile.delete();
                }
                String current = manifest.getString("packageKind").equals("native") ? BuildConfig.VERSION_NAME : currentWebVersion();
                if (manifest.getString("version").equals(current)) {
                    call.resolve();
                    return;
                }
                call.resolve(toJsObject(manifest));
            } catch (Exception error) {
                call.reject(errorCode(error));
            }
        }).start();
    }

    @PluginMethod
    public void confirmUpdateHealthy(PluginCall call) {
        try {
            File pendingFile = new File(getContext().getFilesDir(), "pending-web-activation.json");
            JSObject response = new JSObject();
            if (!pendingFile.isFile()) {
                response.put("pending", false);
                response.put("accepted", true);
                call.resolve(response);
                return;
            }
            JSONObject pending = readJsonFile(pendingFile);
            response.put("pending", true);
            response.put("targetVersion", pending.getString("version"));
            if (pending.getLong("healthDeadlineEpochMs") <= System.currentTimeMillis()) {
                recoverExpiredActivation(getContext());
                response.put("accepted", false);
                call.resolve(response);
                return;
            }
            String webVersion = call.getString("webVersion", "");
            JSONObject active = readJsonFile(new File(getContext().getFilesDir(), "active-web-slot.json"));
            boolean accepted = webVersion.equals(pending.getString("version"))
                    && active.getString("slot").equals(pending.getString("slot"))
                    && active.getString("activationNId").equals(pending.getString("activationNId"));
            response.put("accepted", accepted);
            if (!accepted) {
                call.resolve(response);
                return;
            }
            File lastGoodFile = new File(getContext().getFilesDir(), "last-good-web-slot.json");
            JSONObject previous = lastGoodFile.isFile() ? readJsonFile(lastGoodFile) : null;
            writeJsonAtomic(lastGoodFile, active);
            pendingFile.delete();
            new File(getContext().getFilesDir(), "failed-web-activation.json").delete();
            if (previous != null && !previous.optString("slot").equals(active.getString("slot"))) {
                deleteRecursively(new File(new File(getContext().getFilesDir(), "web-slots"), previous.optString("slot")));
            }
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void downloadUpdate(PluginCall call) {
        new Thread(() -> {
            try {
                JSONObject manifest = targetFromCall(call);
                JSONObject policy = readPolicy();
                verifyManifest(manifest, policy);
                URL url = allowedUpdateUrl(manifest.getString("artifactUrl"), policy);
                File directory = new File(getContext().getCacheDir(), UPDATE_CACHE);
                if (!directory.exists() && !directory.mkdirs()) throw new IllegalStateException("UPDATE_STORAGE_UNAVAILABLE");
                File destination = new File(directory, manifest.getString("releaseNId") + ".artifact");
                download(url, destination, policy.optLong("maxArtifactBytes", 536870912L));
                pendingManifest = manifest;
                pendingArtifact = destination;
                pendingVerified = false;
                call.resolve();
            } catch (Exception error) {
                call.reject(errorCode(error));
            }
        }).start();
    }

    @PluginMethod
    public void verifyUpdate(PluginCall call) {
        try {
            JSONObject manifest = targetFromCall(call);
            if (pendingArtifact == null || pendingManifest == null
                    || !pendingManifest.optString("releaseNId").equals(manifest.optString("releaseNId")))
                throw new IllegalStateException("UPDATE_NOT_DOWNLOADED");
            if (!sha256(pendingArtifact).equalsIgnoreCase(manifest.getString("artifactHash")))
                throw new IllegalStateException("UPDATE_ARTIFACT_INVALID");
            if (manifest.has("artifactSizeBytes")
                    && pendingArtifact.length() != Long.parseLong(manifest.getString("artifactSizeBytes")))
                throw new IllegalStateException("UPDATE_ARTIFACT_INVALID");
            pendingVerified = true;
            call.resolve();
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void activateUpdate(PluginCall call) {
        try {
            JSONObject manifest = targetFromCall(call);
            if (!pendingVerified || pendingArtifact == null || pendingManifest == null
                    || !pendingManifest.optString("releaseNId").equals(manifest.optString("releaseNId")))
                throw new IllegalStateException("UPDATE_NOT_VERIFIED");
            if (manifest.getString("packageKind").equals("web")) {
                File slot = stageWebBundle(manifest, call.getString("activationNId", ""));
                JSObject response = new JSObject();
                response.put("activated", true);
                call.resolve(response);
                getBridge().setServerBasePath(slot.getAbsolutePath());
                new Handler(Looper.getMainLooper()).postDelayed(() -> {
                    try {
                        File pendingFile = new File(getContext().getFilesDir(), "pending-web-activation.json");
                        if (!pendingFile.isFile()) return;
                        recoverExpiredActivation(getContext());
                        if (getActivity() != null) getActivity().recreate();
                    } catch (Exception ignored) {
                        if (getActivity() != null) getActivity().recreate();
                    }
                }, 60100L);
                pendingArtifact.delete();
                pendingArtifact = null;
                pendingManifest = null;
                pendingVerified = false;
                return;
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                    && !getContext().getPackageManager().canRequestPackageInstalls())
                throw new SecurityException("PERMISSION_REQUIRED");
            Uri uri = FileProvider.getUriForFile(
                    getContext(), getContext().getPackageName() + ".fileprovider", pendingArtifact);
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(uri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            getContext().startActivity(intent);
            call.resolve();
            if (getActivity() != null) getActivity().finishAndRemoveTask();
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void rollbackUpdate(PluginCall call) {
        if (pendingArtifact != null) pendingArtifact.delete();
        pendingArtifact = null;
        pendingManifest = null;
        pendingVerified = false;
        call.resolve();
    }

    @PluginMethod
    public void exit(PluginCall call) {
        call.resolve();
        if (getActivity() != null) getActivity().finishAndRemoveTask();
    }

    @PluginMethod
    public void exportDiagnostics(PluginCall call) {
        try {
            if (!call.getBoolean("consent", false)) throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
            String name = "industrial-platform-diagnostics-" + System.currentTimeMillis() + ".json";
            File file = new File(getContext().getCacheDir(), name);
            JSONObject result = new JSONObject();
            result.put("schemaVersion", 1);
            result.put("exportedOn", System.currentTimeMillis());
            result.put("appId", APP_ID);
            result.put("nativeVersion", BuildConfig.VERSION_NAME);
            result.put("platform", "android");
            byte[] bytes = result.toString(2).getBytes(StandardCharsets.UTF_8);
            try (OutputStream output = new FileOutputStream(file)) {
                output.write(bytes);
            }
            JSObject response = new JSObject();
            response.put("artifactHandle", file.getName());
            response.put("name", name);
            response.put("sizeBytes", String.valueOf(bytes.length));
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    @PluginMethod
    public void showNotification(PluginCall call) {
        try {
            String eventNId = call.getString("eventNId", "");
            String titleKey = call.getString("titleKey", "");
            if (eventNId.length() == 0 || (!titleKey.equals("newMessage") && !titleKey.equals("collaborationInvite")))
                throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                    && getContext().checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                JSObject response = new JSObject();
                response.put("accepted", false);
                call.resolve(response);
                return;
            }
            NotificationManager manager = (NotificationManager) getContext().getSystemService(Context.NOTIFICATION_SERVICE);
            String channelId = "industrial-platform-runtime";
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
                manager.createNotificationChannel(new NotificationChannel(channelId, "Industrial Platform", NotificationManager.IMPORTANCE_DEFAULT));
            Notification.Builder builder = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                    ? new Notification.Builder(getContext(), channelId)
                    : new Notification.Builder(getContext());
            builder.setContentTitle(titleKey.equals("newMessage") ? "有新消息" : "有协作邀请")
                    .setSmallIcon(android.R.drawable.ic_dialog_info)
                    .setAutoCancel(true);
            manager.notify(eventNId.hashCode(), builder.build());
            JSObject response = new JSObject();
            response.put("accepted", true);
            call.resolve(response);
        } catch (Exception error) {
            call.reject(errorCode(error));
        }
    }

    private JSONObject readPolicy() throws Exception {
        try (InputStream input = getContext().getAssets().open(UPDATE_POLICY)) {
            String text = readAll(input);
            assertNoDuplicateJsonKeys(text);
            return new JSONObject(text);
        }
    }

    private boolean hasUpdatePolicy() {
        try (InputStream ignored = getContext().getAssets().open(UPDATE_POLICY)) {
            return true;
        } catch (Exception error) {
            return false;
        }
    }

    private JSONObject readJsonFromHttps(String address) throws Exception {
        URL url = new URL(address);
        if (!url.getProtocol().equals("https")) throw new SecurityException("UPDATE_ORIGIN_DENIED");
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(30000);
        if (connection.getResponseCode() != HttpURLConnection.HTTP_OK) throw new IllegalStateException("UPDATE_DOWNLOAD_FAILED");
        try (InputStream input = connection.getInputStream()) {
            String text = readAll(input);
            assertNoDuplicateJsonKeys(text);
            return new JSONObject(text);
        } finally {
            connection.disconnect();
        }
    }

    private void assertNoDuplicateJsonKeys(String text) throws Exception {
        try (JsonReader reader = new JsonReader(new StringReader(text))) {
            inspectJsonValue(reader);
            if (reader.peek() != JsonToken.END_DOCUMENT) throw new IllegalArgumentException("UPDATE_MANIFEST_INVALID");
        }
    }

    private void inspectJsonValue(JsonReader reader) throws Exception {
        switch (reader.peek()) {
            case BEGIN_OBJECT:
                reader.beginObject();
                java.util.HashSet<String> keys = new java.util.HashSet<>();
                while (reader.hasNext()) {
                    String key = reader.nextName();
                    if (!keys.add(key)) throw new IllegalArgumentException("UPDATE_MANIFEST_INVALID");
                    inspectJsonValue(reader);
                }
                reader.endObject();
                return;
            case BEGIN_ARRAY:
                reader.beginArray();
                while (reader.hasNext()) inspectJsonValue(reader);
                reader.endArray();
                return;
            case STRING:
                reader.nextString();
                return;
            case NUMBER:
                reader.nextString();
                return;
            case BOOLEAN:
                reader.nextBoolean();
                return;
            case NULL:
                reader.nextNull();
                return;
            default:
                throw new IllegalArgumentException("UPDATE_MANIFEST_INVALID");
        }
    }

    private URL allowedUpdateUrl(String address, JSONObject policy) throws Exception {
        URL url = new URL(address);
        if (!url.getProtocol().equals("https")) throw new SecurityException("UPDATE_ORIGIN_DENIED");
        JSONArray origins = policy.getJSONArray("updateOrigins");
        boolean allowed = false;
        for (int index = 0; index < origins.length(); index++)
            if ((url.getProtocol() + "://" + url.getAuthority()).equals(origins.getString(index))) allowed = true;
        if (!allowed) throw new SecurityException("UPDATE_ORIGIN_DENIED");
        return url;
    }

    private void download(URL url, File destination, long maxBytes) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(60000);
        if (connection.getResponseCode() != HttpURLConnection.HTTP_OK) throw new IllegalStateException("UPDATE_DOWNLOAD_FAILED");
        long expected = connection.getContentLengthLong();
        if (expected > maxBytes) throw new IllegalStateException("UPDATE_STORAGE_FULL");
        long total = 0;
        long lastReported = 0;
        emitUpdateProgress(0, expected);
        try (InputStream input = new BufferedInputStream(connection.getInputStream()); OutputStream output = new BufferedOutputStream(new FileOutputStream(destination))) {
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                if (total > maxBytes) throw new IllegalStateException("UPDATE_STORAGE_FULL");
                output.write(buffer, 0, read);
                if (total - lastReported >= 1048576 || total == expected) {
                    emitUpdateProgress(total, expected);
                    lastReported = total;
                }
            }
            if (lastReported != total) emitUpdateProgress(total, expected);
        } finally {
            connection.disconnect();
        }
    }

    private void emitUpdateProgress(long downloaded, long total) {
        JSObject event = new JSObject();
        event.put("downloadedBytes", String.valueOf(downloaded));
        if (total >= 0) event.put("totalBytes", String.valueOf(total));
        notifyListeners("updateProgress", event);
    }

    private JSONObject targetFromCall(PluginCall call) throws Exception {
        JSONObject target = call.getObject("target");
        if (target == null) throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
        return target;
    }

    private void verifyManifest(JSONObject manifest, JSONObject policy) throws Exception {
        if (!manifest.getString("platform").equals("android")
                || !manifest.getString("appId").equals(policy.getString("appId"))
                || !manifest.getString("environmentNId").equals(policy.getString("environmentNId"))
                || !manifest.getString("channel").equals(policy.getString("channel")))
            throw new IllegalStateException("UPDATE_INCOMPATIBLE");
        String keyId = manifest.getString("keyId");
        JSONObject key = null;
        JSONArray keys = policy.getJSONArray("trustKeys");
        for (int index = 0; index < keys.length(); index++) {
            JSONObject candidate = keys.getJSONObject(index);
            if (candidate.getString("keyId").equals(keyId)) key = candidate;
        }
        if (key == null || !key.getString("algorithm").equals("ed25519")) throw new SecurityException("UPDATE_SIGNATURE_INVALID");
        String pem = key.getString("publicKey").replace("-----BEGIN PUBLIC KEY-----", "").replace("-----END PUBLIC KEY-----", "").replaceAll("\\s", "");
        PublicKey publicKey = KeyFactory.getInstance("Ed25519").generatePublic(new X509EncodedKeySpec(Base64.decode(pem, Base64.DEFAULT)));
        Signature signature = Signature.getInstance("Ed25519");
        signature.initVerify(publicKey);
        signature.update(canonicalWithoutSignature(manifest).getBytes(StandardCharsets.UTF_8));
        byte[] signed = Base64.decode(manifest.getString("signature"), Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
        if (!signature.verify(signed)) throw new SecurityException("UPDATE_SIGNATURE_INVALID");
    }

    private String canonicalWithoutSignature(JSONObject source) throws Exception {
        JSONObject copy = new JSONObject(source.toString());
        copy.remove("signature");
        return canonical(copy);
    }

    private String canonical(Object value) throws Exception {
        if (value == JSONObject.NULL) return "null";
        if (value instanceof JSONObject) {
            JSONObject object = (JSONObject) value;
            List<String> keys = new ArrayList<>();
            Iterator<String> iterator = object.keys();
            while (iterator.hasNext()) keys.add(iterator.next());
            Collections.sort(keys);
            StringBuilder output = new StringBuilder("{");
            for (String key : keys) {
                if (output.length() > 1) output.append(',');
                output.append(JSONObject.quote(key)).append(':').append(canonical(object.get(key)));
            }
            return output.append('}').toString();
        }
        if (value instanceof JSONArray) {
            JSONArray array = (JSONArray) value;
            StringBuilder output = new StringBuilder("[");
            for (int index = 0; index < array.length(); index++) {
                if (index > 0) output.append(',');
                output.append(canonical(array.get(index)));
            }
            return output.append(']').toString();
        }
        if (value instanceof String) return JSONObject.quote((String) value);
        return value.toString();
    }

    private String currentWebVersion() {
        File slot = activeWebSlot(getContext());
        if (slot == null) return "0.1.0";
        File versionFile = new File(slot, "runtime-version.json");
        try (InputStream input = new FileInputStream(versionFile)) {
            return new JSONObject(readAll(input)).optString("webVersion", "0.1.0");
        } catch (Exception error) {
            return "0.1.0";
        }
    }

    private File stageWebBundle(JSONObject manifest, String activationNId) throws Exception {
        if (activationNId.length() == 0) throw new IllegalArgumentException("UPDATE_ACTIVATION_CONTEXT_MISSING");
        File slots = new File(getContext().getFilesDir(), "web-slots");
        if (!slots.exists() && !slots.mkdirs()) throw new IllegalStateException("UPDATE_STORAGE_UNAVAILABLE");
        String slotName = manifest.getString("releaseNId") + "-" + System.currentTimeMillis();
        File destination = new File(slots, slotName);
        if (!destination.mkdirs()) throw new IllegalStateException("UPDATE_STORAGE_UNAVAILABLE");
        String root = destination.getCanonicalPath() + File.separator;
        try (ZipInputStream input = new ZipInputStream(new BufferedInputStream(new FileInputStream(pendingArtifact)))) {
            ZipEntry entry;
            byte[] buffer = new byte[65536];
            while ((entry = input.getNextEntry()) != null) {
                File output = new File(destination, entry.getName());
                if (!output.getCanonicalPath().startsWith(root)) throw new SecurityException("UPDATE_ARTIFACT_INVALID");
                if (entry.isDirectory()) {
                    if (!output.mkdirs() && !output.isDirectory()) throw new IllegalStateException("UPDATE_ARTIFACT_INVALID");
                    continue;
                }
                File parent = output.getParentFile();
                if (parent != null && !parent.exists() && !parent.mkdirs()) throw new IllegalStateException("UPDATE_ARTIFACT_INVALID");
                try (OutputStream stream = new BufferedOutputStream(new FileOutputStream(output))) {
                    int read;
                    while ((read = input.read(buffer)) != -1) stream.write(buffer, 0, read);
                }
            }
        } catch (Exception error) {
            deleteRecursively(destination);
            throw error;
        }
        File index = new File(destination, "index.html");
        if (!index.isFile()) {
            deleteRecursively(destination);
            throw new IllegalStateException("UPDATE_ARTIFACT_INVALID");
        }
        JSONObject version = new JSONObject();
        version.put("schemaVersion", 1);
        version.put("webVersion", manifest.getString("version"));
        try (OutputStream output = new FileOutputStream(new File(destination, "runtime-version.json"))) {
            output.write(version.toString().getBytes(StandardCharsets.UTF_8));
        }
        JSONObject active = new JSONObject();
        active.put("schemaVersion", 1);
        active.put("slot", slotName);
        active.put("releaseNId", manifest.getString("releaseNId"));
        active.put("version", manifest.getString("version"));
        active.put("activationNId", activationNId);
        JSONObject pending = new JSONObject(active.toString());
        pending.put("healthDeadlineEpochMs", System.currentTimeMillis() + 60000L);
        writeJsonAtomic(new File(getContext().getFilesDir(), "pending-web-activation.json"), pending);
        writeJsonAtomic(new File(getContext().getFilesDir(), "active-web-slot.json"), active);
        return destination;
    }

    private long copyUriToFile(Uri uri, File destination, long maxBytes) throws Exception {
        long total = 0;
        try (InputStream input = new BufferedInputStream(getContext().getContentResolver().openInputStream(uri)); OutputStream output = new BufferedOutputStream(new FileOutputStream(destination))) {
            if (input == null) throw new IllegalStateException("RUNTIME_STORAGE_UNAVAILABLE");
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                if (total > maxBytes) {
                    destination.delete();
                    throw new IllegalArgumentException("RUNTIME_FILE_TOO_LARGE");
                }
                output.write(buffer, 0, read);
            }
        }
        return total;
    }

    private File resolveRuntimeFile(String handle) throws Exception {
        if (handle == null || !handle.matches("[A-Za-z0-9._-]+")) throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
        File root = new File(getContext().getCacheDir(), "runtime-files").getCanonicalFile();
        File file = new File(root, handle).getCanonicalFile();
        if (!file.getPath().startsWith(root.getPath() + File.separator) || !file.isFile()) throw new IllegalArgumentException("RUNTIME_FILE_NOT_FOUND");
        return file;
    }

    private String safeDisplayName(String displayName) {
        if (displayName == null || displayName.length() == 0 || displayName.length() > 128 || displayName.contains("/") || displayName.contains("\\") || displayName.equals(".") || displayName.equals("..")) {
            throw new IllegalArgumentException("RUNTIME_INVALID_ARGUMENT");
        }
        return displayName;
    }

    public static File activeWebSlot(Context context) {
        try {
            recoverExpiredActivation(context);
            File marker = new File(context.getFilesDir(), "active-web-slot.json");
            JSONObject active = new JSONObject(new String(Files.readAllBytes(marker.toPath()), StandardCharsets.UTF_8));
            String slot = active.getString("slot");
            if (!slot.matches("[A-Za-z0-9._-]+")) return null;
            File root = new File(context.getFilesDir(), "web-slots").getCanonicalFile();
            File candidate = new File(root, slot).getCanonicalFile();
            if (!candidate.getPath().startsWith(root.getPath() + File.separator) || !new File(candidate, "index.html").isFile()) return null;
            return candidate;
        } catch (Exception error) {
            return null;
        }
    }

    private static JSONObject readJsonFile(File file) throws Exception {
        return new JSONObject(new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8));
    }

    private static void writeJsonAtomic(File file, JSONObject value) throws Exception {
        File temporary = new File(file.getParentFile(), file.getName() + ".tmp");
        try (OutputStream output = new FileOutputStream(temporary)) {
            output.write(value.toString().getBytes(StandardCharsets.UTF_8));
        }
        if (file.exists() && !file.delete()) throw new IllegalStateException("UPDATE_STORAGE_UNAVAILABLE");
        if (!temporary.renameTo(file)) throw new IllegalStateException("UPDATE_STORAGE_UNAVAILABLE");
    }

    private static void recoverExpiredActivation(Context context) throws Exception {
        File pendingFile = new File(context.getFilesDir(), "pending-web-activation.json");
        if (!pendingFile.isFile()) return;
        JSONObject pending = readJsonFile(pendingFile);
        if (pending.optLong("healthDeadlineEpochMs", Long.MAX_VALUE) > System.currentTimeMillis()) return;
        File activeFile = new File(context.getFilesDir(), "active-web-slot.json");
        File lastGoodFile = new File(context.getFilesDir(), "last-good-web-slot.json");
        if (lastGoodFile.isFile()) writeJsonAtomic(activeFile, readJsonFile(lastGoodFile));
        else activeFile.delete();
        JSONObject failed = new JSONObject();
        failed.put("schemaVersion", 1);
        failed.put("releaseNId", pending.getString("releaseNId"));
        failed.put("version", pending.getString("version"));
        failed.put("errorCode", "UPDATE_HEALTH_TIMEOUT");
        writeJsonAtomic(new File(context.getFilesDir(), "failed-web-activation.json"), failed);
        pendingFile.delete();
        String lastGoodSlot = lastGoodFile.isFile() ? readJsonFile(lastGoodFile).optString("slot") : "";
        if (!pending.optString("slot").equals(lastGoodSlot)) {
            deleteRecursively(new File(new File(context.getFilesDir(), "web-slots"), pending.optString("slot")));
        }
    }

    private static void deleteRecursively(File file) {
        if (file.isDirectory()) {
            File[] children = file.listFiles();
            if (children != null) for (File child : children) deleteRecursively(child);
        }
        file.delete();
    }

    private JSObject toJsObject(JSONObject source) throws Exception {
        JSObject output = new JSObject();
        Iterator<String> keys = source.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            output.put(key, source.get(key));
        }
        return output;
    }

    private String sha256(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream input = new BufferedInputStream(new FileInputStream(file))) {
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) != -1) digest.update(buffer, 0, read);
        }
        StringBuilder output = new StringBuilder();
        for (byte value : digest.digest()) output.append(String.format("%02x", value));
        return output.toString();
    }

    private String readAll(InputStream input) throws Exception {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
        return output.toString(StandardCharsets.UTF_8.name());
    }

    private String errorCode(Exception error) {
        String message = error.getMessage();
        return message == null || message.length() == 0 ? "RUNTIME_NATIVE_FAILURE" : message;
    }

    private JSObject capability(String name, String availability, String reasonCode) {
        JSObject item = new JSObject();
        item.put("name", name);
        item.put("availability", availability);
        if (reasonCode != null) item.put("reasonCode", reasonCode);
        return item;
    }

    private boolean hasCameraPermission() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return true;
        return getContext().checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED;
    }

    private boolean isAllowedAuthKey(String key) {
        return key.equals("industrial-platform.auth.http.v1")
                || key.equals("industrial-platform.auth.single.v1");
    }

    private SharedPreferences securePreferences() {
        return getContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(KEY_ALIAS)) return ((SecretKey) store.getKey(KEY_ALIAS, null));
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .build());
        return generator.generateKey();
    }

    private String encrypt(String value) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        String iv = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP);
        String body = Base64.encodeToString(cipher.doFinal(value.getBytes(StandardCharsets.UTF_8)), Base64.NO_WRAP);
        return iv + SEPARATOR + body;
    }

    private String decrypt(String encoded) throws Exception {
        String[] parts = encoded.split(SEPARATOR, 2);
        if (parts.length != 2) throw new IllegalArgumentException("invalid secure payload");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)));
        return new String(cipher.doFinal(Base64.decode(parts[1], Base64.NO_WRAP)), StandardCharsets.UTF_8);
    }
}
