<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { ElButton } from 'element-plus'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

type Terminal = 'pc' | 'pda' | 'mobile'
type DayPeriod =
  'overnight' | 'dawn' | 'morning' | 'midday' | 'afternoon' | 'evening' | 'late-night'

const props = withDefaults(
  defineProps<{
    displayName: string
    description: string
    terminal: Terminal
    refreshLoading?: boolean
  }>(),
  { refreshLoading: false },
)

const emit = defineEmits<{ refresh: [] }>()
const locale = usePlatformLocale()
const homeCopy = computed(() => localeMessages[locale.value].home)

const now = ref(new Date())
let clockTimer: ReturnType<typeof setInterval> | undefined

const dayPeriod = computed<DayPeriod>(() => {
  const hour = now.value.getHours()
  if (hour < 5) return 'overnight'
  if (hour < 8) return 'dawn'
  if (hour < 12) return 'morning'
  if (hour < 14) return 'midday'
  if (hour < 18) return 'afternoon'
  if (hour < 22) return 'evening'
  return 'late-night'
})

const greeting = computed(() => {
  const key = dayPeriod.value === 'late-night' ? 'lateNight' : dayPeriod.value
  return homeCopy.value.greeting[key]
})

const welcomeLabel = computed(() =>
  locale.value === 'en-US'
    ? `${greeting.value}, ${props.displayName}`
    : `${greeting.value}，${props.displayName}`,
)

const periodStyle = computed<Record<string, string>>(() => {
  const palettes: Record<DayPeriod, [string, string]> = {
    overnight: ['color-mix(in srgb, var(--ip-color-primary) 74%, var(--ip-color-info))', '18%'],
    dawn: ['color-mix(in srgb, var(--ip-color-primary) 45%, var(--ip-color-warning))', '14%'],
    morning: ['color-mix(in srgb, var(--ip-color-primary) 65%, var(--ip-color-success))', '15%'],
    midday: ['color-mix(in srgb, var(--ip-color-primary) 35%, var(--ip-color-warning))', '17%'],
    afternoon: ['var(--ip-color-primary)', '16%'],
    evening: ['color-mix(in srgb, var(--ip-color-primary) 55%, var(--ip-color-warning))', '17%'],
    'late-night': ['color-mix(in srgb, var(--ip-color-primary) 82%, var(--ip-color-info))', '20%'],
  }
  const [accent, strength] = palettes[dayPeriod.value]
  return {
    '--time-greeting-accent': accent,
    '--time-greeting-strength': strength,
    // 保留 PC 已有的变量名,让现有主题/视觉回归断言保持兼容。
    '--pc-home-period-accent': accent,
    '--pc-home-period-strength': strength,
  }
})

const dateLabel = computed(() =>
  now.value.toLocaleDateString(locale.value, {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    weekday: 'long',
  }),
)

const clockLabel = computed(() => {
  const pad = (value: number): string => String(value).padStart(2, '0')
  return `${pad(now.value.getHours())}:${pad(now.value.getMinutes())}:${pad(now.value.getSeconds())}`
})

function refresh(): void {
  now.value = new Date()
  emit('refresh')
}

onMounted(() => {
  clockTimer = setInterval(() => {
    now.value = new Date()
  }, 1000)
})

onUnmounted(() => {
  if (clockTimer !== undefined) clearInterval(clockTimer)
})
</script>

<template>
  <header
    class="time-greeting-header"
    :class="[
      `time-greeting-header--${dayPeriod}`,
      `time-greeting-header--${props.terminal}`,
      props.terminal === 'pc' ? `pc-home__page-header--${dayPeriod}` : undefined,
      props.terminal === 'pc' ? 'pc-home__page-header' : undefined,
    ]"
    :style="periodStyle"
    data-testid="time-header"
  >
    <div class="time-greeting-header__greeting">
      <h1 data-testid="time-greeting">
        <span data-testid="welcome">{{ welcomeLabel }}</span>
      </h1>
      <p class="time-greeting-header__date" data-testid="welcome-description">
        <span data-testid="time-date">{{ dateLabel }}</span> · {{ props.description }}
      </p>
    </div>
    <div class="time-greeting-header__actions">
      <time class="time-greeting-header__clock pc-home__clock" data-testid="live-clock">
        {{ clockLabel }}
      </time>
      <ElButton
        class="time-greeting-header__refresh"
        :icon="Refresh"
        :loading="props.refreshLoading"
        plain
        data-testid="refresh-home"
        @click="refresh"
      >
        {{ localeMessages[locale].common.action.refresh }}
      </ElButton>
    </div>
  </header>
</template>

<style scoped>
.time-greeting-header {
  --time-greeting-surface: color-mix(
    in srgb,
    var(--time-greeting-accent) 4%,
    var(--ip-color-bg-container)
  );

  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-4);
  overflow: hidden;
  padding: var(--ip-space-4) var(--ip-space-5);
  background:
    radial-gradient(
      ellipse at 88% 24%,
      color-mix(in srgb, var(--time-greeting-accent) var(--time-greeting-strength), transparent) 0,
      color-mix(in srgb, var(--time-greeting-accent) 5%, transparent) 44%,
      transparent 72%
    ),
    var(--time-greeting-surface);
  border: 1px solid color-mix(in srgb, var(--time-greeting-accent) 12%, var(--ip-color-border));
  border-radius: var(--ip-radius-lg);
  box-shadow: 0 4px 14px color-mix(in srgb, var(--ip-color-text-primary) 5%, transparent);
  transition:
    border-color 300ms ease,
    box-shadow 300ms ease;
}

.time-greeting-header__greeting {
  min-width: 0;
}

.time-greeting-header h1 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
}

.time-greeting-header__date {
  margin: var(--ip-space-1) 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  font-weight: 400;
  line-height: var(--ip-line-height-normal);
}

.time-greeting-header__actions {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-4);
}

.time-greeting-header__clock {
  min-width: 116px;
  color: var(--ip-color-text-primary);
  font-size: 28px;
  font-variant-numeric: tabular-nums;
  font-weight: 300;
  line-height: 1;
  letter-spacing: 0.02em;
  text-align: right;
}

.time-greeting-header__refresh {
  min-height: var(--ip-touch-min-size-mobile);
}

.time-greeting-header--pda,
.time-greeting-header--mobile {
  padding: var(--ip-space-4);
}

.time-greeting-header--pda .time-greeting-header__clock,
.time-greeting-header--mobile .time-greeting-header__clock {
  min-width: auto;
  font-size: 22px;
}

@media (max-width: 560px) {
  .time-greeting-header--pda,
  .time-greeting-header--mobile {
    align-items: flex-start;
    flex-direction: column;
    gap: var(--ip-space-3);
  }

  .time-greeting-header--pda .time-greeting-header__actions,
  .time-greeting-header--mobile .time-greeting-header__actions {
    width: 100%;
    justify-content: space-between;
  }

  .time-greeting-header--mobile .time-greeting-header__refresh,
  .time-greeting-header--pda .time-greeting-header__refresh {
    min-width: 88px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .time-greeting-header {
    transition: none;
  }
}
</style>
