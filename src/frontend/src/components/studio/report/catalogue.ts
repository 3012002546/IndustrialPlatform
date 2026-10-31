import {
  Checked,
  Document,
  EditPen,
  Files,
  FullScreen,
  Grid,
  Minus,
  Picture,
  Tickets,
} from '@element-plus/icons-vue'

// Only list controls and bands supported by the report contract and renderer.
export const reportControls = [
  { type: 'Text', label: 'labelControl', icon: EditPen },
  { type: 'Field', label: 'fieldControl', icon: Tickets },
  { type: 'CheckBox', label: 'checkControl', icon: Checked },
  { type: 'Html', label: 'richControl', icon: Document },
  { type: 'Image', label: 'imageControl', icon: Picture },
  { type: 'Table', label: 'tableControl', icon: Grid },
  { type: 'Line', label: 'lineControl', icon: Minus },
  { type: 'Box', label: 'boxControl', icon: FullScreen },
  { type: 'PageNumber', label: 'pageControl', icon: Files },
] as const
export const reportBandKinds = [
  { kind: 'PageHeader', label: 'pageHeader' },
  { kind: 'ReportHeader', label: 'reportHeader' },
  { kind: 'GroupHeader', label: 'groupHeader' },
  { kind: 'Detail', label: 'detailBand' },
  { kind: 'GroupFooter', label: 'groupFooter' },
  { kind: 'ReportFooter', label: 'reportFooter' },
  { kind: 'PageFooter', label: 'pageFooter' },
] as const
