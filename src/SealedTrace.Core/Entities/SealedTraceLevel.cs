namespace Arovyx.SealedTrace.Core.Entities;

/// <summary>Wire levels map to the fixed <c>Logs.Level</c> tinyint values.</summary>
public enum SealedTraceLevel : byte
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}
