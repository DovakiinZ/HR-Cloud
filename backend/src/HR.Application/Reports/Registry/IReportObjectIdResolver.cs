namespace HR.Application.Reports.Registry;

/// <summary>Maps a catalog object code (e.g. "Employee") to its ObjectDefinition Guid (the engine's identifier).</summary>
public interface IReportObjectIdResolver
{
    Guid? ResolveId(string objectCode);
}
