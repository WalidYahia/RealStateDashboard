namespace RealState.Web.Models;

/// <summary>
/// One line of an invoice / return / order, listed under its row in the detailed account statement («تفصيلي» — customer
/// and supplier statements). Price is the unit selling price on sales documents and the unit cost on purchase ones.
/// </summary>
public sealed record StatementItem(string Name, string? Unit, decimal Quantity, decimal Price, decimal LineTotal);
