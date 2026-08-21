<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import {
  Avatar,
  FolderOpened,
  Lock,
  Monitor,
  Tickets,
  User,
  UserFilled,
} from '@element-plus/icons-vue'
import { ElIcon, ElTable, ElTableColumn, ElTag } from 'element-plus'

import type { LoginAuditItemDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'
import AppPage from '@/components/base/AppPage.vue'
import TimeGreetingHeader from '@/components/home/TimeGreetingHeader.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import type { TerminalType } from '@/device/types'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

const TERMINAL_LABELS: Record<TerminalType, string> = { pc: 'PC', pda: 'PDA', mobile: 'Mobile' }
const authStore = useAuthStore()
const deviceStore = useDeviceStore()
const runtimeConfig = loadRuntimeConfig()

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)
const authModeLabel = computed(() =>
  runtimeConfig.authMode === 'mock' ? 'Mock（演示模式）' : 'HTTP（真实服务）',
)
const serviceLabel = computed(() => {
  if (runtimeConfig.authMode === 'mock') return '演示数据'
  try {
    return new URL(runtimeConfig.apiBaseUrl).port === '5041' ? 'UnifiedHost' : 'API Gateway'
  } catch {
    return '统一 API'
  }
})

const quickActions = [
  {
    label: '用户管理',
    description: '管理用户账号与状态',
    route: 'identity-users',
    icon: User,
    permission: PERMISSIONS.userView,
  },
  {
    label: '用户组管理',
    description: '管理用户组与成员',
    route: 'identity-user-groups',
    icon: UserFilled,
    permission: PERMISSIONS.userGroupView,
  },
  {
    label: '角色权限',
    description: '角色分配与权限管理',
    route: 'identity-roles',
    icon: Avatar,
    permission: PERMISSIONS.roleView,
  },
  {
    label: '权限目录',
    description: '查看系统菜单与资源',
    route: 'identity-permissions',
    icon: FolderOpened,
    permission: PERMISSIONS.permissionView,
  },
  {
    label: '登录审计',
    description: '查看登录行为记录',
    route: 'identity-audits',
    icon: Tickets,
    permission: PERMISSIONS.auditLoginView,
  },
  {
    label: '企业登录源',
    description: '配置企业级登录源',
    route: 'sso-providers',
    icon: Lock,
    permission: PERMISSIONS.ssoView,
  },
] as const
const visibleQuickActions = computed(() =>
  quickActions.filter((action) => authStore.hasPermission(action.permission)),
)

const audits = ref<LoginAuditItemDto[]>([])
const auditsLoading = ref(false)
const auditsUnavailable = ref(false)

