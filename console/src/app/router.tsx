import { Navigate, Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from '../components/guards/ProtectedRoute'
import { GuestRoute } from '../components/guards/GuestRoute'
import { LoginPage } from '../features/auth/LoginPage'
import { RegisterPage } from '../features/auth/RegisterPage'
import { DashboardPage } from '../features/projects/DashboardPage'
import { CreateProjectWizard } from '../features/projects/CreateProjectWizard'
import { LogExplorerPage } from '../features/logs/LogExplorerPage'
import { StatsPage } from '../features/stats/StatsPage'
import { SettingsPage } from '../features/settings/SettingsPage'

export function Router() {
  return (
    <Routes>
      <Route path="/login" element={<GuestRoute><LoginPage /></GuestRoute>} />
      <Route path="/register" element={<GuestRoute><RegisterPage /></GuestRoute>} />
      <Route
        path="/create"
        element={<ProtectedRoute><CreateProjectWizard /></ProtectedRoute>}
      />
      <Route
        path="/projects/:projectId/*"
        element={
          <ProtectedRoute>
            <Routes>
              <Route path="logs" element={<LogExplorerPage />} />
              <Route path="stats" element={<StatsPage />} />
              <Route path="settings" element={<SettingsPage />} />
              <Route index element={<LogExplorerPage />} />
            </Routes>
          </ProtectedRoute>
        }
      />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <DashboardPage />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
