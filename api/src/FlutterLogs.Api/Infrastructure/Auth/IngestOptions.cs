using System.Text;

namespace FlutterLogs.Api.Infrastructure.Auth;

public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>Server-side pepper mixed into every key digest. Secret.</summary>
    public string Pepper { get; set; } = string.Empty;

    /// <summary>Environment marker embedded in the plaintext key, for example <c>live</c>.</summary>
    public string KeyEnvironment { get; set; } = "live";

    /// <summary>At most two effective (unrevoked, unexpired) keys per project.</summary>
    public int MaxKeysPerProject { get; set; } = 2;

    /// <summary>How long a superseded key keeps working after rotation.</summary>
    public int RotationGraceHours { get; set; } = 24;

    public int MaxRecordsPerBatch { get; set; } = 500;

    /// <summary>
    /// Fails fast rather than hashing keys with a weak pepper. The error never
    /// echoes the configured value.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Pepper) || Encoding.UTF8.GetByteCount(Pepper) < 32)
        {
            throw new InvalidOperationException(
                "Ingest:Pepper is missing or shorter than 32 bytes. Configure it with " +
                "`dotnet user-secrets set \"Ingest:Pepper\" \"<random 32+ byte value>\"` " +
                "or the Ingest__Pepper environment variable.");
        }

        if (string.IsNullOrWhiteSpace(KeyEnvironment)
            || KeyEnvironment.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidOperationException(
                "Ingest:KeyEnvironment must be a non-empty alphanumeric value such as `live`.");
        }

        if (MaxKeysPerProject is < 1 or > 2)
        {
            throw new InvalidOperationException("Ingest:MaxKeysPerProject must be 1 or 2.");
        }

        if (RotationGraceHours is < 0 or > 24)
        {
            throw new InvalidOperationException("Ingest:RotationGraceHours must be between 0 and 24.");
        }
    }
}
