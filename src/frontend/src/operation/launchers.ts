import {
  Collection,
  DataAnalysis,
  Document,
  Goods,
  Operation,
  ScaleToOriginal,
  Setting,
  Tickets,
  Van,
} from '@element-plus/icons-vue'

import type { OperationLauncher } from './types'

/**
 * 生产模式只展示已确认的入口注册表。未来业务项故意不携带 route、permission 或 feature，
 * 这样占位卡片不能被误认为已实现业务，也不会产生请求。
 */
export const operationLaunchers: readonly OperationLauncher[] = [
  { id: 'task-execution', titleKey: 'operation.launchers.taskExecution', fallbackTitle: '任务执行', icon: Operation, state: 'coming-soon' },
  { id: 'work-order', titleKey: 'operation.launchers.workOrder', fallbackTitle: '工单作业', icon: Tickets, state: 'coming-soon' },
  { id: 'material-feeding', titleKey: 'operation.launchers.materialFeeding', fallbackTitle: '投料作业', icon: Goods, state: 'coming-soon' },
  { id: 'weighing', titleKey: 'operation.launchers.weighing', fallbackTitle: '称量作业', icon: ScaleToOriginal, state: 'coming-soon' },
  { id: 'feeding-statistics', titleKey: 'operation.launchers.feedingStatistics', fallbackTitle: '投料统计', icon: DataAnalysis, state: 'coming-soon' },
  { id: 'material-concentration', titleKey: 'operation.launchers.materialConcentration', fallbackTitle: '物料集中', icon: Collection, state: 'coming-soon' },
  { id: 'material-receipt', titleKey: 'operation.launchers.materialReceipt', fallbackTitle: '物料接收', icon: Van, state: 'coming-soon' },
  { id: 'recipe-view', titleKey: 'operation.launchers.recipeView', fallbackTitle: '配方查看', icon: Document, state: 'coming-soon' },
  { id: 'interface-settings', titleKey: 'operation.launchers.interfaceSettings', fallbackTitle: '界面设置', icon: Setting, state: 'available' },
]
