import { createContext, useContext, useEffect, useState, ReactNode } from 'react'
export interface User {
  id: string
  email: string
  name?: string
}

interface AuthContextType {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
  accessToken: string | null
  login: (accessToken: string, user: User) => void
  logout: () => void
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [accessToken, setAccessToken] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    // Restore session from memory (refresh token is in HttpOnly cookie)
    const token = sessionStorage.getItem('accessToken')
    const userJson = sessionStorage.getItem('user')
    if (token && userJson) {
      setAccessToken(token)
      setUser(JSON.parse(userJson))
    }
    setIsLoading(false)
  }, [])

  const login = (token: string, userData: User) => {
    sessionStorage.setItem('accessToken', token)
    sessionStorage.setItem('user', JSON.stringify(userData))
    setAccessToken(token)
    setUser(userData)
  }

  const logout = () => {
    sessionStorage.removeItem('accessToken')
    sessionStorage.removeItem('user')
    setAccessToken(null)
    setUser(null)
  }

  return (
    <AuthContext.Provider value={{ user, isLoading, isAuthenticated: !!user, accessToken, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
