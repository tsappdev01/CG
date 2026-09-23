using PdfSharp.Fonts;

namespace CGTOOL.Web.Data.Governance;

/// <summary>PDFsharp has no GDI to fall back on for font resolution on .NET (unlike the original
/// Windows-only PDFsharp), so it needs an explicit IFontResolver or every XFont construction throws.
/// Uses the DejaVu Sans family bundled under wwwroot/fonts (Bitstream Vera license -- free to embed
/// and redistribute) rather than depending on whatever fonts happen to be installed on the host OS,
/// which differs between this sandbox (Linux) and the production IIS server (Windows).</summary>
public class PdfFontResolver : IFontResolver
{
    private readonly string _fontsDirectory;

    public PdfFontResolver(IWebHostEnvironment env)
    {
        _fontsDirectory = Path.Combine(env.WebRootPath, "fonts");
    }

    public string DefaultFontName => "DejaVu Sans";

    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(_fontsDirectory, faceName switch
    {
        "DejaVu Sans#Bold" => "DejaVuSans-Bold.ttf",
        _ => "DejaVuSans.ttf",
    }));

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "DejaVu Sans#Bold" : "DejaVu Sans#Normal");
}
