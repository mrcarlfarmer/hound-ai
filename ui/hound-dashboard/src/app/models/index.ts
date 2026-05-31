export interface Pack {
  id: string;
  name: string;
  status: 'Idle' | 'Running' | 'Error' | 'Stopped';
  houndCount: number;
  lastActivity?: string;
}

export interface HoundInfo {
  id: string;
  name: string;
  packId: string;
  status: 'Idle' | 'Processing' | 'Error' | 'Disabled';
  lastActivity?: string;
}

export interface ActivityLog {
  id: string;
  packId: string;
  houndId: string;
  houndName: string;
  message: string;
  severity: 'Info' | 'Warning' | 'Error' | 'Success';
  timestamp: string;
  metadata?: Record<string, unknown>;
}

export interface ActivityFilter {
  pack?: string;
  hound?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TunerExperiment {
  id: string;
  houndName: string;
  timestamp: string;
  configBefore: string;
  configAfter: string;
  baselineScore: number;
  candidateScore: number;
  delta: number;
  status: 'improved' | 'equal' | 'worse' | 'crash' | 'pending-review' | 'applied' | 'rejected';
  rationale: string;
}

export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown';

export interface ServiceHealth {
  name: string;
  status: HealthStatus;
  detail?: string;
}

export interface HealthReport {
  status: HealthStatus;
  timestamp: string;
  services: ServiceHealth[];
}

export type FillStatus = 'Pending' | 'PartiallyFilled' | 'Filled' | 'Canceled' | 'Expired' | 'Rejected';

export interface TradeDocument {
  id: string;
  symbol: string;
  action: string;
  requestedQuantity: number;
  orderId: string;
  fillStatus: FillStatus;
  filledQuantity: number;
  averageFillPrice?: number;
  executionTime?: string;
  createdAt: string;
  updatedAt: string;
  riskAssessmentSummary: string;
  packId: string;
  houndId: string;
}

export interface OrderUpdate {
  tradeDocumentId: string;
  symbol: string;
  fillStatus: string;
  filledQuantity: number;
  averageFillPrice?: number;
  executionTime?: string;
}

// ── Graph Run models ─────────────────────────────────────────────────────────

export type GraphPhase = 'Entry' | 'Monitor';
export type NodeStatus = 'Pending' | 'Active' | 'Completed' | 'Failed' | 'Skipped';
export type ApprovalStatus = 'NotRequested' | 'Pending' | 'Approved' | 'Rejected';

export interface NodeSnapshot {
  nodeId: string;
  status: NodeStatus;
  startedAt?: string;
  completedAt?: string;
  outputJson?: string;
  errorMessage?: string;
  reasoningText?: string;
}

export interface GraphRun {
  id: string;
  runId: string;
  symbol: string;
  startedAt: string;
  completedAt?: string;
  phase: GraphPhase;
  currentNode?: string;
  isComplete: boolean;
  errorMessage?: string;
  refinementCount: number;
  monitorCycleCount: number;
  approvalStatus?: ApprovalStatus;
  approvalDecidedBy?: string;
  approvalDecidedAt?: string;
  approvalNotes?: string;
  nodes: NodeSnapshot[];
  refinements?: RefinementSnapshot[];
}

export interface RefinementSnapshot {
  attempt: number;
  symbol?: string;
  action?: string;
  quantity: number;
  riskReasoning: string;
  occurredAt: string;
}

export type RunRequestStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';

export interface RunRequest {
  id: string;
  symbol: string;
  status: RunRequestStatus;
  requestedAt: string;
  startedAt?: string;
  completedAt?: string;
  runId?: string;
  errorMessage?: string;
}

export interface NodeStreamChunk {
  packId: string;
  runId: string;
  nodeId: string;
  text: string;
  timestamp: string;
}

// ── Portfolio models ─────────────────────────────────────────────────────────

export interface AccountSummary {
  equity: number;
  cash: number;
  buyingPower: number;
  portfolioValue: number;
  dailyChangePercent: number;
  dailyChangeAmount: number;
  lastEquity: number;
  currency: string;
}

export interface PositionInfo {
  symbol: string;
  quantity: number;
  marketValue: number;
  currentPrice: number;
  averageEntryPrice: number;
  unrealizedPl: number;
  unrealizedPlPercent: number;
  changeToday: number;
  side: string;
}

export interface AlpacaSyncResult {
  checked: number;
  updated: number;
  imported: number;
  errors: number;
  startedAt: string;
  completedAt: string;
  changedTradeIds: string[];
}
