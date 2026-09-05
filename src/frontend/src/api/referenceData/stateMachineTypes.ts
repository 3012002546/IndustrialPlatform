import type { PublicationStatus, ReferenceScope, VersionRequest } from './types'

export type StateOutcome = 'None' | 'Success' | 'Failure' | 'Skipped'

export interface StateNodeWrite {
  nId: string
  name: string
  description: string | null
  isInitial: boolean
  isTerminal: boolean
  outcome: StateOutcome
  color: string | null
  sort: number
}

export interface StateNode extends StateNodeWrite {
  id: string
}

export interface StateTransitionWrite {
  fromStatusNId: string
  actionNId: string
  actionName: string
  toStatusNId: string
  description: string | null
}

export interface StateTransition extends StateTransitionWrite {
  id: string
}

export interface StateMachineSummary {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  nodeCount: number
  transitionCount: number
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  publishedOn: string | null
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}

export interface StateMachineDetail extends Omit<
  StateMachineSummary,
  'nodeCount' | 'transitionCount'
> {
  nodes: StateNode[]
  transitions: StateTransition[]
}

export interface StateMachinePublicationIssue {
  code: string
  field: string
}

export interface StateMachinePublicationCheck {
  previousRevision: number | null
  addedNodeNIds: string[]
  removedNodeNIds: string[]
  changedNodeNIds: string[]
  addedTransitionKeys: string[]
  removedTransitionKeys: string[]
  changedTransitionKeys: string[]
  errors: StateMachinePublicationIssue[]
}

export interface CreateStateMachineRequest {
  scopeType: ReferenceScope
  nId: string
  name: string
  description: string | null
  nodes: StateNodeWrite[]
  transitions: StateTransitionWrite[]
}

export interface UpdateStateMachineRequest extends VersionRequest {
  name: string
  description: string | null
  nodes: StateNodeWrite[]
  transitions: StateTransitionWrite[]
}

export interface AvailableStateMachine {
  nId: string
  name: string
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  publishedOn: string
  nodeCount: number
  transitionCount: number
}

export type RuntimeStateNode = StateNodeWrite
export type RuntimeStateTransition = StateTransitionWrite

export interface RuntimeStateMachine {
  nId: string
  name: string
  description: string | null
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  publishedOn: string | null
  nodes: RuntimeStateNode[]
  transitions: RuntimeStateTransition[]
}

export interface TransitionEvaluationRequest {
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  fromStatusNId: string
  actionNId: string
}

export interface TransitionEvaluation {
  stateMachineNId: string
  stateMachineRevision: number
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  fromStatusNId: string
  actionNId: string
  allowedByDefinition: boolean
  toStatusNId: string | null
  reasonCode: string | null
}
