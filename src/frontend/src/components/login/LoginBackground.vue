<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

interface NetworkNode {
  x: number
  y: number
  vx: number
  vy: number
  phase: number
}

const canvasRef = ref<HTMLCanvasElement | null>(null)
const nodes: NetworkNode[] = []
let animationFrame: number | null = null
let context: CanvasRenderingContext2D | null = null
let cssWidth = 0
let cssHeight = 0
let primaryColor = '#1677ff'
let resizeObserver: ResizeObserver | null = null
let themeObserver: MutationObserver | null = null
let reducedMotion: MediaQueryList | null = null

function readThemeColor(): void {
  const value = getComputedStyle(document.documentElement)
    .getPropertyValue('--ip-color-primary')
    .trim()
  primaryColor = value || '#1677ff'
}

function seedNodes(): void {
  const count = cssWidth < 720 ? 16 : 34
  nodes.length = 0
  for (let index = 0; index < count; index += 1) {
    nodes.push({
      x: Math.random(),
      y: Math.random(),
      vx: 0.00003 + Math.random() * 0.00005,
      vy: (Math.random() - 0.5) * 0.000025,
      phase: Math.random() * Math.PI * 2,
    })
  }
}

function resizeCanvas(): void {
  const canvas = canvasRef.value
  if (canvas === null || context === null) return
  const bounds = canvas.getBoundingClientRect()
  cssWidth = Math.max(1, bounds.width)
  cssHeight = Math.max(1, bounds.height)
  const pixelRatio = Math.min(window.devicePixelRatio || 1, 2)
  canvas.width = Math.round(cssWidth * pixelRatio)
  canvas.height = Math.round(cssHeight * pixelRatio)
  context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0)
  readThemeColor()
  seedNodes()
  draw(performance.now(), false)
}

function quadraticPoint(
  start: readonly [number, number],
  control: readonly [number, number],
  end: readonly [number, number],
  progress: number,
): readonly [number, number] {
  const inverse = 1 - progress
  return [
    inverse * inverse * start[0] +
      2 * inverse * progress * control[0] +
      progress * progress * end[0],
    inverse * inverse * start[1] +
      2 * inverse * progress * control[1] +
      progress * progress * end[1],
  ]
}

function drawFlowLanes(ctx: CanvasRenderingContext2D, time: number): void {
  const center: readonly [number, number] = [cssWidth * 0.5, cssHeight * 0.52]
  const lanes: ReadonlyArray<readonly [readonly [number, number], readonly [number, number]]> = [
    [
      [-30, cssHeight * 0.26],
      [cssWidth * 0.24, cssHeight * 0.2],
    ],
    [
      [-30, cssHeight * 0.78],
      [cssWidth * 0.25, cssHeight * 0.84],
    ],
    [
      [cssWidth * 0.18, cssHeight + 30],
      [cssWidth * 0.31, cssHeight * 0.7],
    ],
  ]

  ctx.strokeStyle = primaryColor
  ctx.lineWidth = 1
  ctx.globalAlpha = 0.12
  for (const [start, control] of lanes) {
    ctx.beginPath()
    ctx.moveTo(start[0], start[1])
    ctx.quadraticCurveTo(control[0], control[1], center[0], center[1])
    ctx.stroke()
  }

  for (let index = 0; index < 9; index += 1) {
    const lane = lanes[index % lanes.length]!
    const progress = (time * 0.000055 + index * 0.19) % 1
    const point = quadraticPoint(lane[0], lane[1], center, progress)
    ctx.globalAlpha = 0.2 + progress * 0.55
    ctx.beginPath()
    ctx.arc(point[0], point[1], 1.5 + progress * 1.4, 0, Math.PI * 2)
    ctx.fillStyle = primaryColor
    ctx.fill()
  }
}

function draw(time: number, advance: boolean): void {
  const ctx = context
  if (ctx === null) return
  ctx.clearRect(0, 0, cssWidth, cssHeight)
  drawFlowLanes(ctx, time)

  const connectionDistance = Math.min(170, Math.max(105, cssWidth * 0.12))
  const connectionDistanceSquared = connectionDistance * connectionDistance
  for (let index = 0; index < nodes.length; index += 1) {
    const node = nodes[index]!
    if (advance) {
      node.x = (node.x + node.vx * 16 + 1) % 1
      node.y = (node.y + node.vy * 16 + 1) % 1
    }
    const x = node.x * cssWidth
    const y = node.y * cssHeight
    for (let peerIndex = index + 1; peerIndex < nodes.length; peerIndex += 1) {
      const peer = nodes[peerIndex]!
      const peerX = peer.x * cssWidth
      const peerY = peer.y * cssHeight
      const deltaX = peerX - x
      const deltaY = peerY - y
      const distanceSquared = deltaX * deltaX + deltaY * deltaY
      if (distanceSquared > connectionDistanceSquared) continue
      ctx.globalAlpha = 0.07 * (1 - distanceSquared / connectionDistanceSquared)
      ctx.strokeStyle = primaryColor
      ctx.beginPath()
      ctx.moveTo(x, y)
      ctx.lineTo(peerX, peerY)
      ctx.stroke()
    }
    ctx.globalAlpha = 0.16 + (Math.sin(time * 0.0012 + node.phase) + 1) * 0.09
    ctx.fillStyle = primaryColor
    ctx.beginPath()
    ctx.arc(x, y, 1.4, 0, Math.PI * 2)
    ctx.fill()
  }
  ctx.globalAlpha = 1
}

