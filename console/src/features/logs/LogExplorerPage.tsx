import { useState, useCallback, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { get } from '../../lib/apiClient'
import { LogEntry, LogQueryResponse } from '../../types/api'

const LEVELS = ['verbose', 'debug', 'info', 'warning', 'error', 'fatal'] as const

const levelBadgeClass: Record<string, string> = {
  verbose: 'lvl lvl-verbose',
  debug: 'lvl lvl-debug',
  info: 'lvl lvl-info',
  warning: 'lvl lvl-warning',
  error: 'lvl lvl-error',
  fatal: 'lvl lvl-fatal',
}

interface LogFilters {
  level: string[]
  tag: string
  from: string
  to: string
  search: string
  cursor: string
  limit: number
}

const defaultFilters: LogFilters = {
  level: [],
  tag: '',
  from: '',
  to: '',
  search: '',
  cursor: '',
  limit: 50,
}

export function LogExplorerPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const [filters, setFilters] = useState<LogFilters>(defaultFilters)
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [selectedLog, setSelectedLog] = useState<LogEntry | null>(null)

  const loadLogs = useCallback(
    async (active: LogFilters) => {
      if (!projectId) return
      setLoading(true)
      try {
        const params = new URLSearchParams()
        if (active.level.length > 0) active.level.forEach((l) => params.append('level', l))
        if (active.tag) params.set('tag', active.tag)
        if (active.from) params.set('from', active.from)
        if (active.to) params.set('to', active.to)
        if (active.search) params.set('search', active.search)
        if (active.cursor) params.set('cursor', active.cursor)
        params.set('limit', String(active.limit))

        const response = await get(`/projects/${projectId}/logs?${params.toString()}`)
        if (!response.ok) throw new Error('Failed to load logs')
        const data: LogQueryResponse = await response.json()

        if (active.cursor) {
          setLogs((prev) => [...prev, ...data.items])
        } else {
          setLogs(data.items)
        }
        setNextCursor(data.nextCursor ?? null)
      } catch {
        // Error handled by apiClient (redirects to login on 401)
      } finally {
        setLoading(false)
      }
    },
    [projectId],
  )

  useEffect(() => {
    void loadLogs(defaultFilters)
  }, [loadLogs])

  const applyFilters = (patch: Partial<LogFilters>) => {
    const next = { ...filters, ...patch }
    setFilters(next)
    void loadLogs(next)
  }

  const handleLoadMore = () => {
    if (nextCursor) {
      applyFilters({ cursor: nextCursor })
    }
  }

  const timePresets = [
    { label: '15m', minutes: 15 },
    { label: '1h', minutes: 60 },
    { label: '24h', minutes: 1440 },
    { label: '7d', minutes: 10080 },
  ]

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1 className="page-title">Log Explorer</h1>
          <p className="page-subtitle">Search and filter your logs</p>
        </div>
        <div className="header-actions">
          <button
            onClick={() => {
              const now = new Date()
              const from = new Date(now.getTime() - 24 * 60 * 60 * 1000)
              applyFilters({
                level: [],
                tag: '',
                search: '',
                from: from.toISOString(),
                to: now.toISOString(),
                cursor: '',
              })
            }}
            className="btn btn-secondary btn-sm"
          >
            Reset
          </button>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="filter-bar">
        <div className="filter-grid">
          <div>
            <label className="filter-label">Level</label>
            <div className="chip-row">
              {LEVELS.map((level) => {
                const active = filters.level.includes(level)
                return (
                  <button
                    key={level}
                    onClick={() => {
                      const levels = filters.level.includes(level)
                        ? filters.level.filter((l) => l !== level)
                        : [...filters.level, level]
                      applyFilters({ level: levels, cursor: '' })
                    }}
                    className={active ? 'chip chip-active' : 'chip'}
                  >
                    {level}
                  </button>
                )
              })}
            </div>
          </div>
          <div>
            <label className="filter-label">Tag</label>
            <input
              type="text"
              value={filters.tag}
              onChange={(e) => setFilters((prev) => ({ ...prev, tag: e.target.value, cursor: '' }))}
              className="input"
              placeholder="tag"
            />
          </div>
          <div>
            <label className="filter-label">Search</label>
            <input
              type="text"
              value={filters.search}
              onChange={(e) => setFilters((prev) => ({ ...prev, search: e.target.value, cursor: '' }))}
              className="input"
              placeholder="message or userId"
            />
          </div>
          <div>
            <label className="filter-label">Time Preset</label>
            <div className="chip-row">
              {timePresets.map((p) => (
                <button
                  key={p.label}
                  onClick={() => {
                    const now = new Date()
                    const from = new Date(now.getTime() - p.minutes * 60 * 1000)
                    applyFilters({
                      from: from.toISOString(),
                      to: now.toISOString(),
                      cursor: '',
                    })
                  }}
                  className="chip"
                >
                  {p.label}
                </button>
              ))}
            </div>
          </div>
        </div>

        <div className="filter-actions">
          <button
            onClick={() => applyFilters({ cursor: '' })}
            className="btn btn-primary"
          >
            Search
          </button>
        </div>
      </div>

      {/* Log Table */}
      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>Time</th>
              <th>Level</th>
              <th>Tag</th>
              <th>Message</th>
              <th>User</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {logs.map((log) => (
              <tr
                key={log.id}
                className="log-row"
                onClick={() => setSelectedLog(log)}
              >
                <td className="cell-mono">
                  {new Date(log.ts).toLocaleString()}
                </td>
                <td>
                  <span className={levelBadgeClass[log.level] ?? 'lvl'}>
                    {log.level}
                  </span>
                </td>
                <td className="cell-mono cell-truncate">{log.tag}</td>
                <td className="cell-message">
                  {log.message}
                </td>
                <td className="cell-mono">
                  {log.userId || '—'}
                </td>
                <td>
                  <button
                    onClick={(e) => {
                      e.stopPropagation()
                      setSelectedLog(log)
                    }}
                    className="link"
                  >
                    View
                  </button>
                </td>
              </tr>
            ))}
            {logs.length === 0 && (
              <tr>
                <td colSpan={6} className="empty-cell">
                  No logs found
                </td>
              </tr>
            )}
          </tbody>
        </table>

        {nextCursor && (
          <div className="table-footer">
            <button
              onClick={handleLoadMore}
              className="btn btn-secondary btn-block"
              disabled={loading}
            >
              {loading ? 'Loading...' : 'Load More'}
            </button>
          </div>
        )}
      </div>

      {/* Detail Drawer */}
      {selectedLog && (
        <div className="modal-overlay">
          <div
            className="modal-backdrop"
            onClick={() => setSelectedLog(null)}
          />
          <div className="modal">
            <div className="modal-header">
              <div>
                <h2 className="modal-title">Log Detail</h2>
                <p className="modal-meta">
                  {new Date(selectedLog.ts).toLocaleString()} • {selectedLog.level} • {selectedLog.tag}
                </p>
              </div>
              <button
                onClick={() => setSelectedLog(null)}
                className="modal-close"
              >
                ✕
              </button>
            </div>

            <div className="modal-body">
              <div className="detail-grid">
                <div className="meta-list">
                  <div className="meta-row">
                    <span className="meta-label">ID</span>
                    <span className="meta-value">{selectedLog.id}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">Level</span>
                    <span className="meta-value">{selectedLog.level}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">Tag</span>
                    <span className="meta-value">{selectedLog.tag}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">Seq</span>
                    <span className="meta-value">{selectedLog.seq}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">Session ID</span>
                    <span className="meta-value">{selectedLog.sessionId || '—'}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">User ID</span>
                    <span className="meta-value">{selectedLog.userId || '—'}</span>
                  </div>
                  <div className="meta-row">
                    <span className="meta-label">Received</span>
                    <span className="meta-value">
                      {new Date(selectedLog.receivedAtUtc).toLocaleString()}
                    </span>
                  </div>
                </div>

                <div>
                  <div className="detail-section">
                    <h3 className="section-label">Message</h3>
                    <pre className="code-block">
                      {selectedLog.message}
                    </pre>
                  </div>
                  {selectedLog.error && (
                    <div className="detail-section">
                      <h3 className="section-label">Error</h3>
                      <pre className="code-block code-block-error">
                        {selectedLog.error}
                      </pre>
                    </div>
                  )}
                  {selectedLog.stackTrace && (
                    <div className="detail-section">
                      <h3 className="section-label">Stack Trace</h3>
                      <pre className="code-block">
                        {selectedLog.stackTrace}
                      </pre>
                    </div>
                  )}
                  {selectedLog.metadataJson && (
                    <div className="detail-section">
                      <h3 className="section-label">Metadata</h3>
                      <pre className="code-block">
                        {selectedLog.metadataJson}
                      </pre>
                    </div>
                  )}
                </div>
              </div>

              {(selectedLog.appVersion || selectedLog.platform) && (
                <div className="client-context">
                  <h3 className="section-label">Client Context</h3>
                  <div className="context-grid">
                    {selectedLog.appVersion && (
                      <div className="meta-row">
                        <span className="meta-label">App Version</span>
                        <span className="meta-value">{selectedLog.appVersion}</span>
                      </div>
                    )}
                    {selectedLog.platform && (
                      <div className="meta-row">
                        <span className="meta-label">Platform</span>
                        <span className="meta-value">{selectedLog.platform}</span>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
