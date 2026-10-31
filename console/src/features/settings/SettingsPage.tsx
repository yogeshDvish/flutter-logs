import { useState, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { get, post, del } from '../../lib/apiClient'
import { ApiKey } from '../../types/api'

interface ApiKeyWithPlaintext extends ApiKey {
  plaintext?: string
}

export function SettingsPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const [apiKeys, setApiKeys] = useState<ApiKeyWithPlaintext[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [creating, setCreating] = useState(false)
  const [revokingId, setRevokingId] = useState<string | null>(null)

  useEffect(() => {
    if (!projectId) return
    loadKeys()
  }, [projectId])

  async function loadKeys() {
    if (!projectId) return
    try {
      setLoading(true)
      const response = await get(`/projects/${projectId}/api-keys`)
      if (response.ok) {
        const data = await response.json()
        setApiKeys(Array.isArray(data) ? data : [])
      }
    } catch {
      // Error handled by apiClient
    } finally {
      setLoading(false)
    }
  }

  async function handleCreateKey() {
    if (!projectId) return
    setCreating(true)
    try {
      const response = await post(`/projects/${projectId}/api-keys`, null)
      if (response.ok) {
        const data = await response.json()
        // The plaintext is returned once
        const plaintext = (data as any).plaintext
        setApiKeys((prev) => [
          { ...data, plaintext, isRevoked: false } as ApiKeyWithPlaintext,
          ...prev,
        ])
      } else {
        const errorBody = await response.json()
        setError((errorBody as any)?.detail || 'Failed to create key')
      }
    } catch {
      setError('Network error')
    } finally {
      setCreating(false)
    }
  }

  async function handleRevoke(keyId: string) {
    if (!projectId) return
    setRevokingId(keyId)
    try {
      const response = await del(`/projects/${projectId}/api-keys/${keyId}`)
      if (response.ok) {
        setApiKeys((prev) => prev.map((k) =>
          k.id === keyId ? { ...k, revokedAtUtc: new Date().toISOString(), isRevoked: true } : k
        ) as ApiKeyWithPlaintext[])
      }
    } catch {
      // Error handled by apiClient
    } finally {
      setRevokingId(null)
    }
  }

  if (loading) return <div className="loading-block">Loading...</div>

  return (
    <div className="page">
      <h1 className="page-title mb-6">Settings</h1>

      {error && (
        <div className="alert alert-error">
          {error}
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <div>
            <h2 className="card-title">API Keys</h2>
            <p className="card-subtitle">Manage ingestion keys for this project</p>
          </div>
          <button
            onClick={handleCreateKey}
            disabled={creating}
            className="btn btn-primary"
          >
            {creating ? 'Creating...' : 'Create Key'}
          </button>
        </div>

        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Prefix</th>
                <th>Created</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {apiKeys.length === 0 ? (
                <tr>
                  <td colSpan={4} className="empty-cell">
                    No API keys yet
                  </td>
                </tr>
              ) : (
                apiKeys.map((key) => (
                  <tr key={key.id}>
                    <td className="cell-mono">
                      {key.prefix}…
                    </td>
                    <td className="cell-muted">
                      {new Date(key.createdAtUtc).toLocaleDateString()}
                    </td>
                    <td>
                      <span
                        className={
                          key.isRevoked
                            ? 'badge badge-error'
                            : 'badge badge-success'
                        }
                      >
                        {key.isRevoked ? 'Revoked' : 'Active'}
                      </span>
                    </td>
                    <td>
                      {key.isRevoked ? (
                        <span className="cell-muted">—</span>
                      ) : (
                        <button
                          onClick={() => handleRevoke(key.id)}
                          disabled={revokingId === key.id}
                          className="link link-danger"
                        >
                          Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {apiKeys.some((k) => k.plaintext) && (
          <div className="alert alert-warning mt-6 mb-0">
            <h3 className="alert-title">New API Key</h3>
            <p className="alert-text">
              The API key plaintext was shown once during creation. Store it securely — it cannot be retrieved again.
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