function stopAnimation(): void {
  if (animationFrame === null) return
  cancelAnimationFrame(animationFrame)
  animationFrame = null
}

function startAnimation(): void {
  stopAnimation()
  if (document.hidden || reducedMotion?.matches === true) {
    draw(performance.now(), false)
    return
  }
  const tick = (time: number): void => {
    draw(time, true)
    animationFrame = requestAnimationFrame(tick)
  }
  animationFrame = requestAnimationFrame(tick)
}

function onThemeChanged(): void {
  readThemeColor()
  if (animationFrame === null) draw(performance.now(), false)
}

onMounted(() => {
  if (typeof CanvasRenderingContext2D === 'undefined') return
  const canvas = canvasRef.value
  if (canvas === null) return
  context = canvas.getContext('2d')
  if (context === null) return

  reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
  reducedMotion.addEventListener('change', startAnimation)
  document.addEventListener('visibilitychange', startAnimation)
  resizeObserver = new ResizeObserver(resizeCanvas)
  resizeObserver.observe(canvas)
  themeObserver = new MutationObserver(onThemeChanged)
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['data-ip-color-mode', 'data-ip-theme'],
  })
  resizeCanvas()
  startAnimation()
})

onBeforeUnmount(() => {
  stopAnimation()
  resizeObserver?.disconnect()
  themeObserver?.disconnect()
  reducedMotion?.removeEventListener('change', startAnimation)
  document.removeEventListener('visibilitychange', startAnimation)
})
</script>

<template>
  <div class="login-background" data-testid="login-background" aria-hidden="true">
    <div class="login-background__grid"></div>
    <div class="login-background__glow login-background__glow--primary"></div>
    <div class="login-background__glow login-background__glow--secondary"></div>
    <canvas ref="canvasRef" class="login-background__canvas"></canvas>
  </div>
</template>

<style scoped>
.login-background {
  position: absolute;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  background-color: var(--ip-color-bg-page);
  background-image: url('/brand/login-background.png');
  background-position: center;
  background-repeat: no-repeat;
  background-size: cover;
}

.login-background__grid {
  position: absolute;
  inset: 0;
  opacity: 0.38;
  background-image:
    linear-gradient(
      color-mix(in srgb, var(--ip-color-primary) 8%, transparent) 1px,
      transparent 1px
    ),
    linear-gradient(
      90deg,
      color-mix(in srgb, var(--ip-color-primary) 8%, transparent) 1px,
      transparent 1px
    );
  background-size: 48px 48px;
  mask-image: linear-gradient(to bottom, transparent 3%, #000 38%, #000 100%);
  transform: perspective(520px) rotateX(58deg) scale(1.5) translateY(20%);
  transform-origin: center bottom;
}

.login-background__glow {
  position: absolute;
  width: min(42vw, 620px);
  aspect-ratio: 1;
  border: 1px solid color-mix(in srgb, var(--ip-color-primary) 18%, transparent);
  border-radius: 50%;
  opacity: 0.48;
  box-shadow:
    0 0 80px color-mix(in srgb, var(--ip-color-primary) 12%, transparent),
    inset 0 0 60px color-mix(in srgb, var(--ip-color-primary) 8%, transparent);
  animation: login-background-breathe 8s ease-in-out infinite alternate;
}

.login-background__glow--primary {
  bottom: -20%;
  left: -8%;
}

.login-background__glow--secondary {
  top: -27%;
  right: -7%;
  width: min(34vw, 480px);
  animation-delay: -4s;
}

.login-background__canvas {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}

@keyframes login-background-breathe {
  from {
    opacity: 0.32;
    transform: scale(0.96);
  }

  to {
    opacity: 0.58;
    transform: scale(1.04);
  }
}

@media (max-width: 720px) {
  .login-background__grid {
    background-size: 38px 38px;
  }

  .login-background__glow--secondary {
    display: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .login-background__glow {
    animation: none;
  }
}
</style>
