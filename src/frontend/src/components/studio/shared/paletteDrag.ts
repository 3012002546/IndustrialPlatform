const paletteMime = 'application/x-platform-studio-palette'

export function startPaletteDrag(event: DragEvent, kind: 'report' | 'dashboard', key: string) {
  if (!event.dataTransfer) return
  event.dataTransfer.effectAllowed = 'copy'
  event.dataTransfer.setData(paletteMime, JSON.stringify({ kind, key }))
}

export function paletteKey(event: DragEvent, kind: 'report' | 'dashboard'): string | undefined {
  try {
    const value = JSON.parse(event.dataTransfer?.getData(paletteMime) || 'null')
    return value?.kind === kind && typeof value.key === 'string' ? value.key : undefined
  } catch {
    return undefined
  }
}

export function allowPaletteDrop(event: DragEvent, extraType?: string) {
  const types = event.dataTransfer?.types ?? []
  if (types.includes(paletteMime) || (extraType && types.includes(extraType))) {
    event.preventDefault()
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy'
  }
}

// The caller supplies pixels per document unit, including its current canvas zoom.
export function canvasDropPoint(event: DragEvent, element: HTMLElement, scale: number) {
  const rect = element.getBoundingClientRect()
  return {
    x: Math.max(0, (event.clientX - rect.left) / scale),
    y: Math.max(0, (event.clientY - rect.top) / scale),
  }
}
