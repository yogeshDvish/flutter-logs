import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { post } from '../../lib/apiClient'

interface ProjectCreated {
  id: string
  name: string
  slug: string
  description?: string
  environment: string
  createdAtUtc: string
  retentionMaxAgeDays?: number
  retentionMaxEvents?: number
  activeApiKeyCount: number
}

interface ApiKeyCreated {
  id: string
  prefix: string
  plaintext: string
  createdAtUtc: string
}

interface ProjectCreatedResponse {
  project: ProjectCreated
  apiKey: ApiKeyCreated
  publicKeyPem: string
}

export function CreateProjectWizard() {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [environment, setEnvironment] = useState('prod')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [created, setCreated] = useState<ProjectCreatedResponse | null>(null)
  const [showKey, setShowKey] = useState(false)
  const navigate = useNavigate()

  const environments = ['prod', 'staging', 'dev', 'test']

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      const response = await post('/projects', { name, description, environment })

      if (response.ok) {
        const data: ProjectCreatedResponse = await response.json()
        setCreated(data)
        // Store the API key plaintext in session so it can be shown once
        sessionStorage.setItem('pendingApiKeyPlaintext', data.apiKey.plaintext)
        setShowKey(true)
      } else {
        const errorBody = await response.json()
        setError((errorBody as any)?.detail || 'Failed to create project')
      }
    } catch {
      setError('Network error. Is the API running?')
    } finally {
      setLoading(false)
    }
  }

  function handleDone() {
    // Clear the one-time key from session
    sessionStorage.removeItem('pendingApiKeyPlaintext')
    setCreated(null)
    setShowKey(false)
    navigate('/')
  }

  function copyToClipboard(text: string) {
    navigator.clipboard.writeText(text)
  }

  if (!created) {
    return (
      <div className="page">
        <h1 className="page-title mb-6">Create Project</h1>

        {error && (
          <div className="alert alert-error">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="card form-narrow">
          <div className="field">
            <label className="field-label">
              Project Name
            </label>
            <input
              type="text"
              required
              minLength={2}
              maxLength={120}
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="input"
              placeholder="My Production App"
            />
          </div>

          <div className="field">
            <label className="field-label">
              Description (optional)
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={500}
              className="textarea"
              placeholder="Description of this project"
              rows={3}
            />
          </div>

          <div className="field">
            <label className="field-label">
              Environment
            </label>
            <select
              value={environment}
              onChange={(e) => setEnvironment(e.target.value)}
              className="select"
            >
              {environments.map((env) => (
                <option key={env} value={env}>
                  {env.charAt(0).toUpperCase() + env.slice(1)}
                </option>
              ))}
            </select>
          </div>

          <button
            type="submit"
            disabled={loading}
            className="btn btn-primary btn-block form-submit"
          >
            {loading ? 'Creating...' : 'Create Project'}
          </button>
        </form>
      </div>
    )
  }

  // One-time success panel - API key shown once
  return (
    <div className="page">
      <h1 className="page-title mb-6">Project Created</h1>

      <div className="card card-wide">
        <div className="card-header">
          <div>
            <h2 className="card-title">{created.project.name}</h2>
            <p className="card-subtitle">{created.project.slug}</p>
          </div>
          <span className="badge badge-accent">
            {created.project.environment}
          </span>
        </div>

        <div className="panel key-panel">
          <h3 className="panel-title">API Key (shown once)</h3>
          <div className="key-row">
            <code className="key-code">
              {showKey ? created.apiKey.plaintext : '••••••••'}
            </code>
            <button
              onClick={() => {
                copyToClipboard(showKey ? created.apiKey.plaintext : '')
                setShowKey(!showKey)
              }}
              className="btn btn-primary btn-sm"
            >
              {showKey ? 'Hide' : 'Show'}
            </button>
          </div>
          <p className="hint-danger">
            ⚠️ This is the only time the API key will be shown. Copy it now.
          </p>
        </div>

        <div className="panel">
          <h3 className="panel-title">Public Key</h3>
          <details className="pem">
            <summary className="pem-summary">
              Show Public Key PEM
            </summary>
            <pre className="code-block mt-3">
              {created.publicKeyPem}
            </pre>
          </details>
        </div>

        <div className="panel panel-info">
          <h3 className="panel-title">Flutter SDK Snippet</h3>
          <pre className="code-block code-dark">
            {`import 'package:sealed_trace/sealed_trace.dart';

final client = SealedTraceClient(
  baseUrl: '${window.location.origin}',
  projectId: '${created.project.id}',
  apiKey: '${created.apiKey.plaintext}',
  publicKeyPem: \`${created.publicKeyPem}\`,
);`}
          </pre>
          <button
            onClick={() => copyToClipboard(
              `import 'package:sealed_trace/sealed_trace.dart';\n\nfinal client = SealedTraceClient(\n  baseUrl: '${window.location.origin}',\n  projectId: '${created.project.id}',\n  apiKey: '${created.apiKey.plaintext}',\n  publicKeyPem: \`${created.publicKeyPem}\`,\n);`)}
            className="btn btn-primary btn-sm mt-3"
          >
            Copy Snippet
          </button>
        </div>

        <div className="mt-6">
          <button
            onClick={handleDone}
            className="btn btn-primary"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  )
}
