export type SupportedLocale = 'zh-CN' | 'en-US'

export interface IdentityManagementCommonCopy {
  queryTitle: string
  search: string
  reset: string
  refresh: string
  expandAll: string
  collapseAll: string
  totalSuffix: string
  actions: string
  create: string
  edit: string
  save: string
  cancel: string
  enabled: string
  disabled: string
  status: string
  name: string
  description: string
  businessId: string
  optional: string
  page: string
  operation: string
  yes: string
  no: string
  pagePath: string
}

export interface IdentityManagementMessages {
  common: IdentityManagementCommonCopy
  userGroups: {
    title: string; description: string; breadcrumb: string; countSuffix: string; name: string; groupNId: string
    descriptionColumn: string; memberCount: string; roleCount: string; includeDeleted: string; create: string; edit: string
    members: string; roles: string; delete: string; restore: string; enable: string; disable: string; createTitle: string
    editTitle: string; memberTitle: string; roleTitle: string; selectUser: string; selectRole: string
    initialMembers: string; initialRoles: string; memberDescription: string; roleDescription: string
  }
  roles: {
    title: string; description: string; breadcrumb: string; countSuffix: string; roleName: string; roleNId: string
    descriptionColumn: string; systemRole: string; permissionCount: string; create: string; edit: string
    assignPermissions: string; createTitle: string; editTitle: string; permissionDescription: string
  }
  permissions: {
    title: string; description: string; breadcrumb: string; countSuffix: string; filter: string; page: string; operation: string
  }
  audits: {
    title: string; description: string; breadcrumb: string; countSuffix: string; userNId: string; result: string
    success: string; failed: string; time: string; loginName: string; failureCode: string; ipHash: string; uaHash: string
    traceId: string; hint: string
  }
  ssoProviders: {
    title: string; description: string; breadcrumb: string; countSuffix: string; create: string; name: string; protocol: string
    authority: string; clientId: string; secret: string; autoRedirect: string; status: string; createdOn: string
    configured: string; notConfigured: string; enabled: string; disabled: string; edit: string; secretAction: string
    accounts: string; test: string; createTitle: string; editTitle: string; secretTitle: string; accountTitle: string
    callbackPath: string; provisioningMode: string; logoutMode: string; allowedEmailDomains: string; defaultRole: string
    secretReference: string; bindUser: string; externalSubject: string; externalName: string; externalEmail: string; lastLoginOn: string; bind: string; unbind: string
    manualProvisioning: string; jitProvisioning: string; localLogout: string; federatedLogout: string
    clientIdPlaceholder: string; callbackPlaceholder: string; autoRedirectHint: string; allowedEmailDomainsPlaceholder: string; defaultRolePlaceholder: string; secretDescription: string; secretReferencePlaceholder: string; userNIdPlaceholder: string; externalSubjectPlaceholder: string
  }
  ssoClients: {
    title: string; description: string; breadcrumb: string; countSuffix: string; create: string; name: string; clientId: string
    endpointCount: string; status: string; createdOn: string; enabled: string; disabled: string; edit: string; endpoints: string
    endpointTitle: string; createTitle: string; editTitle: string; type: string; uri: string; register: string; remove: string
    redirect: string; postLogoutRedirect: string; origin: string
  }
}

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
      skipToContent: string
      exitFocusMode: string
      logout: string
      retry: string
    }
    formSurface: {
      centeredModal: string
      rightDrawer: string
      switchToCenteredModal: string
      switchToRightDrawer: string
      submitting: string
    }
    state: {
      loading: string
      empty: string
      error: string
      forbidden: string
      comingSoon: string
      available: string
      unauthenticated: string
    }
    errors: {
      network: string
      timeout: string
      business: string
      unauthorized: string
      forbidden: string
      notFound: string
      server: string
      invalidResponse: string
      cancelled: string
      unknown: string
      conflictTitle: string
      conflictMessage: string
      reload: string
    }
    locale: { label: string; zhCN: string; enUS: string }
    theme: {
      label: string
      palette: string
      mode: string
      density: string
      palettes: { industrialCyan: string; technologyBlue: string; neutralGray: string }
      modes: { light: string; dark: string; system: string }
      densities: { comfortable: string; compact: string }
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
      queryHeaderLabel: string
      queryTopLabel: string
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
    navigation: {
      workspace: string
      platformManagement: string
      group: Record<string, string>
      section: Record<string, string>
      item: Record<string, string>
    }
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
      messages: string
      onlineUsers: string
      profile: string
      clearCache: string
      notification: string
      notificationUnavailable: string
      notificationEmpty: string
      sendMessage: string
      sendMessageUnavailable: string
      onlineUsersDescription: string
      onlineUsersEmpty: string
      revokeSession: string
      revokeSessionConfirm: string
      revokeSessionSuccess: string
      profileAccount: string
      profileName: string
      profileTenant: string
      profileRoles: string
      changePassword: string
      cacheCleared: string
      clearCacheConfirm: string
      more: string
      moreNavigation: string
      index: string
      loginTime: string
      lastRefresh: string
      expires: string
      currentSession: string
    }
    commandSearch: { placeholder: string; empty: string; recent: string; commands: string }
    mode: { management: string; operation: string }
    copy: {
      tenant: string
      searchMenu: string
      tabList: string
      tabActions: string
      tabReload: string
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
      menuSearchHint: string
    }
  }
  operation: {
    title: string
    description: string
    settingsDescription: string
    launcherState: { available: string; comingSoon: string }
    launchers: Record<string, string>
  }
  home: {
    greeting: {
      overnight: string
      dawn: string
      morning: string
      midday: string
      afternoon: string
      evening: string
      lateNight: string
    }
    description: string
    quickStart: string
    quickStartDescription: string
    noQuickActions: string
    quickActions: Record<string, { label: string; description: string }>
    environment: string
    environmentDescription: string
    currentTerminal: string
    authMode: string
    dataHost: string
    connected: string
    loginStatus: string
    authenticated: string
    auditTitle: string
    auditDescription: string
    viewAll: string
    time: string
    user: string
    result: string
    traceId: string
    success: string
    failure: string
    auditUnavailable: string
    auditEmpty: string
    mockMode: string
    httpMode: string
    demoData: string
    unifiedApi: string
  }
  changePassword: {
    title: string
    subtitle: string
    currentPassword: string
    newPassword: string
    confirmPassword: string
    showCurrentPassword: string
    hideCurrentPassword: string
    showNewPassword: string
    hideNewPassword: string
    currentPasswordRequired: string
    passwordPolicy: string
    passwordsMismatch: string
    submitting: string
    submit: string
    logout: string
  }
  login: {
    title: string
    subtitle: string
    username: string
    password: string
    showPassword: string
    hidePassword: string
    usernameRequired: string
    passwordRequired: string
    submitting: string
    submit: string
    methodToggle: string
    methodPanelTitle: string
    methodPanelClose: string
    methodOptionsLabel: string
    currentAccount: string
    usernamePassword: string
    domain: string
    domainDescription: string
    sso: string
    ssoDescription: string
    ssoHttpDescription: string
    mockMode: string
    demoCredentials: string
    bootstrapRecoveryRequired: string
    bootstrapPending: string
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
      moreConditions: string
      userList: string
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
    management: IdentityManagementMessages
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
