import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.industrialplatform.pda',
  appName: 'Industrial Platform',
  webDir: '../../frontend/dist',
  android: {
    allowMixedContent: false,
    captureInput: true,
  },
  plugins: {
    SplashScreen: {
      launchAutoHide: false,
    },
    BarcodeScanner: {
      cameraDirection: 'back',
    },
  },
}

export default config
