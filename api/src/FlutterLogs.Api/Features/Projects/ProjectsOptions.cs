namespace FlutterLogs.Api.Features.Projects;

public sealed class ProjectsOptions
{
    public const string SectionName = "Projects";

    public int MaxPerUser { get; set; } = 20;

    public void Validate()
    {
        if (MaxPerUser is < 1 or > 200)
        {
            throw new InvalidOperationException("Projects:MaxPerUser must be between 1 and 200.");
        }
    }
}
