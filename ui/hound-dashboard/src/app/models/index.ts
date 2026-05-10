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

export interface WatchtowerEvent {
  id: string;
  containerName: string;
  imageName: string;
  oldImageId: string;
  newImageId: string;
  action: string;
  timestamp: string;
  rawPayload: string;
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
