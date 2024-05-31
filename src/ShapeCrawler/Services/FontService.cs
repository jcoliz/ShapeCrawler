using System;
using System.Linq;
using ShapeCrawler.Shared;
using SkiaSharp;

namespace ShapeCrawler.Services;

internal static class FontService
{
    internal static int GetAdjustedFontSize(string text, ITextPortionFont font, int shapeWidth, int shapeHeight)
    {
        var surface = SKSurface.Create(new SKImageInfo(shapeWidth, shapeHeight));
        var canvas = surface.Canvas;

        const float dpi = 96f;
        const float pointsPerInch = 72f;
    
        var paint = new SKPaint();
        var fontSizePoints = font.Size;
        paint.TextSize = fontSizePoints * dpi / pointsPerInch;
        paint.Typeface = SKTypeface.FromFamilyName(font.LatinName);
        paint.IsAntialias = true;
        const int defaultPaddingSize = 7;
        const int topBottomPadding = defaultPaddingSize * 2;
        var wordMaxY = shapeHeight - topBottomPadding;

        var rect = new SKRect(defaultPaddingSize, defaultPaddingSize, shapeWidth - defaultPaddingSize, shapeHeight - defaultPaddingSize);

        var spaceWidth = paint.MeasureText(" ");

        var wordX = rect.Left;
        var wordY = rect.Top + paint.TextSize;

        var words = text.Split(' ').ToList();
        for (var i = 0; i < words.Count;)
        {
            var word = words[i];
            var wordWidth = paint.MeasureText(word);
            if (wordWidth <= rect.Right - wordX)
            {
                canvas.DrawText(word, wordX, wordY, paint);
                wordX += wordWidth + spaceWidth;
            }
            else
            {
                wordY += paint.FontSpacing;
                wordX = rect.Left;

                if (wordY > wordMaxY)
                {
                    if (paint.TextSize == Constants.MinReduceFontSize)
                    {
                        break;
                    }

                    paint.TextSize = --paint.TextSize;
                    wordX = rect.Left;
                    wordY = rect.Top + paint.TextSize;
                    i = -1;
                }
                else
                {
                    wordX += wordWidth + spaceWidth;
                    canvas.DrawText(word, wordX, wordY, paint);
                }
            }

            i++;
        }
        var points = Math.Round(paint.TextSize * pointsPerInch / dpi, 0);

        return (int)points;
    }
}