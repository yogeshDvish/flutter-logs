import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { ApiError, apiRequest, type ApiRequestOptions } from '../lib/api'
import type { AuthResponse, User } from '../lib/types'
import {
  AuthContext,
  type AuthContextValue,
  type AuthStatus,
  type RegisterInput,
} from './AuthContext'

/**
 * Holds the access token in memory only. The refresh token lives in an
 * httpOnly cookie that JavaScript cannot read, so a page reload re-establishes
 * the session through `POST /auth/refresh`.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const accessTokenRef = useRef<string | null>(null)
  const refreshPromiseRef = useRef<Promise<AuthResponse> | null>(null)
  const bootstrapStartedRef = useRef(false)

  const [status, setStatus] = useState<AuthStatus>('booting')
  const [user, setUser] = useState<User | null>(null)

  const applySession = useCallback((session: AuthResponse) => {
    accessTokenRef.current = session.accessToken
    setUser(session.user)
    setStatus('authenticated')
  }, [])

  const clearSession = useCallback(() => {
    accessTokenRef.current = null
    setUser(null)
    setStatus('anonymous')
  }, [])

  const refreshSession = useCallback((): Promise<AuthResponse> => {
    // Collapse concurrent refreshes: a second rotation with the same cookie
    // would look like token reuse and revoke the family.
    if (refreshPromiseRef.current) {
      return refreshPromiseRef.current
    }

    const promise = apiRequest<AuthResponse>('/auth/refresh', { method: 'POST' })
      .then((session) => {
        applySession(session)
        return session
      })
      .catch((error: unknown) => {
        clearSession()
        throw error
      })
      .finally(() => {
        refreshPromiseRef.current = null
      })

    refreshPromiseRef.current = promise
    return promise
  }, [applySession, clearSession])

  useEffect(() => {
    // StrictMode runs effects twice in development; the session refresh rotates
    // a single-use cookie, so only the first run may issue the request.
    if (bootstrapStartedRef.current) {
      return
    }

    bootstrapStartedRef.current = true

    apiRequest<AuthResponse>('/auth/refresh', { method: 'POST' })
      .then(applySession)
      .catch(clearSession)
  }, [applySession, clearSession])

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await apiRequest<AuthResponse>('/auth/login', {
        method: 'POST',
        body: { email, password },
      })

      applySession(session)
    },
    [applySession],
  )

  const register = useCallback(
    (input: RegisterInput) =>
      apiRequest<User>('/auth/register', { method: 'POST', body: input }),
    [],
  )

  const logout = useCallback(async () => {
    try {
      await apiRequest<void>('/auth/logout', { method: 'POST' })
    } finally {
      clearSession()
    }
  }, [clearSession])

  const authedRequest = useCallback(
    async <T,>(
      path: string,
      options: Omit<ApiRequestOptions, 'accessToken'> = {},
    ): Promise<T> => {
      try {
        return await apiRequest<T>(path, {
          ...options,
          accessToken: accessTokenRef.current,
        })
      } catch (error) {
        if (!(error instanceof ApiError) || error.status !== 401) {
          throw error
        }
      }

      await refreshSession()

      return apiRequest<T>(path, {
        ...options,
        accessToken: accessTokenRef.current,
      })
    },
    [refreshSession],
  )

  const resendVerification = useCallback(
    () => authedRequest<void>('/auth/resend-verification', { method: 'POST' }),
    [authedRequest],
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      login,
      register,
      logout,
      resendVerification,
      authedRequest,
    }),
    [
      status,
      user,
      login,
      register,
      logout,
      resendVerification,
      authedRequest,
    ],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
