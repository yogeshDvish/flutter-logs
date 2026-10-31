import type { ApiRequestOptions } from './api'
import type {
  IssuedProjectKey,
  ProjectCreatedResponse,
  ProjectKey,
  ProjectStats,
  ProjectSummary,
} from './types'

export type AuthedRequest = <T>(
  path: string,
  options?: Omit<ApiRequestOptions, 'accessToken'>,
) => Promise<T>

export interface CreateProjectInput {
  name: string
  description?: string | null
}

export interface UpdateProjectInput {
  name?: string
  description?: string | null
  ingestEnabled?: boolean
}

export interface IssueKeyInput {
  label?: string | null
  graceHours?: number
}

export function listProjects(
  request: AuthedRequest,
  includeArchived = false,
  signal?: AbortSignal,
): Promise<ProjectSummary[]> {
  const query = includeArchived ? '?includeArchived=true' : ''
  return request<ProjectSummary[]>(`/projects${query}`, { signal })
}

export function getProject(
  request: AuthedRequest,
  projectId: string,
): Promise<ProjectSummary> {
  return request<ProjectSummary>(`/projects/${projectId}`)
}

export function createProject(
  request: AuthedRequest,
  input: CreateProjectInput,
): Promise<ProjectCreatedResponse> {
  return request<ProjectCreatedResponse>('/projects', {
    method: 'POST',
    body: input,
  })
}

export function updateProject(
  request: AuthedRequest,
  projectId: string,
  input: UpdateProjectInput,
): Promise<ProjectSummary> {
  return request<ProjectSummary>(`/projects/${projectId}`, {
    method: 'PATCH',
    body: input,
  })
}

export function deleteProject(
  request: AuthedRequest,
  projectId: string,
  purge = false,
): Promise<void> {
  return request<void>(`/projects/${projectId}${purge ? '?purge=true' : ''}`, {
    method: 'DELETE',
  })
}

export function getProjectStats(
  request: AuthedRequest,
  projectId: string,
): Promise<ProjectStats & { projectId: string }> {
  return request<ProjectStats & { projectId: string }>(
    `/projects/${projectId}/stats`,
  )
}

export function listKeys(
  request: AuthedRequest,
  projectId: string,
): Promise<ProjectKey[]> {
  return request<ProjectKey[]>(`/projects/${projectId}/keys`)
}

export function issueKey(
  request: AuthedRequest,
  projectId: string,
  input: IssueKeyInput,
): Promise<IssuedProjectKey> {
  return request<IssuedProjectKey>(`/projects/${projectId}/keys`, {
    method: 'POST',
    body: input,
  })
}

export function revokeKey(
  request: AuthedRequest,
  projectId: string,
  keyId: string,
): Promise<void> {
  return request<void>(`/projects/${projectId}/keys/${keyId}`, {
    method: 'DELETE',
  })
}
