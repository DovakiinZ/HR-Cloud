namespace HR.Application.Engines.Finance;

/// <summary>A rendered payslip PDF and its file name.</summary>
public sealed record PayslipPdf(byte[] Pdf, string FileName);

/// <summary>Renders and archives employee payslips for a payroll run through the Document Template engine.
/// Implemented in the Platform module (which owns the renderer); the abstraction lives here so the Payroll
/// module can depend on it without referencing Platform.</summary>
public interface IPayslipDocumentService
{
    /// <summary>Render one employee's payslip for a run. When <paramref name="archive"/> is false and an
    /// archived copy exists, the frozen bytes are served (reproducible); otherwise it renders live. When
    /// true, the rendered bytes are persisted so later edits to the template never change this payslip.</summary>
    Task<PayslipPdf> RenderAsync(Guid runId, Guid employeeId, bool archive, CancellationToken ct = default);

    /// <summary>Render and archive every payslip in a run (e.g. on approval). Returns the count archived.</summary>
    Task<int> ArchiveRunAsync(Guid runId, CancellationToken ct = default);
}
