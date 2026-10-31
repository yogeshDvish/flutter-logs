import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { AppShell } from '../components/AppShell'
import { Alert, Button } from '../components/Button'
import { FormField, TextAreaField } from '../components/FormField'
import { IssuedKeyPanel } from '../components/IssuedKeyPanel'
import { getErrorMessage, getFieldErrors } from '../lib/api'
import {
  deleteProject,
  getProject,
  issueKey,
  listKeys,
  revokeKey,
  updateProject,
  type IssueKeyInput,
  type UpdateProjectInput,
} from '../lib/projects'
import type { IssuedProjectKey, ProjectKeyStatus } from '../lib/types'

const statusClasses: Record<ProjectKeyStatus, string> = {
  active: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  grace: 'border-amber-200 bg-amber-50 text-amber-700',
  expired: 'border-slate-200 bg-slate-50 text-slate-600',
  revoked: 'border-rose-200 bg-rose-50 text-rose-700',
}

const statusLabels: Record<ProjectKeyStatus, string> = {
  active: 'Active',
  grace: 'Grace period',
  expired: 'Expired',
  revoked: 'Revoked',
}

function KeyStatusBadge({ status }: { status: ProjectKeyStatus }) {
  return (
    <span
      className={`rounded-full border px-2 py-0.5 text-[11px] font-medium ${statusClasses[status]}`}
    >
      {statusLabels[status]}
    </span>
  )
}

function formatDate(value: string) {
  return new Date(value).toISOString().slice(0, 10)
}

function formatDateTime(value: string) {
  return `${new Date(value).toISOString().slice(0, 16).replace('T', ' ')} UTC`
}

function StatCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <dt className="text-xs font-medium text-slate-500">{label}</dt>
      <dd className="mt-1 text-lg font-semibold tracking-tight text-slate-900">
        {value}
      </dd>
    </div>
  )
}

