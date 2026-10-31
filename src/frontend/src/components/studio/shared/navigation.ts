import {
  Coin,
  Collection,
  Connection,
  DataAnalysis,
  Document,
  EditPen,
  Files,
  Grid,
  VideoPlay,
} from '@element-plus/icons-vue'

// Shared by the platform's published-menu adapter and the independent Studio shell.
export const studioNavigationIcons = [
  { route: 'studio-sources', path: '/pc/studio/sources', key: 'connection', icon: Connection },
  { route: 'studio-datasets', path: '/pc/studio/datasets', key: 'coin', icon: Coin },
  { route: 'studio-lowcode', path: '/pc/studio/low-code', key: 'editpen', icon: EditPen },
  {
    route: 'studio-ledger-models',
    path: '/pc/studio/ledger-models',
    key: 'collection',
    icon: Collection,
  },
  {
    route: 'studio-dashboards',
    path: '/pc/studio/dashboards',
    key: 'dataanalysis',
    icon: DataAnalysis,
  },
  { route: 'studio-playlists', path: '/pc/studio/playlists', key: 'videoplay', icon: VideoPlay },
  { route: 'studio-reports', path: '/pc/studio/reports', key: 'document', icon: Document },
  { route: 'studio-report-bundles', path: '/pc/studio/report-bundles', key: 'files', icon: Files },
  { route: 'studio-workbooks', path: '/pc/studio/workbooks', key: 'grid', icon: Grid },
]
