export interface TodoItem {
  id: number
  title: string
  isCompleted: boolean
  status: string
  dueDate: string | null
  createdAt: string
  parentId: number | null
  priority: string
  related: string
  detailRelated: string
  sortOrder: number
}

export interface TaskSession {
  id: number
  todoId: number
  startedAt: string
  endedAt: string | null
  activeCountAtStart: number
  comment: string | null
}

export interface CommentAttachment {
  id: number
  commentId: number
  path: string
  type: 'image' | 'video' | 'file' | null
  preview: string | null
  sortOrder: number
}

export interface Comment {
  id: number
  todoId: number
  text: string
  attachments: CommentAttachment[]
  createdAt: string
}

export interface TaskLog {
  id: number
  todoId: number
  timestamp: string
  eventType: string
  detail: string | null
}

// ── Planner ("GPS pro tasky") ──────────────────────────────────────────────

export interface BlockedByInfo {
  kind: 'task' | 'person' | 'start'
  ref: string
  reason: string
}

export interface PlanNode {
  todoId: number
  slug: string
  title: string
  status: string
  predictedStart: string
  predictedFinish: string
  deadline: string | null
  slackMinutes: number
  state: 'now' | 'next' | 'blocked' | 'at_risk' | 'future' | 'done'
  blockedBy: BlockedByInfo | null
  downstreamImpactCount: number
  softWindowMissed: boolean
  sharesWindowWith: string[]
  isOccurrence: boolean
  occurrenceDate: string | null
}

export interface PlanAlert {
  type: 'dependency_cycle' | 'periodic_stuck' | 'bottleneck'
  message: string
  slug: string | null
  todoId: number | null
  downstreamImpact: number
}

export interface PlanResult {
  computedAt: string
  horizon: string
  now: PlanNode | null
  next: PlanNode[]
  blocked: PlanNode[]
  atRisk: PlanNode[]
  alerts: PlanAlert[]
  timeline: PlanNode[]
}

export interface FilterState {
  nameFilter: string
  listFilter: Set<number>
  statusFilter: Set<string>
  prioritaExcluded: Set<string>
  relatedFilter: string
  detailRelatedFilter: string
  dateFrom: string
  dateTo: string
  // Activity-date filter: show only todos with activity in [activityFrom, activityTo].
  // activityTypes is a subset of {'created','modified','commented'} (empty/all = any kind).
  activityFrom: string
  activityTo: string
  activityTypes: Set<string>
}