export function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const id = projectId ?? ''
  const { authedRequest } = useAuth()
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const [issuedKey, setIssuedKey] = useState<IssuedProjectKey | null>(null)
  const [showRotate, setShowRotate] = useState(false)
  const [label, setLabel] = useState('')
  const [graceHours, setGraceHours] = useState('24')
  const [revokeKeyId, setRevokeKeyId] = useState<string | null>(null)
  const [confirmArchive, setConfirmArchive] = useState(false)
  const [confirmPurge, setConfirmPurge] = useState(false)

  const projectQuery = useQuery({
    queryKey: ['project', id],
    queryFn: () => getProject(authedRequest, id),
    enabled: Boolean(id),
  })

  const keysQuery = useQuery({
    queryKey: ['project', id, 'keys'],
    queryFn: () => listKeys(authedRequest, id),
    enabled: Boolean(id) && projectQuery.isSuccess,
  })

  const update = useMutation({
    mutationFn: (input: UpdateProjectInput) =>
      updateProject(authedRequest, id, input),
    onSuccess: (updated) => {
      queryClient.setQueryData(['project', id], updated)
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
  })

  const rotate = useMutation({
    mutationFn: (input: IssueKeyInput) => issueKey(authedRequest, id, input),
    onSuccess: (key) => {
      setIssuedKey(key)
      setShowRotate(false)
      setLabel('')
      setGraceHours('24')
      void queryClient.invalidateQueries({ queryKey: ['project', id, 'keys'] })
      void queryClient.invalidateQueries({ queryKey: ['project', id] })
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
  })

  const revoke = useMutation({
    mutationFn: (keyId: string) => revokeKey(authedRequest, id, keyId),
    onSuccess: () => {
      setRevokeKeyId(null)
      void queryClient.invalidateQueries({ queryKey: ['project', id, 'keys'] })
      void queryClient.invalidateQueries({ queryKey: ['project', id] })
    },
  })

  const archive = useMutation({
    mutationFn: () => deleteProject(authedRequest, id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
      navigate('/')
    },
  })

  const purge = useMutation({
    mutationFn: () => deleteProject(authedRequest, id, true),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
      navigate('/')
    },
  })

  const updateFieldErrors = getFieldErrors(update.error)
  const rotateFieldErrors = getFieldErrors(rotate.error)

  function handleUpdate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const form = new FormData(event.currentTarget)

    update.mutate({
      name: String(form.get('name') ?? '').trim(),
      description: String(form.get('description') ?? '').trim(),
      ingestEnabled: form.get('ingestEnabled') === 'on',
    })
  }

  function handleRotate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    rotate.mutate({
      label: label.trim() || null,
      graceHours: graceHours === '' ? undefined : Number(graceHours),
    })
  }

  if (projectQuery.isPending) {
    return (
      <AppShell>
        <p role="status" className="text-sm text-slate-600">
          Loading project…
        </p>
      </AppShell>
    )
  }

  if (projectQuery.isError || !projectQuery.data) {
    return (
      <AppShell>
        <div className="space-y-4">
          <Alert>{getErrorMessage(projectQuery.error)}</Alert>
          <Link
            to="/"
            className="inline-block text-sm font-medium text-indigo-600 hover:text-indigo-700"
          >
            ← Back to projects
          </Link>
        </div>
      </AppShell>
    )
  }

  const project = projectQuery.data
  const isArchived = project.archivedAt !== null
  const keys = keysQuery.data ?? []
  const ingestUrl = `${window.location.origin}/api/v1/ingest`

  return (
    <AppShell>
      <div className="mb-6">
        <Link
          to="/"
          className="text-sm font-medium text-indigo-600 hover:text-indigo-700"
        >
          ← All projects
        </Link>
      </div>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              {project.name}
            </h1>
            {isArchived ? (
              <span className="rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700">
                Archived
              </span>
            ) : (
              <span className="rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700">
                {project.ingestEnabled ? 'Ingestion enabled' : 'Ingestion paused'}
              </span>
            )}
          </div>
          <p className="mt-1 max-w-2xl text-sm text-slate-600">
            {project.description ?? 'No description yet.'}
          </p>
          <p className="mt-2 text-xs text-slate-500">
            Created {formatDate(project.createdAt)} ·{' '}
            {project.activeKeyCount === 1
              ? '1 active key'
              : `${project.activeKeyCount} active keys`}
          </p>
        </div>
      </div>

      {isArchived ? (
        <div className="mt-6">
          <Alert tone="info">
            This project is archived. Ingestion is disabled and new keys cannot
            be issued. Logs are retained for the retention window.
          </Alert>
        </div>
      ) : null}

      <section aria-labelledby="stats-heading" className="mt-8">
        <h2
          id="stats-heading"
          className="text-base font-semibold text-slate-900"
        >
          Activity
        </h2>
        <dl className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Last 24 hours"
            value={`${project.stats.last24Hours.total} logs`}
          />
          <StatCard
            label="Errors (24 h)"
            value={`${project.stats.last24Hours.errors} errors`}
          />
          <StatCard
            label="Last 7 days"
            value={`${project.stats.last7Days.total} logs`}
          />
          <StatCard
            label="Last log"
            value={
              project.stats.lastLogAt
                ? formatDateTime(project.stats.lastLogAt)
                : 'No logs yet'
            }
          />
        </dl>
      </section>

      <section aria-labelledby="keys-heading" className="mt-8">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2
              id="keys-heading"
              className="text-base font-semibold text-slate-900"
            >
              Ingestion keys
            </h2>
            <p className="mt-1 text-sm text-slate-600">
              Keys are write-only and scoped to this project. Rotate before a key
              is exposed; the old key keeps working during the grace window.
            </p>
          </div>

          {!isArchived ? (
            <Button
              variant="secondary"
              onClick={() => setShowRotate((current) => !current)}
            >
              {showRotate ? 'Cancel rotation' : 'Rotate key'}
            </Button>
          ) : null}
        </div>

        {issuedKey ? (
          <div className="mt-4">
            <IssuedKeyPanel
              issuedKey={issuedKey}
              projectName={project.name}
              onDismiss={() => setIssuedKey(null)}
            />
          </div>
        ) : null}

        {showRotate && !isArchived ? (
          <form
            onSubmit={handleRotate}
            className="mt-4 grid gap-4 rounded-xl border border-slate-200 bg-white p-5 shadow-xs"
          >
            <div>
              <h3 className="text-sm font-semibold text-slate-900">
                Rotate ingestion key
              </h3>
              <p className="mt-1 text-sm text-slate-600">
                A new key is issued immediately. Existing active keys enter a
                grace window so deployments can roll out the new key.
              </p>
            </div>

            <FormField
              label="Key label (optional)"
              name="label"
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              placeholder="Production build"
              maxLength={60}
              error={rotateFieldErrors.label?.[0]}
            />

            <FormField
              label="Grace period (hours)"
              name="graceHours"
              type="number"
              min={0}
              max={24}
              step={1}
              value={graceHours}
              onChange={(event) => setGraceHours(event.target.value)}
              hint="0 revokes the old keys immediately. The default is 24 hours."
              error={rotateFieldErrors.graceHours?.[0]}
            />

            {rotate.isError ? (
              <Alert>{getErrorMessage(rotate.error)}</Alert>
            ) : null}

            <div>
              <Button type="submit" disabled={rotate.isPending}>
                {rotate.isPending ? 'Issuing…' : 'Issue new key'}
              </Button>
            </div>
          </form>
        ) : null}

        {keysQuery.isPending ? (
          <p role="status" className="mt-3 text-sm text-slate-600">
            Loading keys…
          </p>
        ) : null}

        {keysQuery.isError ? (
          <div className="mt-3">
            <Alert>{getErrorMessage(keysQuery.error)}</Alert>
          </div>
        ) : null}

        {!keysQuery.isPending && !keysQuery.isError && keys.length === 0 ? (
          <p className="mt-3 rounded-lg border border-dashed border-slate-300 bg-white/60 px-4 py-3 text-sm text-slate-600">
            No keys have been issued for this project.
          </p>
        ) : null}

        {keys.length > 0 ? (
          <ul className="mt-4 divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white">
            {keys.map((key) => (
              <li
                key={key.id}
                className="flex flex-wrap items-start justify-between gap-3 p-4"
              >
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <code className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-mono text-xs text-slate-700">
                      {key.prefix}
                    </code>
                    <KeyStatusBadge status={key.status} />
                    {key.label ? (
                      <span className="text-sm font-medium text-slate-700">
                        {key.label}
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-1 text-xs text-slate-500">
                    Created {formatDate(key.createdAt)} ·{' '}
                    {key.lastUsedAt
                      ? `Last used ${formatDateTime(key.lastUsedAt)}`
                      : 'Never used'}
                    {key.graceUntil
                      ? ` · Grace until ${formatDateTime(key.graceUntil)}`
                      : ''}
                    {key.revokedAt
                      ? ` · Revoked ${formatDateTime(key.revokedAt)}`
                      : ''}
                  </p>
                </div>

                {key.status === 'active' || key.status === 'grace' ? (
                  revokeKeyId === key.id ? (
                    <div className="flex flex-wrap gap-2">
                      <Button
                        variant="secondary"
                        onClick={() => setRevokeKeyId(null)}
                      >
                        Cancel
                      </Button>
                      <Button
                        onClick={() => revoke.mutate(key.id)}
                        disabled={revoke.isPending}
                      >
                        {revoke.isPending ? 'Revoking…' : 'Confirm revoke'}
                      </Button>
                    </div>
                  ) : (
                    <Button
                      variant="secondary"
                      onClick={() => setRevokeKeyId(key.id)}
                    >
                      Revoke
                    </Button>
                  )
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}

        {revoke.isError ? (
          <div className="mt-3">
            <Alert>{getErrorMessage(revoke.error)}</Alert>
          </div>
        ) : null}
      </section>

      <section aria-labelledby="integration-heading" className="mt-8">
        <h2
          id="integration-heading"
          className="text-base font-semibold text-slate-900"
        >
          Send your first log
        </h2>
        <p className="mt-1 text-sm text-slate-600">
          Send a JSON batch to the ingestion endpoint with an ingestion key as
          the bearer token. Keys are accepted only by this endpoint.
        </p>

        <div className="mt-3 space-y-3 rounded-xl border border-slate-200 bg-white p-5">
          <div>
            <p className="text-xs font-medium text-slate-500">Endpoint</p>
            <code className="mt-1 block break-all rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 font-mono text-xs text-slate-800">
              {ingestUrl}
            </code>
          </div>

          <div>
            <p className="text-xs font-medium text-slate-500">Example request</p>
            <pre className="mt-1 overflow-x-auto rounded-lg border border-slate-200 bg-slate-900 p-4 font-mono text-xs leading-relaxed text-slate-100">
              <code>{`curl -X POST "$FLUTTER_LOGS_ENDPOINT" \\
  -H "Authorization: Bearer <ingestion-key>" \\
  -H "Content-Type: application/json" \\
  -d '{
    "schema_version": 1,
    "installation_id": "<uuid>",
    "client": { "platform": "android", "app_version": "1.0.0" },
    "logs": [{
      "log_id": "<uuid>",
      "timestamp": "2026-10-30T12:00:00Z",
      "level": "info",
      "message": "App started"
    }]
  }'`}</code>
            </pre>
          </div>

          <p className="text-xs text-slate-500">
            The full ingestion contract, limits, and retry guidance live in{' '}
            <span className="font-mono">docs/api-contract.md</span>.
          </p>
        </div>
      </section>

      {!isArchived ? (
        <section
          aria-labelledby="settings-heading"
          className="mt-8 rounded-xl border border-slate-200 bg-white p-5 shadow-xs"
        >
          <h2
            id="settings-heading"
            className="text-base font-semibold text-slate-900"
          >
            Project settings
          </h2>
          <p className="mt-1 text-sm text-slate-600">
            Rename the project, change its description, or pause ingestion
            without revoking keys.
          </p>

          <form
            key={project.updatedAt}
            onSubmit={handleUpdate}
            className="mt-4 grid gap-4"
          >
            <FormField
              label="Project name"
              name="name"
              defaultValue={project.name}
              maxLength={80}
              required
              error={updateFieldErrors.name?.[0]}
            />

            <TextAreaField
              label="Description (optional)"
              name="description"
              defaultValue={project.description ?? ''}
              maxLength={500}
              rows={3}
              error={updateFieldErrors.description?.[0]}
            />

            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                name="ingestEnabled"
                defaultChecked={project.ingestEnabled}
                className="size-4 rounded border-slate-300 text-slate-900 focus:ring-indigo-500"
              />
              Accept new logs for this project
            </label>

            {update.isError ? (
              <Alert>{getErrorMessage(update.error)}</Alert>
            ) : null}

            <div>
              <Button type="submit" disabled={update.isPending}>
                {update.isPending ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </form>
        </section>
      ) : null}

      <section
        aria-labelledby="danger-heading"
        className="mt-8 rounded-xl border border-rose-200 bg-rose-50/60 p-5"
      >
        <h2
          id="danger-heading"
          className="text-base font-semibold text-rose-950"
        >
          Danger zone
        </h2>

        {!isArchived ? (
          <div className="mt-4 flex flex-wrap items-start justify-between gap-3 border-b border-rose-200 pb-4">
            <div>
              <h3 className="text-sm font-semibold text-slate-900">
                Archive project
              </h3>
              <p className="mt-1 max-w-xl text-sm text-slate-600">
                Stops ingestion and hides the project from the dashboard. Logs
                are retained for the retention window. Restore is not available
                in the MVP.
              </p>
            </div>

            {confirmArchive ? (
              <div className="flex flex-wrap gap-2">
                <Button
                  variant="secondary"
                  onClick={() => setConfirmArchive(false)}
                >
                  Cancel
                </Button>
                <Button
                  onClick={() => archive.mutate()}
                  disabled={archive.isPending}
                >
                  {archive.isPending ? 'Archiving…' : 'Confirm archive'}
                </Button>
              </div>
            ) : (
              <Button
                variant="secondary"
                onClick={() => setConfirmArchive(true)}
              >
                Archive project
              </Button>
            )}
          </div>
        ) : null}

        <div className="mt-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-slate-900">
              Delete permanently
            </h3>
            <p className="mt-1 max-w-xl text-sm text-slate-600">
              Removes the project, its keys, installations, logs, and statistics.
              This cannot be undone.
            </p>
          </div>

          {confirmPurge ? (
            <div className="flex flex-wrap gap-2">
              <Button
                variant="secondary"
                onClick={() => setConfirmPurge(false)}
              >
                Cancel
              </Button>
              <Button onClick={() => purge.mutate()} disabled={purge.isPending}>
                {purge.isPending ? 'Deleting…' : 'Confirm permanent delete'}
              </Button>
            </div>
          ) : (
            <Button variant="secondary" onClick={() => setConfirmPurge(true)}>
              Delete permanently
            </Button>
          )}
        </div>

        {archive.isError ? (
          <div className="mt-3">
            <Alert>{getErrorMessage(archive.error)}</Alert>
          </div>
        ) : null}

        {purge.isError ? (
          <div className="mt-3">
            <Alert>{getErrorMessage(purge.error)}</Alert>
          </div>
        ) : null}
      </section>
    </AppShell>
  )
}
