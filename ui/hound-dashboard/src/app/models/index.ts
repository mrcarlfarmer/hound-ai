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
