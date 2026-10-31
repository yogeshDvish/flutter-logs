import { createContext } from 'react'
import type { ApiRequestOptions } from '../lib/api'
import type { User } from '../lib/types'

export type AuthStatus = 'booting' | 'authenticated' | 'anonymous'

export interface RegisterInput {
  email: string
  password: string
  displayName?: string
}

export interface AuthContextValue {
  status: AuthStatus
  user: User | null
  login: (email: string, password: string) => Promise<void>
  register: (input: RegisterInput) => Promise<User>
  logout: () => Promise<void>
  resendVerification: () => Promise<void>
  /** Adds the in-memory access token and retries once after a refresh on 401. */
  authedRequest: <T>(
    path: string,
    options?: Omit<ApiRequestOptions, 'accessToken'>,
  ) => Promise<T>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
