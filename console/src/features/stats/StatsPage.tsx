import { useState, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { get } from '../../lib/apiClient'
import { StatsResponse, StatsDimension } from '../../types/api'

export function StatsPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const [stats, setStats] = useState<StatsResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!projectId) return
    loadStats()
  }, [projectId])

  async function loadStats() {
    try {
      const now = new Date()
      const from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000)
      const response = await get(
        `/projects/${projectId}/stats?from=${from.toISOString()}&to=${now.toISOString()}`
      )
      if (response.ok) {
        const data: StatsResponse = await response.json()
        setStats(data)
      } else {
        setError('Failed to load stats')
      }
    } catch {
      // Error handled by apiClient
    } finally {
      setLoading(false)
    }
  }

  if (loading) return <div className="loading-block">Loading...</div>
  if (error) return <div className="status-block status-error">{error}</div>
  if (!stats) return <div className="status-block">No data</div>

  return (
    <div className="page">
      <h1 className="page-title mb-6">Statistics</h1>

      <div className="stats-summary">
        <div className="stat-card">
          <div className="stat-label">Total Events</div>
          <div className="stat-value">{stats.totalEvents}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Time Range</div>
          <div className="stat-value-sm">
            {new Date(stats.from).toLocaleDateString()} -{' '}
            {new Date(stats.to).toLocaleDateString()}
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Top Tag</div>
          <div className="stat-value-sm">
            {stats.topTags.length > 0 ? stats.topTags[0].name : '—'}
          </div>
        </div>
      </div>

      <div className="stats-grid">
        {/* Level Donut */}
        <div className="chart-card">
          <h3 className="chart-title">Level Distribution</h3>
          <div className="donut-wrap">
            <svg
              className="donut"
              viewBox="0 0 100 100"
            >
              <circle
                cx="50"
                cy="50"
                r="40"
                fill="none"
                className="donut-track"
                strokeWidth="8"
              />
              {renderDonutArcs(stats.levels)}
            </svg>
          </div>
          <div className="legend-grid">
            {stats.levels.map((d) => (
              <div key={d.name} className="legend-item">
                <span
                  className="legend-dot"
                  style={{ backgroundColor: levelColor(d.name) }}
                />
                <span className="legend-name">{d.name}</span>
                <span className="legend-count">{d.count}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Events Per Day */}
        <div className="chart-card">
          <h3 className="chart-title">Events Per Day</h3>
          <div className="bar-chart">
            {renderBarChart(stats.eventsPerDay)}
          </div>
          <div className="day-grid">
            {stats.eventsPerDay.slice(0, 6).map((d) => (
              <div key={d.name} className="day-item">
                <div className="day-count">{d.count}</div>
                <div className="day-name">{d.name}</div>
              </div>
            ))}
          </div>
        </div>

        {/* Top Tags */}
        <div className="chart-card">
          <h3 className="chart-title">Top Tags</h3>
          {stats.topTags.length === 0 ? (
            <div className="tag-empty">No tags yet</div>
          ) : (
            <div className="tag-list">
              {stats.topTags.map((d) => (
                <div key={d.name} className="tag-row">
                  <span className="tag-name">{d.name}</span>
                  <div className="tag-track">
                    <div
                      className="tag-fill"
                      style={{ width: `${(d.count / Math.max(1, stats.topTags[0]?.count || 1)) * 100}%` }}
                    />
                  </div>
                  <span className="tag-count">{d.count}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function renderDonutArcs(levels: StatsDimension[]) {
  const total = levels.reduce((sum, d) => sum + d.count, 0)
  if (total === 0) return ''

  let startAngle = 0
  const colors = {
    verbose: '#94a3b8',
    debug: '#3b82f6',
    info: '#22c55e',
    warning: '#f59e0b',
    error: '#ef4444',
    fatal: '#a855f7',
  }

  return levels.map((d) => {
    const sweepAngle = (d.count / total) * 360
    const endAngle = startAngle + sweepAngle

    // Convert to radians
    const startRad = (startAngle * Math.PI) / 180
    const endRad = (endAngle * Math.PI) / 180

    const x1 = 50 + 40 * Math.cos(startRad)
    const y1 = 50 + 40 * Math.sin(startRad)
    const x2 = 50 + 40 * Math.cos(endRad)
    const y2 = 50 + 40 * Math.sin(endRad)

    const largeArcFlag = sweepAngle > 180 ? 1 : 0

    const pathData = [
      'M', x1, y1,
      'A', 40, 40, 0, largeArcFlag, 1, x2, y2,
      'L', 50, 50,
      'Z',
    ].join(' ')

    startAngle = endAngle

    return (
      <path
        key={d.name}
        d={pathData}
        fill={colors[d.name as keyof typeof colors] || '#94a3b8'}
      />
    )
  })
}

function renderBarChart(days: StatsDimension[]) {
  if (days.length === 0) return null

  const max = Math.max(...days.map((d) => d.count))
  return days.map((d) => {
    const height = Math.max(10, (d.count / max) * 100)
    return (
      <div key={d.name} className="bar-col">
        <div
          className="bar"
          style={{ height: `${height}%` }}
        />
        <span className="bar-label">{d.name}</span>
      </div>
    )
  })
}

function levelColor(level: string) {
  const colors: Record<string, string> = {
    verbose: '#94a3b8',
    debug: '#3b82f6',
    info: '#22c55e',
    warning: '#f59e0b',
    error: '#ef4444',
    fatal: '#a855f7',
  }
  return colors[level] || '#94a3b8'
}
