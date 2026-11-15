using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Invoiceflow.Api.Dtos;
using UglyToad.PdfPig;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class InvoicePdfTests(ApiFactory factory)
{
    static readonly CultureInfo Canada = CultureInfo.GetCultureInfo("en-CA");

    // All the text in the PDF with spaces removed, so the check doesn't depend on how words were laid out
    static string TextOf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Concat(doc.GetPages().SelectMany(p => p.Text).Where(c => !char.IsWhiteSpace(c)));
    }

    static string NoSpaces(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public async Task Pdf_number_and_totals_match_the_api()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient("Pdf Client");
        var created = await a.AddInvoice(client.Id);
        await factory.SetTaxRate(created.Id, 0.13m); // saved under a different rate than today's 5%

        var invoice = (await a.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{created.Id}"))!;
        Assert.Equal(0.13m, invoice.TaxRate);

        var text = TextOf(await a.GetByteArrayAsync($"/api/invoices/{created.Id}/pdf"));

        Assert.Contains(invoice.Number, text);
        Assert.Contains("PdfClient", text);
        Assert.Contains("GST(13%)", text);
        Assert.Contains(NoSpaces(invoice.Subtotal.ToString("C", Canada)), text);
        Assert.Contains(NoSpaces(invoice.Tax.ToString("C", Canada)), text);
        Assert.Contains(NoSpaces(invoice.Total.ToString("C", Canada)), text);
    }

    [Fact]
    public async Task Pdf_downloads_with_a_safe_name_and_is_never_cached()
    {
        var a = await factory.SignedInUser("a");
        var client = await a.AddClient();
        var invoice = await a.AddInvoice(client.Id);

        var res = await a.GetAsync($"/api/invoices/{invoice.Id}/pdf");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"{invoice.Number}.pdf", res.Content.Headers.ContentDisposition?.FileName);
        Assert.True(res.Headers.CacheControl?.NoStore);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }
}