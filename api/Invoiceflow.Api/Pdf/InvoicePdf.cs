using System.Globalization;
using Invoiceflow.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Invoiceflow.Api.Pdf;

// Draws a saved invoice as a PDF. Same layout as the live preview (InvoicePreview.jsx), but a separate
// renderer on purpose: what the client receives never depends on what a browser drew.
// QuestPDF draws text as text. It never parses HTML or fetches URLs, so nothing typed can become markup or a request.
public static class InvoicePdf
{
    public record Sender(string Name, string Email);

    // Same colours as tokens.css
    const string Ink = "#151A17";
    const string Ink2 = "#3B403C";
    const string Muted = "#6E726C";
    const string Rule = "#EEEBE4";
    const string Green = "#1E5B43";

    static readonly CultureInfo Canada = CultureInfo.GetCultureInfo("en-CA");
    static string Money(decimal amount) => amount.ToString("C", Canada); // $1,234.50, same as the web app
    static string Date(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    static string Plain(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    public static byte[] Render(Invoice invoice, Sender sender)
    {
        var number = InvoiceMath.FormatNumber(invoice.Number);
        var lines = invoice.LineItems.OrderBy(li => li.Position).ToList();
        var client = invoice.Client;

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(54);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).LineHeight(1.45f));

            page.Content().Column(col =>
            {
                // Who it's from, and which invoice
                col.Item().Row(row =>
                {
                    row.Spacing(12);
                    row.ConstantItem(32).Height(32).Background(Ink).AlignCenter().AlignMiddle()
                        .Text(Initials(sender.Name)).FontSize(10).SemiBold().FontColor(Colors.White);
                    row.RelativeItem().AlignMiddle().Column(from =>
                    {
                        from.Item().Text(sender.Name).FontSize(12).SemiBold();
                        from.Item().Text(sender.Email).FontColor(Muted);
                    });
                    row.AutoItem().Column(title =>
                    {
                        title.Item().AlignRight().Text("Invoice").FontSize(28);
                        title.Item().AlignRight().Text(number).FontColor(Muted);
                        if (Stamp(invoice.Status) is { } stamp)
                            title.Item().AlignRight().PaddingTop(4).Text(stamp.Text).FontSize(8).SemiBold().LetterSpacing(0.08f).FontColor(stamp.Color);
                    });
                });

                // Billed to, issued, due
                col.Item().PaddingTop(28).Row(row =>
                {
                    row.Spacing(16);
                    row.RelativeItem(1.4f).Column(c =>
                    {
                        Label(c.Item(), "Billed to");
                        c.Item().Text(client.Name).SemiBold();
                        if (client.Company is { } company) c.Item().Text(company).FontColor(Muted);
                        c.Item().Text(client.Email).FontColor(Muted);
                        if (client.Address is { } address) c.Item().Text(address).FontColor(Muted);
                    });
                    row.RelativeItem().Column(c => { Label(c.Item(), "Issued"); c.Item().Text(Date(invoice.IssueDate)).SemiBold(); });
                    row.RelativeItem().Column(c => { Label(c.Item(), "Due"); c.Item().Text(Date(invoice.DueDate)).SemiBold(); });
                });

                // Line items. The header row repeats on every page.
                col.Item().PaddingTop(28).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(48);
                        c.ConstantColumn(84);
                        c.ConstantColumn(84);
                    });
                    table.Header(h =>
                    {
                        Label(h.Cell().Element(Head), "Description");
                        Label(h.Cell().Element(Head).PaddingLeft(6).AlignRight(), "Qty");
                        Label(h.Cell().Element(Head).PaddingLeft(6).AlignRight(), "Rate");
                        Label(h.Cell().Element(Head).PaddingLeft(6).AlignRight(), "Amount");
                    });
                    foreach (var li in lines)
                    {
                        table.Cell().Element(Cell).Text(li.Description);
                        table.Cell().Element(Cell).PaddingLeft(6).AlignRight().Text(Plain(li.Quantity));
                        table.Cell().Element(Cell).PaddingLeft(6).AlignRight().Text(Money(li.UnitPrice));
                        table.Cell().Element(Cell).PaddingLeft(6).AlignRight().Text(Money(InvoiceMath.LineAmount(li.Quantity, li.UnitPrice)));
                    }
                });

                // Totals: the ones saved on the invoice, with the rate saved on the invoice. Never recalculated here.
                col.Item().PaddingTop(14).AlignRight().Width(220).Column(t =>
                {
                    t.Spacing(6);
                    TotalRow(t, "Subtotal", Money(invoice.Subtotal));
                    TotalRow(t, $"GST ({Plain(invoice.TaxRate * 100)}%)", Money(invoice.Tax));
                    t.Item().PaddingTop(2).BorderTop(1).BorderColor(Ink).PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().AlignBottom().Text("Total due").SemiBold();
                        r.AutoItem().Text(Money(invoice.Total)).FontSize(22);
                    });
                });

                if (invoice.Notes is { } notes)
                    col.Item().PaddingTop(24).BorderTop(1).BorderColor(Rule).PaddingTop(12).Text(notes).FontColor(Ink2);
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(number).FontSize(8).FontColor(Muted);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }))
        .WithMetadata(new DocumentMetadata { Title = $"Invoice {number}", Author = sender.Name, Creator = "Invoiceflow" })
        .GeneratePdf();
    }

    // A draft PDF says so, so it's never mistaken for the one the client received
    static (string Text, string Color)? Stamp(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => ("DRAFT · NOT SENT", Muted),
        InvoiceStatus.Paid => ("PAID", Green),
        _ => null,
    };

    static void Label(IContainer c, string text) =>
        c.PaddingBottom(3).Text(text.ToUpperInvariant()).FontSize(7.5f).LetterSpacing(0.08f).FontColor(Muted);

    static IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(Ink).PaddingBottom(4);
    static IContainer Cell(IContainer c) => c.BorderBottom(1).BorderColor(Rule).PaddingVertical(8);

    static void TotalRow(ColumnDescriptor t, string label, string value) => t.Item().Row(r =>
    {
        r.RelativeItem().Text(label).FontColor(Ink2);
        r.AutoItem().Text(value).FontColor(Ink2);
    });

    static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "";
        return (words[0][..1] + (words.Length > 1 ? words[^1][..1] : "")).ToUpperInvariant();
    }
}