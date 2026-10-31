import type { HealthResponse, ProblemDetailsPayload } from './types'

const API_BASE = '/api/v1'

/**
 * Error raised for any failed API call. Expected failures (validation, auth,
 * conflicts) arrive as ProblemDetails and are surfaced with their stable `code`
 * and per-field `errors`; network failures use status 0.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Record<string, string[]>

  constructor(
    status: number,
    code: string,
    message: string,
    fieldErrors: Record<string, string[]> = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

export interface ApiRequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  accessToken?: string | null
  signal?: AbortSignal
}

/**
 * Single entry point for HTTP calls. Always sends the refresh cookie
 * (`credentials: 'include'`) so the auth endpoints can rotate the session.
 */
export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  if (options.accessToken) {
    headers.Authorization = `Bearer ${options.accessToken}`
  }

  let response: Response

  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: options.method ?? 'GET',
      headers,
      credentials: 'include',
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }

    throw new ApiError(
      0,
      'network_error',
      'The server could not be reached. Check that the API is running.',
    )
  }

  if (response.status === 204 || response.status === 202) {
    return undefined as T
  }

  const payload = await readJson(response)

  if (!response.ok) {
    const problem = (payload ?? {}) as ProblemDetailsPayload

    throw new ApiError(
      response.status,
      problem.code ?? 'request_failed',
      problem.title ?? `Request failed with status ${response.status}.`,
      problem.errors ?? {},
    )
  }

  return payload as T
}

export function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return apiRequest<HealthResponse>('/health', { signal })
}

/** Lower-cases server field names (`Email` → `email`) for form lookups. */
export function getFieldErrors(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError)) {
    return {}
  }

  const result: Record<string, string[]> = {}

  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    result[field.toLowerCase()] = messages
  }

  return result
}

export function getErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message
  }

  if (error instanceof Error) {
    return error.message
  }

  return 'Something went wrong. Please try again.'
}

async function readJson(response: Response): Promise<unknown> {
  const contentType = response.headers.get('content-type') ?? ''

  if (!contentType.includes('json')) {
    return null
  }

  try {
    return (await response.json()) as unknown
  } catch {
    return null
  }
}
