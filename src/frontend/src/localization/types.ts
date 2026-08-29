export type SupportedLocale = 'zh-CN' | 'en-US'

export interface LocalePreferences {
  locale: SupportedLocale
  timeZone: string
  dateFormat: 'yyyy-MM-dd' | 'MM/dd/yyyy'
  numberLocale: SupportedLocale
  unitSystem: 'metric'
}

export interface PlatformLocaleMessages {
  common: {
    brand: { name: string; description: string }
    action: {
      search: string
      reset: string
      save: string
      cancel: string
      close: string
      confirm: string
      refresh: string
      settings: string
      fullscreen: string
      exitFullscreen: string
      logout: string
      retry: string
    }
    state: {
      loading: string
      empty: string
      error: string
      forbidden: string
      comingSoon: string
      available: string
    }
  }
  shell: {
    navigation: { workspace: string; platformManagement: string }
    top: {
      environment: string
      tenant: string
      noTenant: string
      terminal: string
      globalSearch: string
      serviceStatus: string
      userMenu: string
      lock: string
      mode: string
    }
    commandSearch: { placeholder: string; empty: string; recent: string; commands: string }
    mode: { management: string; operation: string }
  }
  operation: {
    title: string
    description: string
    settingsDescription: string
    launcherState: { available: string; comingSoon: string }
    launchers: Record<string, string>
  }
  identity: {
    user: {
      title: string
      description: string
      loginName: string
      name: string
      status: string
      email: string
      phone: string
      createdOn: string
      lastLoginOn: string
    }
    status: { enabled: string; disabled: string; deleted: string }
  }
  systemData: { runtimeStatus: string; notConfigured: string }
}