function formatOccurredOn(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

async function refreshHome(): Promise<void> {
  auditsUnavailable.value = false
  if (runtimeConfig.authMode !== 'http' || !authStore.hasPermission(PERMISSIONS.auditLoginView)) {
    audits.value = []
    return
  }
  auditsLoading.value = true
  try {
    audits.value = (await getManagementApi().listLoginAudits({ pageIndex: 1, pageSize: 4 })).items
  } catch {
    auditsUnavailable.value = true
  } finally {
    auditsLoading.value = false
  }
}

onMounted(() => {
  void refreshHome()
})
</script>

<template>
  <AppPage class="pc-home">
    <TimeGreetingHeader
      terminal="pc"
      :display-name="displayName"
      description="快速访问常用管理功能并查看系统状态"
      :refresh-loading="auditsLoading"
      @refresh="refreshHome"
    />

    <div class="pc-home__primary-grid">
      <section class="pc-home__section" aria-labelledby="quick-start-title">
        <header class="pc-home__section-header">
          <div>
            <h2 id="quick-start-title">快速开始</h2>
            <p>仅展示当前账号有权访问的功能</p>
          </div>
        </header>
        <div v-if="visibleQuickActions.length > 0" class="pc-home__quick-grid">
          <RouterLink
            v-for="action in visibleQuickActions"
            :key="action.route"
            :to="{ name: action.route }"
            class="pc-home__quick-action"
          >
            <ElIcon :size="32"><component :is="action.icon" /></ElIcon>
            <strong>{{ action.label }}</strong
            ><span>{{ action.description }}</span>
          </RouterLink>
        </div>
        <p v-else class="pc-home__empty">当前账号暂无其他管理入口。</p>
      </section>

      <section class="pc-home__section" aria-labelledby="environment-title">
        <header class="pc-home__section-header">
          <div>
            <h2 id="environment-title">服务与环境</h2>
            <p>当前系统服务及运行环境</p>
          </div>
        </header>
        <dl class="pc-home__environment-list">
          <div>
            <ElIcon><Monitor /></ElIcon>
            <div>
              <dt>当前终端</dt>
              <dd data-testid="terminal">{{ terminalLabel }}</dd>
            </div>
          </div>
          <div>
            <ElIcon><Lock /></ElIcon>
            <div>
              <dt>认证模式</dt>
              <dd data-testid="auth-mode">{{ authModeLabel }}</dd>
            </div>
          </div>
          <div>
            <ElIcon><Monitor /></ElIcon>
            <div>
              <dt>数据宿主</dt>
              <dd data-testid="data-source">{{ serviceLabel }} <em>已连接</em></dd>
            </div>
          </div>
          <div>
            <ElIcon><User /></ElIcon>
            <div>
              <dt>登录状态</dt>
              <dd>身份认证已完成</dd>
            </div>
          </div>
        </dl>
      </section>
    </div>

    <section class="pc-home__section pc-home__audits" aria-labelledby="audit-title">
      <header class="pc-home__section-header pc-home__section-header--row">
        <div>
          <h2 id="audit-title">最近登录审计</h2>
          <p>来自 Identity 服务的最新登录行为</p>
        </div>
        <RouterLink
          v-if="authStore.hasPermission(PERMISSIONS.auditLoginView)"
          :to="{ name: 'identity-audits' }"
          >查看全部</RouterLink
        >
      </header>
      <ElTable v-if="audits.length > 0" v-loading="auditsLoading" :data="audits" size="small">
        <ElTableColumn label="时间" min-width="170"
          ><template #default="{ row }">{{
            formatOccurredOn(row.occurredOn)
          }}</template></ElTableColumn
        >
        <ElTableColumn prop="loginNameSnapshot" label="用户" min-width="140" />
        <ElTableColumn label="结果" width="100"
          ><template #default="{ row }"
            ><ElTag :type="row.success ? 'success' : 'danger'" effect="plain" size="small">{{
              row.success ? '成功' : '失败'
            }}</ElTag></template
          ></ElTableColumn
        >
        <ElTableColumn prop="traceId" label="追踪标识" min-width="220" show-overflow-tooltip />
      </ElTable>
      <div v-else class="pc-home__audit-empty" v-loading="auditsLoading">
        {{
          auditsUnavailable ? '暂时无法读取登录审计，请稍后重试。' : '暂无可展示的登录审计记录。'
        }}
      </div>
    </section>
  </AppPage>
</template>

<style scoped>
.pc-home {
  box-sizing: border-box;
  gap: var(--ip-space-5);
  padding: var(--ip-space-5) var(--ip-space-6) var(--ip-space-6);
}

.pc-home :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-5);
}

