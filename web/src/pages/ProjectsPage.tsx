import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { AppShell } from '../components/AppShell'
import { Alert, Button } from '../components/Button'
import { FormField, TextAreaField } from '../components/FormField'
import { IssuedKeyPanel } from '../components/IssuedKeyPanel'
import { getErrorMessage, getFieldErrors } from '../lib/api'
import {
  createProject,
  listProjects,
  type CreateProjectInput,
} from '../lib/projects'
import type { ProjectCreatedResponse } from '../lib/types'

function pluralize(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`
}

export function ProjectsPage() {
  const { authedRequest } = useAuth()
  const queryClient = useQueryClient()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [issued, setIssued] = useState<ProjectCreatedResponse | null>(null)

  const projects = useQuery({
    queryKey: ['projects', includeArchived],
    queryFn: ({ signal }) =>
      listProjects(authedRequest, includeArchived, signal),
  })

  const create = useMutation({
    mutationFn: (input: CreateProjectInput) =>
      createProject(authedRequest, input),
    onSuccess: (result) => {
      setIssued(result)
      setName('')
      setDescription('')
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
  })

  const fieldErrors = getFieldErrors(create.error)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    create.mutate({
      name: name.trim(),
      description: description.trim() || null,
    })
  }

  return (
    <AppShell>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Projects</h1>
          <p className="mt-1 max-w-xl text-sm text-slate-600">
            A project groups one application&apos;s ingestion keys, logs, and
            installations. Create one per app or environment.
          </p>
        </div>
        <span className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-medium text-slate-500">
          Phase 3 · projects &amp; keys
        </span>
      </div>

      {issued ? (
        <div className="mt-6 space-y-3">
          <IssuedKeyPanel
            issuedKey={issued.key}
            projectName={issued.project.name}
            onDismiss={() => setIssued(null)}
          />
          <Link
            to={`/projects/${issued.project.id}`}
            className="inline-block text-sm font-medium text-indigo-600 hover:text-indigo-700"
          >
            Open {issued.project.name} →
          </Link>
        </div>
      ) : null}

      <section
        aria-labelledby="create-project-heading"
        className="mt-8 rounded-xl border border-slate-200 bg-white p-5 shadow-xs"
      >
        <h2
          id="create-project-heading"
          className="text-base font-semibold text-slate-900"
        >
          Create a project
        </h2>
        <p className="mt-1 text-sm text-slate-600">
          The first ingestion key is issued automatically and shown once.
        </p>

        <form onSubmit={handleSubmit} className="mt-4 grid gap-4">
          <FormField
            label="Project name"
            name="name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Mobile app"
            maxLength={80}
            required
            error={fieldErrors.name?.[0]}
          />

          <TextAreaField
            label="Description (optional)"
            name="description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            placeholder="Production Flutter app"
            maxLength={500}
            rows={3}
            error={fieldErrors.description?.[0]}
          />

          {create.isError ? (
            <Alert>{getErrorMessage(create.error)}</Alert>
          ) : null}

          <div>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? 'Creating…' : 'Create project'}
            </Button>
          </div>
        </form>
      </section>

      <section aria-labelledby="project-list-heading" className="mt-8">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2
            id="project-list-heading"
            className="text-base font-semibold text-slate-900"
          >
            Your projects
          </h2>
          <div className="flex items-center gap-4">
            {projects.data ? (
              <span className="text-xs text-slate-500">
                {pluralize(projects.data.length, 'project')}
              </span>
            ) : null}
            <label className="flex items-center gap-2 text-xs font-medium text-slate-600">
              <input
                type="checkbox"
                checked={includeArchived}
                onChange={(event) => setIncludeArchived(event.target.checked)}
                className="size-4 rounded border-slate-300 text-slate-900 focus:ring-indigo-500"
              />
              Show archived
            </label>
          </div>
        </div>

        {projects.isPending ? (
          <p role="status" className="mt-3 text-sm text-slate-600">
            Loading projects…
          </p>
        ) : null}

        {projects.isError ? (
          <div className="mt-3">
            <Alert>{getErrorMessage(projects.error)}</Alert>
          </div>
        ) : null}

        {projects.data && projects.data.length === 0 ? (
          <div className="mt-3 rounded-xl border border-dashed border-slate-300 bg-white/60 p-6">
            <h3 className="text-sm font-semibold text-slate-900">
              No projects yet
            </h3>
            <p className="mt-1 text-sm text-slate-600">
              Create your first project above to issue an ingestion key.
            </p>
          </div>
        ) : null}

        {projects.data && projects.data.length > 0 ? (
          <ul className="mt-3 grid gap-4">
            {projects.data.map((project) => (
              <li
                key={project.id}
                className="rounded-xl border border-slate-200 bg-white p-5 shadow-xs"
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <h3 className="text-base font-semibold text-slate-900">
                        {project.name}
                      </h3>
                      {project.archivedAt ? (
                        <span className="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                          Archived
                        </span>
                      ) : (
                        <span className="rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-medium text-emerald-700">
                          {project.ingestEnabled
                            ? 'Ingestion enabled'
                            : 'Ingestion paused'}
                        </span>
                      )}
                    </div>
                    <p className="mt-1 text-sm text-slate-600">
                      {project.description ?? 'No description yet.'}
                    </p>
                  </div>

                  <Link
                    to={`/projects/${project.id}`}
                    className="text-sm font-medium text-indigo-600 hover:text-indigo-700"
                  >
                    Open project →
                  </Link>
                </div>

                <dl className="mt-4 grid gap-3 border-t border-slate-100 pt-4 sm:grid-cols-3">
                  <div>
                    <dt className="text-xs font-medium text-slate-500">
                      Last 24 h
                    </dt>
                    <dd className="mt-0.5 text-sm font-semibold text-slate-900">
                      {project.stats.last24Hours.total} logs
                    </dd>
                  </div>
                  <div>
                    <dt className="text-xs font-medium text-slate-500">
                      Errors (24 h)
                    </dt>
                    <dd className="mt-0.5 text-sm font-semibold text-slate-900">
                      {project.stats.last24Hours.errors} errors
                    </dd>
                  </div>
                  <div>
                    <dt className="text-xs font-medium text-slate-500">
                      Active keys
                    </dt>
                    <dd className="mt-0.5 text-sm font-semibold text-slate-900">
                      {pluralize(project.activeKeyCount, 'active key')}
                    </dd>
                  </div>
                </dl>
              </li>
            ))}
          </ul>
        ) : null}
      </section>
    </AppShell>
  )
}
