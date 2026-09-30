package com.industrialplatform.pda;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.util.Base64;
import android.util.DisplayMetrics;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

public final class ScreenCaptureService extends Service {
    static final String ACTION_START = "com.industrialplatform.pda.SCREEN_START";
    static final String ACTION_STOP = "com.industrialplatform.pda.SCREEN_STOP";
    static final String EXTRA_RESULT_CODE = "resultCode";
    static final String EXTRA_RESULT_DATA = "resultData";
    private static final String CHANNEL_ID = "industrial-platform-screen-share";
    private static final int NOTIFICATION_ID = 1606;

    interface Listener {
        void onFrame(String base64Jpeg, int width, int height);
        void onStopped(String errorCode);
    }

    static volatile Listener listener;

    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private HandlerThread worker;
    private long lastFrameTime;
    private boolean stopping;

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || ACTION_STOP.equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (!ACTION_START.equals(intent.getAction()) || projection != null) return START_NOT_STICKY;
        try {
            startForegroundCapture();
            int resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, 0);
            Intent resultData = Build.VERSION.SDK_INT >= 33
                    ? intent.getParcelableExtra(EXTRA_RESULT_DATA, Intent.class)
                    : intent.getParcelableExtra(EXTRA_RESULT_DATA);
            if (resultData == null) throw new IllegalStateException("MEDIA_CAPTURE_FAILED");
            MediaProjectionManager manager = (MediaProjectionManager) getSystemService(Context.MEDIA_PROJECTION_SERVICE);
            projection = manager.getMediaProjection(resultCode, resultData);
            if (projection == null) throw new IllegalStateException("MEDIA_CAPTURE_FAILED");
            worker = new HandlerThread("industrial-screen-capture");
            worker.start();
            Handler handler = new Handler(worker.getLooper());
            projection.registerCallback(new MediaProjection.Callback() {
                @Override
                public void onStop() {
                    stopSelf();
                }
            }, handler);

            DisplayMetrics metrics = getResources().getDisplayMetrics();
            int width = Math.max(2, Math.round(Math.min(metrics.widthPixels, 960) / 2f) * 2);
            int height = Math.max(2, Math.round((float) metrics.heightPixels * width / metrics.widthPixels / 2f) * 2);
            reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);
            reader.setOnImageAvailableListener(this::onImageAvailable, handler);
            display = projection.createVirtualDisplay("Industrial Platform screen share", width, height,
                    metrics.densityDpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                    reader.getSurface(), null, handler);
            return START_NOT_STICKY;
        } catch (Exception error) {
            Listener current = listener;
            if (current != null) current.onStopped("MEDIA_CAPTURE_FAILED");
            stopSelf();
            return START_NOT_STICKY;
        }
    }

    private void startForegroundCapture() {
        NotificationManager manager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            manager.createNotificationChannel(new NotificationChannel(CHANNEL_ID,
                    "屏幕共享", NotificationManager.IMPORTANCE_LOW));
        }
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this);
        Notification notification = builder.setSmallIcon(android.R.drawable.ic_menu_share)
                .setContentTitle("Industrial Platform 正在共享屏幕")
                .setContentText("返回应用可停止共享")
                .setOngoing(true).build();
        if (Build.VERSION.SDK_INT >= 29)
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
        else startForeground(NOTIFICATION_ID, notification);
    }

    private void onImageAvailable(ImageReader source) {
        Image image = source.acquireLatestImage();
        if (image == null) return;
        try {
            long now = android.os.SystemClock.elapsedRealtime();
            if (now - lastFrameTime < 200 || listener == null) return;
            lastFrameTime = now;
            Image.Plane plane = image.getPlanes()[0];
            int width = image.getWidth();
            int height = image.getHeight();
            int rowWidth = plane.getRowStride() / plane.getPixelStride();
            ByteBuffer pixels = plane.getBuffer();
            Bitmap padded = Bitmap.createBitmap(rowWidth, height, Bitmap.Config.ARGB_8888);
            padded.copyPixelsFromBuffer(pixels);
            Bitmap frame = Bitmap.createBitmap(padded, 0, 0, width, height);
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            frame.compress(Bitmap.CompressFormat.JPEG, 80, bytes);
            frame.recycle();
            padded.recycle();
            Listener current = listener;
            if (current != null) current.onFrame(Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP), width, height);
        } catch (Exception ignored) {
            // A transient frame failure must not end the projection; the next frame can still succeed.
        } finally {
            image.close();
        }
    }

    @Override
    public void onDestroy() {
        if (!stopping) {
            stopping = true;
            if (reader != null) {
                reader.setOnImageAvailableListener(null, null);
                reader.close();
                reader = null;
            }
            if (display != null) {
                display.release();
                display = null;
            }
            if (projection != null) {
                projection.stop();
                projection = null;
            }
            if (worker != null) {
                worker.quitSafely();
                worker = null;
            }
            Listener current = listener;
            listener = null;
            if (current != null) current.onStopped(null);
        }
        super.onDestroy();
    }
}
