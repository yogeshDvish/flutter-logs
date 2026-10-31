using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlutterLogs.Api.Infrastructure.Persistence;

/// <summary>
/// The single ownership gate for every project-scoped endpoint. Callers never
/// filter by project id alone: a non-owned project must look absent (404).
/// </summary>
public static class ProjectQueries
{
    public static IQueryable<Project> OwnedBy(this IQueryable<Project> projects, Guid userId) =>
        projects.Where(project => project.OwnerUserId == userId);

    public static Task<Project?> FindOwnedAsync(
        this DbSet<Project> projects,
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken,
        bool asTracking = true)
    {
        var query = asTracking ? projects : projects.AsNoTracking();

        return query.SingleOrDefaultAsync(
            project => project.Id == projectId && project.OwnerUserId == userId,
            cancellationToken);
    }
}
