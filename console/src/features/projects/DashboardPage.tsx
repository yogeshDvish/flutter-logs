import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { get } from '../../lib/apiClient'
import { Project } from '../../types/api'
import { Link } from 'react-router-dom'

export function DashboardPage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    loadProjects()
  }, [])

  async function loadProjects() {
    try {
      const response = await get('/projects')
      if (response.ok) {
        const data = await response.json()
        setProjects((data as any).items || data)
      } else {
        setError('Failed to load projects')
      }
    } catch {
      setError('Network error. Is the API running?')
    } finally {
      setLoading(false)
    }
  }

  function handleLogout() {
    logout()
  }

  function handleCreate() {
    navigate('/create')
  }

  if (loading) {
    return <div className="loading-block">Loading...</div>
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1 className="page-title">Dashboard</h1>
          <p className="page-subtitle">Welcome back, {user?.name || user?.email}</p>
        </div>
        <div className="header-actions">
          <button
            onClick={handleCreate}
            className="btn btn-primary"
          >
            Create Project
          </button>
          <button
            onClick={handleLogout}
            className="btn btn-danger-soft"
          >
            Sign out
          </button>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          {error}
        </div>
      )}

      <div className="project-grid">
        {projects.length === 0 ? (
          <div className="empty-state">
            <p>No projects yet. Create one to get started.</p>
          </div>
        ) : (
          projects.map((project) => (
            <Link
              key={project.id}
              to={`/projects/${project.id}/logs`}
              className="project-card"
            >
              <div className="project-card-top">
                <h3 className="project-name">{project.name}</h3>
                <span className="badge badge-accent">
                  {project.environment}
                </span>
              </div>
              <p className="project-slug">{project.slug}</p>
              {project.description && (
                <p className="project-desc">{project.description}</p>
              )}
              <div className="metric-grid">
                <div className="metric">
                  <div className="metric-label">Today</div>
                  <div className="metric-value">{project.todayEventCount}</div>
                </div>
                <div className="metric">
                  <div className="metric-label">Keys</div>
                  <div className="metric-value">{project.activeApiKeyCount}</div>
                </div>
                <div className="metric">
                  <div className="metric-label">Last</div>
                  <div className="metric-value-sm">
                    {project.lastReceivedAtUtc ? new Date(project.lastReceivedAtUtc).toLocaleDateString() : '—'}
                  </div>
                </div>
              </div>
            </Link>
          ))
        )}
      </div>
    </div>
  )
}
