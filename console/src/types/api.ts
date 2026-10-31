export interface User {
  id: string
  email: string
  name?: string
}

export interface Project {
  id: string
  name: string
  slug: string
  description?: string
  environment: string
  createdAtUtc: string
  retentionMaxAgeDays?: number
  retentionMaxEvents?: number
  activeApiKeyCount: number
  todayEventCount: number
  lastReceivedAtUtc?: string
}

export interface ApiKey {
  id: string
  prefix: string
  createdAtUtc: string
  revokedAtUtc?: string
  isRevoked: boolean
}

export interface LogEntry {
  id: string
  seq: number
  ts: string
  receivedAtUtc: string
  level: string
  tag: string
  message: string
  error?: string
  stackTrace?: string
  metadataJson?: string
  sessionId?: string
  userId?: string
  appVersion?: string
  platform?: string
}

export interface LogQueryResponse {
  items: LogEntry[]
  nextCursor?: string
}

export interface StatsDimension {
  name: string
  count: number
}

export interface StatsResponse {
  from: string
  to: string
  totalEvents: number
  levels: StatsDimension[]
  eventsPerDay: StatsDimension[]
  topTags: StatsDimension[]
  topUserIds: StatsDimension[]
}

export interface AuthError {
  type: string
  title: string
  status: number
  detail: string
  code: string
  traceId: string
  errors?: Array<{ field: string; message: string }>
}
