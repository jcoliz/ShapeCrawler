namespace ShapeCrawler.Shared;

internal readonly ref struct Pixels
{
    private const decimal HorizontalResolutionDpi = 96m;
    private const decimal VerticalResolutionDpi = 96m;
    private const decimal EmusPerInch = 914400m;
    private readonly decimal pixels;

    internal Pixels(int pixels)
    {
        this.pixels = pixels;
    }
    
    internal Pixels(decimal pixels)
    {
        this.pixels = pixels;
    }

    internal long AsHorizontalEmus() => (long)(this.pixels * EmusPerInch / HorizontalResolutionDpi);
   
    internal long AsVerticalEmus() => (long)(this.pixels * EmusPerInch / VerticalResolutionDpi);
}