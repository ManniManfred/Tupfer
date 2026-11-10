using Tupfer.Designer.Oid;

namespace Tupfer.Designer.Services;

/// <summary>
/// Drucker, der Seiten selbst in Druckerpixeln zeichnet, ohne PDF-Programm dazwischen. PDF-Programme wie der
/// Acrobat Reader verändern die feinen OID-Punkte beim Drucken teils so, dass der Stift sie nicht mehr erkennt,
/// selbst mit „Tatsächliche Größe“.
/// </summary>
public interface IDirectPrinter
{
    IReadOnlyList<string> Printers();

    string? DefaultPrinter();

    /// <summary>
    /// Druckt die Seiten in Originalgröße; liefert eine Meldung, wie gedruckt wurde. Mit <paramref name="booklet"/>
    /// beidseitig, an der kurzen Kante gewendet (Broschüre, siehe <see cref="Booklet"/>).
    /// </summary>
    string Print(IReadOnlyList<PdfPageSpec> pages, int dpi, int pixelSize, string printer, bool booklet = false);
}

public static class DirectPrinting
{
    /// <summary>Wird am Desktop unter Windows gesetzt (siehe Program.cs); sonst null.</summary>
    public static IDirectPrinter? Printer { get; set; }
}
