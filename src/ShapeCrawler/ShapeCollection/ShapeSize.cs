using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ShapeCrawler.Units;
using A = DocumentFormat.OpenXml.Drawing;

namespace ShapeCrawler.ShapeCollection;

internal sealed class ShapeSize
{
    private readonly OpenXmlPart sdkTypedOpenXmlPart;
    private readonly OpenXmlElement sdkPShapeTreeElement;

    internal ShapeSize(OpenXmlPart sdkTypedOpenXmlPart, OpenXmlElement sdkPShapeTreeElement)
    {
        this.sdkTypedOpenXmlPart = sdkTypedOpenXmlPart;
        this.sdkPShapeTreeElement = sdkPShapeTreeElement;
    }

    internal decimal Height() => UnitConverter.VerticalEmuToPixel(this.AExtents().Cy!);
   
    internal void UpdateHeight(decimal heightPixels) => this.AExtents().Cy = UnitConverter.VerticalPixelToEmu(heightPixels);
    
    internal decimal Width() => UnitConverter.HorizontalEmuToPixel(this.AExtents().Cx!);
    
    internal void UpdateWidth(decimal widthPixels) => this.AExtents().Cx = UnitConverter.HorizontalPixelToEmu(widthPixels);

    private A.Extents AExtents()
    {
        var aExtents = this.sdkPShapeTreeElement.Descendants<A.Extents>().FirstOrDefault();
        if (aExtents != null)
        {
            return aExtents;
        }

        return new ReferencedPShape(this.sdkTypedOpenXmlPart, this.sdkPShapeTreeElement).ATransform2D().Extents!;
    }
}