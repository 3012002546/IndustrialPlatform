import type { CollaborationLocaleMessages } from '@/localization/types'

type MediaCopy = CollaborationLocaleMessages['media']

export function formatMediaError(copy: MediaCopy, code: string | null): string | null {
  if (!code) return null
  const messages: Record<string, string> = {
    MEDIA_DISABLED: copy.errors.disabled,
    MEDIA_BUSY: copy.errors.busy,
    MEDIA_FORBIDDEN: copy.errors.forbidden,
    MEDIA_NOT_FOUND: copy.errors.notFound,
    MEDIA_NOT_ACCEPTED: copy.errors.notAccepted,
    MEDIA_UNSUPPORTED: copy.errors.unsupported,
    MEDIA_DEPENDENCY_UNAVAILABLE: copy.errors.unavailable,
    MEDIA_SIGNAL_FAILED: copy.errors.signalFailed,
    MEDIA_MIC_PERMISSION_DENIED: copy.errors.microphonePermissionDenied,
    MEDIA_MIC_UNAVAILABLE: copy.errors.microphoneUnavailable,
    MEDIA_SCREEN_CAPTURE_CANCELLED: copy.errors.screenCaptureCancelled,
    MEDIA_SECURE_CONTEXT_REQUIRED: copy.errors.secureContextRequired,
    MEDIA_ICE_CONFIGURATION: copy.errors.iceConfiguration,
    MEDIA_CAPTURE_FAILED: copy.errors.captureFailed,
  }
  return `${messages[code] ?? copy.errors.unknown}（${code}）`
}
