export interface User {
  id: string
  email: string
  displayName: string | null
  emailVerified: boolean
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  tokenType: string
  expiresIn: number
  user: User
}

export interface HealthResponse {
  status: string
  db: string
  version: string
}

export interface ProjectStatsBucket {
  total: number
  errors: number
}

export interface ProjectStats {
  last24Hours: ProjectStatsBucket
  last7Days: ProjectStatsBucket
  lastLogAt: string | null
}

export interface ProjectSummary {
  id: string
  name: string
  description: string | null
  ingestEnabled: boolean
  createdAt: string
  updatedAt: string
  archivedAt: string | null
  activeKeyCount: number
  stats: ProjectStats
}

/** The only payload that contains the plaintext ingestion key. */
export interface IssuedProjectKey {
  id: string
  prefix: string
  label: string | null
  createdAt: string
  graceUntil: string | null
  plaintext: string
}

export interface ProjectCreatedResponse {
  project: ProjectSummary
  key: IssuedProjectKey
}

export type ProjectKeyStatus = 'active' | 'grace' | 'expired' | 'revoked'

export interface ProjectKey {
  id: string
  prefix: string
  label: string | null
  status: ProjectKeyStatus
  createdAt: string
  lastUsedAt: string | null
  graceUntil: string | null
  revokedAt: string | null
}

/**
 * RFC 9457 problem payload as produced by the API. `code` and `errors` are
 * extensions the dashboard uses for precise messaging.
 */
export interface ProblemDetailsPayload {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  code?: string | null
  errors?: Record<string, string[]> | null
}
