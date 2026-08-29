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
    query: {
      title: string
      submit: string
      reset: string
      expand: string
      collapse: string
    }
    table: {
      businessActions: string
      primaryTools: string
      auxiliaryTools: string
      excel: string
      csv: string
      html: string
      xml: string
      txt: string
      rangeSeparator: string
      rangeStart: string
      rangeEnd: string
      queryHeader: string
      queryTop: string
      sort: string
      sortSettings: string
      sortHint: string
      ascending: string
      descending: string
      clearSort: string
      group: string
      groupSettings: string
      groupHint: string
      clearGroup: string
      quickSearch: string
      download: string
      downloadSettings: string
      downloadData: string
      downloadHint: string
      serverExportRequired: string
      serverExportUsed: string
      loadedDataOnly: string
      fileName: string
      saveType: string
      saveData: string
      currentPage: string
      selectedRows: string
      allData: string
      customData: string
      customExportQuantity: string
      selectFields: string
      selectFieldsHint: string
      cancel: string
      downloadConfirm: string
      print: string
      printCurrentTitle: string
      printTitleSuffix: string
      printSettings: string
      printData: string
      printHint: string
      title: string
      selectData: string
      columnWidth: string
      currentColumnWidth: string
      adaptiveWidth: string
      printFieldsHint: string
      rowSettings: string
      rowSettingsHint: string
      showIndex: string
      showBorder: string
      density: string
      defaultDensity: string
      mediumDensity: string
      compactDensity: string
      done: string
      detail: string
      select: string
      actions: string
      loading: string
      selectionSummary: string
      clearSelection: string
      fullscreen: string
      exitFullscreen: string
      clearQuery: string
      refresh: string
      columnSettings: string
      columnSettingsHint: string
      querySuffix: string
      dateRange: string
      sortDirection: string
      unset: string
      exportConfirm: string
      resetDefault: string
      columnVisible: string
      indexColumn: string
      border: string
      resetWidth: string
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
    copy: {
      tenant: string
      searchMenu: string
      tabList: string
      experienceMode: string
      globalSearch: string
      expandFunctionTree: string
      collapseFunctionTree: string
      platformGroups: string
      workspaceLocked: string
      currentPassword: string
      unlocking: string
      unlock: string
      tabLimitTitle: string
      tabLimitDescription: string
      tabClose: string
      tabCloseLeft: string
      tabCloseRight: string
      tabCloseOthers: string
      tabCloseAll: string
      tabPin: string
      tabUnpin: string
      tabFocus: string
      tabFocusExit: string
      tabReuse: string
      tabCloseAndOpen: string
    }
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
      breadcrumb: string
      queryTitle: string
      businessId: string
      group: string
      role: string
      includeDeleted: string
      create: string
      tableActions: string
      enabled: string
      disabled: string
      mustChangePassword: string
      noChangePassword: string
      effectiveRoles: string
      detail: string
      edit: string
      disable: string
      enable: string
      assignRole: string
      resetPassword: string
      restore: string
      delete: string
      userCountSuffix: string
      more: string
      copy: {
        dialogDetail: string
        close: string
        optionalAuto: string
        loginPlaceholder: string
        displayNamePlaceholder: string
        optional: string
        assignRole: string
        resetPassword: string
        confirmReset: string
        passwordDescription: string
        pagePath: string
        directRoles: string
        inheritedRoles: string
        effectiveRoles: string
        needsChange: string
        noChange: string
        rolesDescription: string
        statusDisableConfirm: string
        statusEnableConfirm: string
        statusConfirmTitle: string
        statusUpdated: string
        statusActionFailed: string
        createdSuccess: string
        createdDescription: string
        updatedSuccess: string
        saveFailed: string
        loadFailed: string
        rolesUpdated: string
        rolesSaveFailed: string
        passwordResetSuccess: string
        passwordResetFailed: string
        deleteConfirm: string
        deleteTitle: string
        deleteConfirmButton: string
        deleteReasonPlaceholder: string
        deletedSuccess: string
        deleteFailed: string
        restoreConfirm: string
        restoreTitle: string
        restoreConfirmButton: string
        restoreReasonPlaceholder: string
        restoredSuccess: string
        restoreFailed: string
        businessIdRule: string
        loginRequired: string
        loginLength: string
        nameRequired: string
        emailInvalid: string
      }
    }
    status: { enabled: string; disabled: string; deleted: string }
  }
  systemData: {
    runtimeStatus: string
    notConfigured: string
    copy: {
      queryAndActions: string
      refresh: string
      savingOrReading: string
      interfaceUnavailable: string
      retry: string
      degraded: string
      snapshotUnavailable: string
      conflict: string
    }
  }
}