.pc-home__page-header,
.pc-home__section-header,
.pc-home__section-header--row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-4);
}
.pc-home__page-header {
  position: relative;
  overflow: hidden;
  padding: var(--ip-space-4) var(--ip-space-5);
  background:
    radial-gradient(
      ellipse at 88% 24%,
      color-mix(in srgb, var(--pc-home-period-accent) var(--pc-home-period-strength), transparent) 0,
      color-mix(in srgb, var(--pc-home-period-accent) 5%, transparent) 44%,
      transparent 72%
    ),
    color-mix(in srgb, var(--pc-home-period-accent) 4%, var(--ip-color-bg-container));
  border: 1px solid color-mix(in srgb, var(--pc-home-period-accent) 12%, var(--ip-color-border));
  border-radius: var(--ip-radius-lg);
  box-shadow: 0 4px 14px color-mix(in srgb, var(--ip-color-text-primary) 5%, transparent);
  transition:
    border-color 300ms ease,
    box-shadow 300ms ease;
}
.pc-home__page-header h1 {
  margin: 0;
  font-size: var(--ip-font-size-xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
  color: var(--ip-color-text-primary);
}
.pc-home__greeting {
  min-width: 0;
}
.pc-home__header-actions {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-4);
}
.pc-home__clock {
  min-width: 116px;
  color: var(--ip-color-text-primary);
  font-size: 28px;
  font-variant-numeric: tabular-nums;
  font-weight: 300;
  line-height: 1;
  letter-spacing: 0.02em;
  text-align: right;
}
.pc-home__welcome,
.pc-home__section-header p {
  margin: var(--ip-space-1) 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  font-weight: 400;
  line-height: var(--ip-line-height-normal);
}
.pc-home__primary-grid {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(280px, 1fr);
  gap: var(--ip-space-4);
}
.pc-home__section {
  padding: var(--ip-space-5);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}
.pc-home__section h2 {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  color: var(--ip-color-text-primary);
}
.pc-home__quick-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--ip-space-3);
  margin-top: var(--ip-space-4);
}
.pc-home__quick-action {
  display: flex;
  min-height: 132px;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-2);
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  color: var(--ip-color-primary);
  text-align: center;
  text-decoration: none;
  transition:
    border-color 150ms ease,
    background-color 150ms ease;
}
.pc-home__quick-action:hover,
.pc-home__quick-action:focus-visible {
  background: var(--ip-color-primary-bg);
  border-color: var(--ip-color-primary);
}
.pc-home__quick-action:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 2px;
}
.pc-home__quick-action strong {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}
.pc-home__quick-action span {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.pc-home__environment-list {
  margin: var(--ip-space-4) 0 0;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}
.pc-home__environment-list > div {
  display: flex;
  align-items: center;
  gap: var(--ip-space-4);
  padding: var(--ip-space-4);
}
.pc-home__environment-list > div + div {
  border-top: 1px solid var(--ip-color-border);
}
.pc-home__environment-list .el-icon {
  flex: 0 0 auto;
  color: var(--ip-color-primary);
  font-size: 24px;
}
.pc-home__environment-list > div > div {
  min-width: 0;
}
.pc-home__environment-list dt {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
}
.pc-home__environment-list dd {
  margin: var(--ip-space-1) 0 0;
  color: var(--ip-color-text-primary);
  font-weight: 500;
}
.pc-home__environment-list em {
  color: var(--ip-color-success);
  font-style: normal;
}
.pc-home__audits {
  min-height: 210px;
}
.pc-home__section-header--row > a {
  color: var(--ip-color-primary);
  text-decoration: none;
}
.pc-home__audit-empty,
.pc-home__empty {
  display: flex;
  min-height: 120px;
  align-items: center;
  justify-content: center;
  margin: var(--ip-space-4) 0 0;
  color: var(--ip-color-text-secondary);
}
@media (max-width: 1100px) {
  .pc-home__primary-grid {
    grid-template-columns: 1fr;
  }
}
@media (max-width: 760px) {
  .pc-home {
    padding: var(--ip-space-4);
  }

  .pc-home__quick-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .pc-home__page-header {
    align-items: flex-start;
  }

  .pc-home__clock {
    display: none;
  }
}
@media (prefers-reduced-motion: reduce) {
  .pc-home__quick-action,
  .pc-home__page-header {
    transition: none;
  }
}
</style>
