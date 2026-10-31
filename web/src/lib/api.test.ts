import { describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest, getFieldErrors } from './api'

function jsonResponse(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get: (name: string) =>
        name.toLowerCase() === 'content-type'
          ? 'application/json; charset=utf-8'
          : null,
    },
    json: async () => body,
  } as unknown as Response
}

describe('apiRequest', () => {
  it('sends the bearer token with credentials and returns the payload', async () => {
    const fetchMock = vi.fn<typeof fetch>()
    fetchMock.mockResolvedValue(jsonResponse(200, { ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      apiRequest('/auth/me', { accessToken: 'abc' }),
    ).resolves.toEqual({ ok: true })

    const call = fetchMock.mock.calls[0]
    expect(call).toBeDefined()

    const [url, init] = call as [RequestInfo | URL, RequestInit]
    expect(url).toBe('/api/v1/auth/me')
    expect(init.credentials).toBe('include')
    expect((init.headers as Record<string, string>).Authorization).toBe(
      'Bearer abc',
    )
  })

  it('turns ProblemDetails into an ApiError with field errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        jsonResponse(400, {
          title: 'One or more validation errors occurred.',
          code: 'validation_failed',
          errors: { Email: ['Email is required.'] },
        }),
      ),
    )

    let caught: unknown

    try {
      await apiRequest('/auth/register', { method: 'POST', body: {} })
    } catch (error) {
      caught = error
    }

    expect(caught).toBeInstanceOf(ApiError)
    expect((caught as ApiError).code).toBe('validation_failed')
    expect(getFieldErrors(caught)).toEqual({
      email: ['Email is required.'],
    })
  })

  it('resolves empty for 204 responses', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue({
        ok: true,
        status: 204,
        headers: { get: () => null },
        json: async () => null,
      } as unknown as Response),
    )

    await expect(
      apiRequest('/auth/logout', { method: 'POST' }),
    ).resolves.toBeUndefined()
  })

  it('wraps network failures in a status-0 ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockRejectedValue(new TypeError('fetch failed')),
    )

    await expect(apiRequest('/health')).rejects.toMatchObject({
      status: 0,
      code: 'network_error',
    })
  })
})
