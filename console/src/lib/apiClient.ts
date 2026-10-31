let accessToken: string | null = null

export function setAccessToken(token: string | null) {
  accessToken = token
}

async function request(
  input: string,
  init: RequestInit = {},
): Promise<Response> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init.headers as Record<string, string> || {}),
  }

  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`
  }

  const response = await fetch(`/api/v1${input}`, {
    ...init,
    headers,
  })

  if (response.status === 401) {
    // Token expired or invalid - clear session
    sessionStorage.removeItem('accessToken')
    sessionStorage.removeItem('user')
    accessToken = null
    window.location.href = '/login'
  }

  return response
}

export async function get(input: string, init?: RequestInit) {
  return request(input, { ...init, method: 'GET' })
}

export async function post(input: string, body: unknown, init?: RequestInit) {
  return request(input, {
    ...init,
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export async function patch(input: string, body: unknown, init?: RequestInit) {
  return request(input, {
    ...init,
    method: 'PATCH',
    body: JSON.stringify(body),
  })
}

export async function del(input: string, init?: RequestInit) {
  return request(input, { ...init, method: 'DELETE' })
}
