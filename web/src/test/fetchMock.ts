import { vi } from 'vitest'

export interface MockRequest {
  url: string
  method: string
  body: unknown
}

export interface MockRoute {
  method?: string
  path: string
  status?: number
  body?: unknown | ((request: MockRequest) => unknown)
  /** Runs before the response is built; useful for stateful test fixtures. */
  onRequest?: (request: MockRequest) => void
}

/**
 * Installs a `fetch` stub that matches requests by method and path substring.
 * The longest matching path wins, so `/projects/1/keys` is preferred over
 * `/projects/1`. Responses are plain objects: jsdom does not need a full
 * `Response` instance.
 */
export function installFetchMock(routes: MockRoute[]) {
  const fetchMock = vi.fn(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url

      const method = (init?.method ?? 'GET').toUpperCase()
      const request: MockRequest = {
        url,
        method,
        body: parseBody(init?.body),
      }

      const route = [...routes]
        .sort((left, right) => right.path.length - left.path.length)
        .find(
          (candidate) =>
            (candidate.method ?? 'GET').toUpperCase() === method &&
            url.includes(candidate.path),
        )

      if (!route) {
        throw new Error(`Unhandled request: ${method} ${url}`)
      }

      route.onRequest?.(request)

      const body =
        typeof route.body === 'function' ? route.body(request) : route.body

      return jsonResponse(route.status ?? 200, body)
    },
  )

  vi.stubGlobal('fetch', fetchMock)

  return fetchMock
}

function parseBody(body: BodyInit | null | undefined): unknown {
  if (typeof body !== 'string') {
    return body
  }

  try {
    return JSON.parse(body) as unknown
  } catch {
    return body
  }
}

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
