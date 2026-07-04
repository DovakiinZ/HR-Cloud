namespace HR.Application.Engines.Documents;

/// <summary>A rendered PDF document and its file name.</summary>
public sealed record RenderedDocument(byte[] Pdf, string FileName);

/// <summary>Renders a printable loan/advance document (branded, like the payslip). Implemented in the
/// Platform module (which owns the renderer); the abstraction lives here so the Loans module can depend
/// on it without referencing Platform.</summary>
public interface ILoanDocumentService
{
    Task<RenderedDocument> RenderAsync(Guid loanId, CancellationToken ct = default);
}

/// <summary>Renders a printable expense document. Implemented in Platform; the abstraction lives here so
/// the Expenses module can depend on it without referencing Platform.</summary>
public interface IExpenseDocumentService
{
    Task<RenderedDocument> RenderAsync(Guid expenseId, CancellationToken ct = default);
}
