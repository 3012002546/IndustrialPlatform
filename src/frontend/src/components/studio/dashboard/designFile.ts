import type { DashboardDocument } from '@/pages/pc/studio/dashboard/model'

export const DASHBOARD_DESIGN_FORMAT = 'IndustrialPlatform.DashboardDesign'
export const MAX_DESIGN_FILE_BYTES = 2 * 1024 * 1024

export class DashboardDesignFileError extends Error {
  constructor(readonly reason: 'designFileSize' | 'designFileInvalid' | 'designFileVersion') {
    super(reason)
  }
}

export interface DashboardDesignFile {
  format: typeof DASHBOARD_DESIGN_FORMAT
  version: 1
  name: string
  exportedOn: string
  document: DashboardDocument
}

export function parseDashboardDesign(text: string): DashboardDesignFile {
  if (new TextEncoder().encode(text).length > MAX_DESIGN_FILE_BYTES)
    throw new DashboardDesignFileError('designFileSize')
  let parsed
  try {
    parsed = JSON.parse(text.replace(/^\uFEFF/, ''))
  } catch {
    throw new DashboardDesignFileError('designFileInvalid')
  }
  const object = (value: unknown) =>
    value !== null && typeof value === 'object' && !Array.isArray(value)
  if (!object(parsed)) throw new DashboardDesignFileError('designFileInvalid')
  const wrapped = 'format' in parsed || 'document' in parsed
  if (wrapped && (parsed.format !== DASHBOARD_DESIGN_FORMAT || parsed.version !== 1))
    throw new DashboardDesignFileError('designFileVersion')
  const document = wrapped ? parsed.document : parsed
  if (!object(document)) throw new DashboardDesignFileError('designFileInvalid')
  if (document.schemaVersion !== 1) throw new DashboardDesignFileError('designFileVersion')
  if (
    !object(document.canvas) ||
    !['parameters', 'instances', 'components', 'navigation'].every((key) =>
      Array.isArray(document[key]),
    ) ||
    document.components.length > 500 ||
    document.instances.length > 64 ||
    document.components.some((item: unknown) => !object(item))
  )
    throw new DashboardDesignFileError('designFileInvalid')
  return {
    format: DASHBOARD_DESIGN_FORMAT,
    version: 1,
    name: wrapped && typeof parsed.name === 'string' ? parsed.name.slice(0, 128) : '',
    exportedOn: wrapped && typeof parsed.exportedOn === 'string' ? parsed.exportedOn : '',
    document,
  }
}

export function serializeDashboardDesign(name: string, document: DashboardDocument): string {
  // Select the design contract explicitly: no resource identity, grants or runtime results.
  return JSON.stringify(
    {
      format: DASHBOARD_DESIGN_FORMAT,
      version: 1,
      name,
      exportedOn: new Date().toISOString(),
      document: {
        schemaVersion: document.schemaVersion,
        canvas: document.canvas,
        parameters: document.parameters,
        instances: document.instances,
        components: document.components,
        navigation: document.navigation,
        ...(document.theme === undefined ? {} : { theme: document.theme }),
        ...(document.samples === undefined ? {} : { samples: document.samples }),
      },
    },
    null,
    2,
  )
}

export function downloadDashboardDesign(name: string, document: DashboardDocument) {
  const content = serializeDashboardDesign(name, document)
  if (new Blob([content]).size > MAX_DESIGN_FILE_BYTES)
    throw new DashboardDesignFileError('designFileSize')
  const url = URL.createObjectURL(new Blob([content], { type: 'application/json;charset=utf-8' }))
  const link = window.document.createElement('a')
  link.href = url
  link.download = `${name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').slice(0, 80) || 'dashboard'}.dashboard.json`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
