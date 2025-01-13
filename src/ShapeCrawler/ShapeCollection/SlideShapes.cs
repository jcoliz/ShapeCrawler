﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ImageMagick;
using ShapeCrawler.Exceptions;
using ShapeCrawler.Extensions;
using ShapeCrawler.Presentations;
using ShapeCrawler.Shared;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using A14 = DocumentFormat.OpenXml.Office2010.Drawing;
using A16 = DocumentFormat.OpenXml.Office2016.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Position = ShapeCrawler.Positions.Position;

namespace ShapeCrawler.ShapeCollection;

internal sealed class SlideShapes : ISlideShapes
{
    private const long DefaultTableWidthEmu = 8128000L;
    private readonly SlidePart sdkSlidePart;
    private readonly IShapes shapes;
    private readonly MediaCollection mediaCollection;

    internal SlideShapes(SlidePart sdkSlidePart, IShapes shapes, MediaCollection mediaCollection)
    {
        this.sdkSlidePart = sdkSlidePart;
        this.shapes = shapes;
        this.mediaCollection = mediaCollection;
    }

    public int Count => this.shapes.Count;

    internal MediaCollection MediaCollection => this.mediaCollection;

    public IShape this[int index] => this.shapes[index];

    public void Add(IShape addingShape)
    {
        var pShapeTree = this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!;
        var id = this.NextShapeId();
        var allShapeNames = this.Select(shape => shape.Name);

        if (addingShape is CopyableShape copyable)
        {
            copyable.CopyTo(id, pShapeTree, allShapeNames);
        }
        else
        {
            throw new SCException($"Adding {addingShape.GetType().Name} is not supported.");
        }
    }

    public void AddAudio(int x, int y, Stream audio) => this.AddAudio(x, y, audio, AudioType.Mp3);

    public void AddAudio(int x, int y, Stream audio, AudioType type)
    {
        string? contentType;
        string? extension;
        switch (type)
        {
            case AudioType.Mp3:
                contentType = "audio/mpeg";
                extension = ".mp3";
                break;
            case AudioType.Wave:
                contentType = "audio/wav";
                extension = ".wav";
                break;
            default:
                throw new SCException("Unsupported audio type.");
        }

        var xEmu = UnitConverter.HorizontalPixelToEmu(x);
        var yEmu = UnitConverter.VerticalPixelToEmu(y);
        var sdkPresentationDocument = (PresentationDocument)this.sdkSlidePart.OpenXmlPackage;
        var mediaDataPart = sdkPresentationDocument.CreateMediaDataPart(contentType, extension);
        audio.Position = 0;
        mediaDataPart.FeedData(audio);
        var imageStream = new Assets(Assembly.GetExecutingAssembly()).StreamOf("audio-image.png");

        var audioRef = this.sdkSlidePart.AddAudioReferenceRelationship(mediaDataPart);
        var mediaRef = this.sdkSlidePart.AddMediaReferenceRelationship(mediaDataPart);

        var audioFromFile = new A.AudioFromFile() { Link = audioRef.Id };

        var appNonVisualDrawingPropsExtensionList = new P.ApplicationNonVisualDrawingPropertiesExtensionList();

        var appNonVisualDrawingPropsExtension = new P.ApplicationNonVisualDrawingPropertiesExtension
        { Uri = "{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}" };

        var media = new DocumentFormat.OpenXml.Office2010.PowerPoint.Media { Embed = mediaRef.Id };
        media.AddNamespaceDeclaration("p14", "http://schemas.microsoft.com/office/powerpoint/2010/main");
        appNonVisualDrawingPropsExtension.Append(media);
        appNonVisualDrawingPropsExtensionList.Append(appNonVisualDrawingPropsExtension);

        var pPicture = this.CreatePPicture(imageStream, "Audio");

        var transform2D = pPicture.ShapeProperties!.Transform2D!;
        transform2D.Offset!.X = xEmu;
        transform2D.Offset!.Y = yEmu;
        transform2D.Extents!.Cx = 609600L;
        transform2D.Extents!.Cy = 609600L;

        var nonVisualPictureProps = pPicture.NonVisualPictureProperties!;
        var nonVisualDrawingProps = pPicture.NonVisualDrawingProperties();
        var hyperlinkOnClick = new A.HyperlinkOnClick
        { Id = string.Empty, Action = "ppaction://media" };
        nonVisualDrawingProps.Append(hyperlinkOnClick);
        nonVisualPictureProps.Append(new P.NonVisualPictureDrawingProperties());

        var applicationNonVisualDrawingProps = nonVisualPictureProps.ApplicationNonVisualDrawingProperties!;
        applicationNonVisualDrawingProps.Append(audioFromFile);
        applicationNonVisualDrawingProps.Append(appNonVisualDrawingPropsExtensionList);
    }

    public void AddPicture(Stream image)
    {
        image.Position = 0;
        var imageCopy = new MemoryStream();
        image.CopyTo(imageCopy);
        imageCopy.Position = 0;
        image.Position = 0;
        using var skBitmap = SKBitmap.Decode(imageCopy);
        
        int height;
        int width;
        P.Picture pPicture;

        if (skBitmap != null)
        {
            height = skBitmap.Height;
            width = skBitmap.Width;

            if (height > 500)
            {
                height = 500;
                width = (int)(height * skBitmap.Width / (decimal)skBitmap.Height);
            }

            if (width > 500)
            {
                width = 500;
                height = (int)(width * skBitmap.Height / (decimal)skBitmap.Width);
            }
            
            pPicture = this.CreatePPicture(image, "Picture");
        }
        else
        {
            image.Position = 0;
            using var imageMagick = new MagickImage(image, new MagickReadSettings
            {
                BackgroundColor = MagickColors.Transparent
            });
            imageMagick.Format = MagickFormat.Png;

            width = imageMagick.Width < 500 ? (int)imageMagick.Width : 500;
            height = imageMagick.Height < 500 ? (int)imageMagick.Height : 500;

            if (width == 500 || height == 500)
            {
                imageMagick.Resize((uint)width, (uint)height);
            }
            
            var rasterStream = new MemoryStream();
            imageMagick.Write(rasterStream);
            image.Position = 0;
            rasterStream.Position = 0;
            pPicture = this.CreateSvgPPicture(rasterStream, image, "Picture");
        }
        
        // Fix up the sizes
        var xEmu = UnitConverter.HorizontalPixelToEmu(100m);
        var yEmu = UnitConverter.VerticalPixelToEmu(100m);
        var cxEmu = UnitConverter.HorizontalPixelToEmu(width);
        var cyEmu = UnitConverter.VerticalPixelToEmu(height);
        var transform2D = pPicture.ShapeProperties!.Transform2D!;
        transform2D.Offset!.X = xEmu;
        transform2D.Offset!.Y = yEmu;
        transform2D.Extents!.Cx = cxEmu;
        transform2D.Extents!.Cy = cyEmu;
    }

    public void AddVideo(int x, int y, Stream stream)
    {
        var sdkPresDocument = (PresentationDocument)this.sdkSlidePart.OpenXmlPackage;
        var xEmu = UnitConverter.HorizontalPixelToEmu(x);
        var yEmu = UnitConverter.VerticalPixelToEmu(y);

        var mediaDataPart = sdkPresDocument.CreateMediaDataPart("video/mp4", ".mp4");

        stream.Position = 0;
        mediaDataPart.FeedData(stream);
        var imgPartRId = $"rId{Guid.NewGuid().ToString().Replace("-", string.Empty)[..5]}";
        var imagePart = this.sdkSlidePart.AddNewPart<ImagePart>("image/png", imgPartRId);
        var imageStream = new Assets(Assembly.GetExecutingAssembly()).StreamOf("video-image.bmp");
        imagePart.FeedData(imageStream);
        var videoRr = this.sdkSlidePart.AddVideoReferenceRelationship(mediaDataPart);
        var mediaRr = this.sdkSlidePart.AddMediaReferenceRelationship(mediaDataPart);

        var pPicture = new P.Picture();

        P.NonVisualPictureProperties nonVisualPictureProperties1 = new();

        var shapeId = (uint)this.shapes.Max(sp => sp.Id) + 1;
        P.NonVisualDrawingProperties nonVisualDrawingProperties2 = new() { Id = shapeId, Name = $"Video{shapeId}" };
        var hyperlinkOnClick1 = new A.HyperlinkOnClick()
        { Id = string.Empty, Action = "ppaction://media" };

        A.NonVisualDrawingPropertiesExtensionList
            nonVisualDrawingPropertiesExtensionList1 = new();

        A.NonVisualDrawingPropertiesExtension nonVisualDrawingPropertiesExtension1 =
            new() { Uri = "{FF2B5EF4-FFF2-40B4-BE49-F238E27FC236}" };

        nonVisualDrawingPropertiesExtensionList1.Append(nonVisualDrawingPropertiesExtension1);

        nonVisualDrawingProperties2.Append(hyperlinkOnClick1);
        nonVisualDrawingProperties2.Append(nonVisualDrawingPropertiesExtensionList1);

        P.NonVisualPictureDrawingProperties nonVisualPictureDrawingProperties1 = new();
        var pictureLocks1 = new A.PictureLocks() { NoChangeAspect = true };

        nonVisualPictureDrawingProperties1.Append(pictureLocks1);

        P.ApplicationNonVisualDrawingProperties applicationNonVisualDrawingProperties2 = new();
        var videoFromFile1 = new A.VideoFromFile() { Link = videoRr.Id };

        P.ApplicationNonVisualDrawingPropertiesExtensionList
            applicationNonVisualDrawingPropertiesExtensionList1 = new();

        P.ApplicationNonVisualDrawingPropertiesExtension applicationNonVisualDrawingPropertiesExtension1 =
            new() { Uri = "{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}" };

        var media1 = new DocumentFormat.OpenXml.Office2010.PowerPoint.Media() { Embed = mediaRr.Id };
        media1.AddNamespaceDeclaration("p14", "http://schemas.microsoft.com/office/powerpoint/2010/main");

        applicationNonVisualDrawingPropertiesExtension1.Append(media1);

        applicationNonVisualDrawingPropertiesExtensionList1.Append(applicationNonVisualDrawingPropertiesExtension1);

        applicationNonVisualDrawingProperties2.Append(videoFromFile1);
        applicationNonVisualDrawingProperties2.Append(applicationNonVisualDrawingPropertiesExtensionList1);

        nonVisualPictureProperties1.Append(nonVisualDrawingProperties2);
        nonVisualPictureProperties1.Append(nonVisualPictureDrawingProperties1);
        nonVisualPictureProperties1.Append(applicationNonVisualDrawingProperties2);

        P.BlipFill blipFill1 = new();
        A.Blip blip1 = new() { Embed = imgPartRId };

        A.Stretch stretch1 = new();
        A.FillRectangle fillRectangle1 = new();

        stretch1.Append(fillRectangle1);

        blipFill1.Append(blip1);
        blipFill1.Append(stretch1);

        P.ShapeProperties shapeProperties1 = new();

        A.Transform2D transform2D1 = new();
        A.Offset offset2 = new() { X = xEmu, Y = yEmu };
        A.Extents extents2 = new() { Cx = 609600L, Cy = 609600L };

        transform2D1.Append(offset2);
        transform2D1.Append(extents2);

        A.PresetGeometry presetGeometry1 = new()
        { Preset = A.ShapeTypeValues.Rectangle };
        A.AdjustValueList adjustValueList1 = new();

        presetGeometry1.Append(adjustValueList1);

        shapeProperties1.Append(transform2D1);
        shapeProperties1.Append(presetGeometry1);

        pPicture.Append(nonVisualPictureProperties1);
        pPicture.Append(blipFill1);
        pPicture.Append(shapeProperties1);

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(pPicture);

        DocumentFormat.OpenXml.Office2010.PowerPoint.CreationId creationId1 = new() { Val = (UInt32Value)3972997422U };
        creationId1.AddNamespaceDeclaration("p14", "http://schemas.microsoft.com/office/powerpoint/2010/main");
    }

    public void AddShape(int x, int y, int width, int height, Geometry geometry = Geometry.Rectangle)
    {
        var xml = new Assets(Assembly.GetExecutingAssembly()).StringOf("new-rectangle.xml");
        var sdkPShape = new P.Shape(xml);

        var cNvPr = sdkPShape.Descendants<P.NonVisualDrawingProperties>().FirstOrDefault()
            ?? throw new SCException("Malformed shape: No NonVisualDrawingProperties");
        cNvPr.Name = geometry.ToString();

        var position = new Position(this.sdkSlidePart, sdkPShape);
        position.UpdateX(x);
        position.UpdateY(y);

        var size = new ShapeSize(this.sdkSlidePart, sdkPShape);
        size.UpdateWidth(width);
        size.UpdateHeight(height);

        var spPr = sdkPShape.GetFirstChild<P.ShapeProperties>()
            ?? throw new SCException("Malformed shape: No shape properties");
        var shapeGeometry = new ShapeGeometry(spPr);
        shapeGeometry.UpdateGeometry(geometry);

        new ShapeId(sdkPShape).Update(this.NextShapeId());

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(sdkPShape);
    }

    public void AddLine(string xml)
    {
        var newPConnectionShape = new P.ConnectionShape(xml);

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(newPConnectionShape);
    }

    public void AddLine(int startPointX, int startPointY, int endPointX, int endPointY)
    {
        var xml = new Assets(Assembly.GetExecutingAssembly()).StringOf("new-line.xml");
        var pConnectionShape = new P.ConnectionShape(xml);
        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(pConnectionShape);

        var deltaY = endPointY - startPointY;
        var cx = endPointX;

        var cy = endPointY;
        if (deltaY == 0)
        {
            cy = 0;
        }

        if (startPointX == endPointX)
        {
            cx = 0;
        }

        var x = startPointX;
        var y = startPointY;
        var flipV = false;
        var flipH = false;
        if (startPointX > endPointX && endPointY > startPointY)
        {
            x = endPointX;
            y = startPointY;
            cx = startPointX - endPointX;
            cy = endPointY;
            flipH = true;
        }
        else if (startPointX > endPointX && startPointY == endPointY)
        {
            x = startPointX;
            cx = Math.Abs(startPointX - endPointX);
            cy = 0;
        }
        else if (startPointY > endPointY)
        {
            y = startPointY;
            cy = endPointY;
            flipV = true;
        }

        if (cx == 0)
        {
            flipV = true;
        }

        if (startPointX > endPointX)
        {
            flipH = true;
        }

        var idAndName = this.GenerateIdAndName();
        pConnectionShape.NonVisualConnectionShapeProperties!.NonVisualDrawingProperties!.Id = (uint)idAndName.Item1;

        var xEmu = UnitConverter.HorizontalPixelToEmu(x);
        var yEmu = UnitConverter.VerticalPixelToEmu(y);
        var cxEmu = UnitConverter.HorizontalPixelToEmu(cx);
        var cyEmu = UnitConverter.VerticalPixelToEmu(cy);
        var aXfrm = pConnectionShape.ShapeProperties!.Transform2D!;
        aXfrm.Offset!.X = xEmu;
        aXfrm.Offset!.Y = yEmu;
        aXfrm.Extents!.Cx = cxEmu;
        aXfrm.Extents!.Cy = cyEmu;
        aXfrm.HorizontalFlip = new BooleanValue(flipH);
        aXfrm.VerticalFlip = new BooleanValue(flipV);
    }

    public void AddTable(int x, int y, int columnsCount, int rowsCount) =>
        this.AddTable(x, y, columnsCount, rowsCount, TableStyle.MediumStyle2Accent1);

    public void AddTable(int x, int y, int columnsCount, int rowsCount, ITableStyle style)
    {
        var shapeName = this.GenerateNextTableName();
        var xEmu = UnitConverter.HorizontalPixelToEmu(x);
        var yEmu = UnitConverter.VerticalPixelToEmu(y);
        var tableHeightEmu = Constants.DefaultRowHeightEmu * rowsCount;

        var graphicFrame = new P.GraphicFrame();
        var nonVisualGraphicFrameProperties = new P.NonVisualGraphicFrameProperties();
        var nonVisualDrawingProperties = new P.NonVisualDrawingProperties
        { Id = (uint)this.NextShapeId(), Name = shapeName };
        var nonVisualGraphicFrameDrawingProperties = new P.NonVisualGraphicFrameDrawingProperties();
        var applicationNonVisualDrawingProperties = new P.ApplicationNonVisualDrawingProperties();
        nonVisualGraphicFrameProperties.Append(nonVisualDrawingProperties);
        nonVisualGraphicFrameProperties.Append(nonVisualGraphicFrameDrawingProperties);
        nonVisualGraphicFrameProperties.Append(applicationNonVisualDrawingProperties);

        var offset = new A.Offset { X = xEmu, Y = yEmu };
        var extents = new A.Extents { Cx = DefaultTableWidthEmu, Cy = tableHeightEmu };
        var pTransform = new P.Transform(offset, extents);

        var graphic = new A.Graphic();
        var graphicData = new A.GraphicData
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" };
        var aTable = new A.Table();

        var tableProperties = new A.TableProperties { FirstRow = true, BandRow = true };
        var tableStyleId = new A.TableStyleId
        { Text = ((TableStyle)style).Guid };
        tableProperties.Append(tableStyleId);

        var tableGrid = new A.TableGrid();
        var gridWidthEmu = DefaultTableWidthEmu / columnsCount;
        for (var i = 0; i < columnsCount; i++)
        {
            var gridColumn = new A.GridColumn { Width = gridWidthEmu };
            tableGrid.Append(gridColumn);
        }

        aTable.Append(tableProperties);
        aTable.Append(tableGrid);
        for (var i = 0; i < rowsCount; i++)
        {
            aTable.AddRow(columnsCount);
        }

        graphicData.Append(aTable);
        graphic.Append(graphicData);
        graphicFrame.Append(nonVisualGraphicFrameProperties);
        graphicFrame.Append(pTransform);
        graphicFrame.Append(graphic);

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(graphicFrame);
    }

    public void Remove(IShape shape)
    {
        var removingShape = this.shapes.FirstOrDefault(sp => sp.Id == shape.Id) ?? throw new SCException("Shape is not found.");
        removingShape.Remove();
    }

    public T GetById<T>(int id) 
        where T : IShape => this.shapes.GetById<T>(id);

    public T? TryGetById<T>(int id) 
        where T : IShape => this.shapes.TryGetById<T>(id);

    public T GetByName<T>(string name) 
        where T : IShape => this.shapes.GetByName<T>(name);

    public T? TryGetByName<T>(string name) 
        where T : IShape => this.shapes.TryGetByName<T>(name);

    public IShape GetByName(string name) => this.shapes.GetByName(name);

    public T Last<T>() 
        where T : IShape => this.shapes.Last<T>();

    public IEnumerator<IShape> GetEnumerator() => this.shapes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    
    private (int, string) GenerateIdAndName()
    {
        var maxId = 0;
        var shapes = this.shapes;
        if (shapes.Any())
        {
            maxId = shapes.Max(s => s.Id);
        }

        var maxOrder = Regex.Matches(string.Join(string.Empty, shapes.Select(s => s.Name)), "\\d+", RegexOptions.None, TimeSpan.FromSeconds(100))

#if NETSTANDARD2_0
            .Cast<Match>()
#endif

            .Select(m => int.Parse(m.Value))
            .DefaultIfEmpty(0)
            .Max();

        return (maxId + 1, $"AutoShape {maxOrder + 1}");
    }

    private string GenerateNextTableName()
    {
        var maxOrder = 0;
        foreach (var shape in this.shapes)
        {
            var matchOrder = Regex.Match(shape.Name, "(?!Table )\\d+", RegexOptions.None, TimeSpan.FromSeconds(100));
            if (!matchOrder.Success)
            {
                continue;
            }

            var order = int.Parse(matchOrder.Value);
            if (order > maxOrder)
            {
                maxOrder = order;
            }
        }

        return $"Table {maxOrder + 1}";
    }

    private bool TryGetImageRId(string hash, out string imgPartRId)
    {
        if (this.mediaCollection.TryGetImagePart(hash, out var imagePart))
        {
            // Image already exists in the presentation sofar.
            // Do we have a reference to it on this slide?
            var found = this.sdkSlidePart.ImageParts.Where(x => x.Uri == imagePart.Uri);
            if (found.Any())
            {
                // Yes, we already have a relationship with this part on this slide
                // So use that relationship ID
                imgPartRId = this.sdkSlidePart.GetIdOfPart(imagePart);
            }
            else
            {
                // No, so let's create a relationship to it
                imgPartRId = this.sdkSlidePart.CreateRelationshipToPart(imagePart);
            }

            return true;
        }

        // Sorry, you'll need to create a new image part
        imgPartRId = string.Empty;
        return false;
    }

    private P.Picture CreatePPicture(Stream imageStream, string shapeName)
    {
        var scStream = new ImageStream(imageStream);
        var hash = scStream.Base64Hash;

        // Does this part already exist in the presentation?
        if (!this.TryGetImageRId(hash, out var imgPartRId))
        {
            // No, let's create it!
            var mimeType = scStream.Mime;
            (imgPartRId, var imagePart) = this.sdkSlidePart.AddImagePart(imageStream, mimeType);
            this.mediaCollection.SetImagePart(hash, imagePart);
        }

        var nonVisualPictureProperties = new P.NonVisualPictureProperties();
        var shapeId = (uint)this.NextShapeId();
        var nonVisualDrawingProperties = new P.NonVisualDrawingProperties
        {
            Id = shapeId,
            Name = $"{shapeName} {shapeId}"
        };
        var nonVisualPictureDrawingProperties = new P.NonVisualPictureDrawingProperties();
        var appNonVisualDrawingProperties = new P.ApplicationNonVisualDrawingProperties();

        nonVisualPictureProperties.Append(nonVisualDrawingProperties);
        nonVisualPictureProperties.Append(nonVisualPictureDrawingProperties);
        nonVisualPictureProperties.Append(appNonVisualDrawingProperties);

        var blipFill = new P.BlipFill();
        var blip = new A.Blip { Embed = imgPartRId };
        var stretch = new A.Stretch();
        blipFill.Append(blip);
        blipFill.Append(stretch);

        var transform2D = new A.Transform2D(
            new A.Offset { X = 0, Y = 0 },
            new A.Extents { Cx = 0, Cy = 0 });

        var presetGeometry = new A.PresetGeometry
        { Preset = A.ShapeTypeValues.Rectangle };
        var shapeProperties = new P.ShapeProperties();
        shapeProperties.Append(transform2D);
        shapeProperties.Append(presetGeometry);

        var pPicture = new P.Picture();
        pPicture.Append(nonVisualPictureProperties);
        pPicture.Append(blipFill);
        pPicture.Append(shapeProperties);

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.Append(pPicture);

        return pPicture;
    }

    private P.Picture CreateSvgPPicture(Stream rasterStream, Stream svgStream, string shapeName)
    {
        // The SVG Blip contains the vector data
        var svgHash = new ImageStream(svgStream).Base64Hash;
        if (!this.TryGetImageRId(svgHash, out var svgPartRId))
        {
            (svgPartRId, var svgPart) = this.sdkSlidePart.AddImagePart(svgStream, "image/svg+xml");
            this.mediaCollection.SetImagePart(svgHash, svgPart);
        }

        // There is a possible optimization here. If we've previously in this session rasterized
        // this SVG, we could look up the rasterized image by reference to its vector image so
        // we wouldn't have to rasterize it every time.

        // The A.Blip contains a raster representation of the vector image
        var imgHash = new ImageStream(rasterStream).Base64Hash;
        if (!this.TryGetImageRId(imgHash, out var imgPartRId))
        {
            (imgPartRId, var imagePart) = this.sdkSlidePart.AddImagePart(rasterStream, "image/png");
            this.mediaCollection.SetImagePart(imgHash, imagePart);
        }

        var nonVisualPictureProperties = new P.NonVisualPictureProperties();
        var shapeId = (uint)this.NextShapeId();
        var nonVisualDrawingProperties = new P.NonVisualDrawingProperties
        {
            Id = shapeId,
            Name = $"{shapeName} {shapeId}"
        };
        var nonVisualPictureDrawingProperties = new P.NonVisualPictureDrawingProperties();
        var appNonVisualDrawingProperties = new P.ApplicationNonVisualDrawingProperties();

        A.NonVisualDrawingPropertiesExtensionList aNonVisualDrawingPropertiesExtensionList =
            new A.NonVisualDrawingPropertiesExtensionList();

        A.NonVisualDrawingPropertiesExtension aNonVisualDrawingPropertiesExtension =
            new A.NonVisualDrawingPropertiesExtension { Uri = "{FF2B5EF4-FFF2-40B4-BE49-F238E27FC236}" };

        A16.CreationId a16CreationId = new A16.CreationId();

        // "http://schemas.microsoft.com/office/drawing/2014/main"
        var a16 = DocumentFormat.OpenXml.Linq.A16.a16;
        a16CreationId.AddNamespaceDeclaration(nameof(a16), a16.NamespaceName);

        a16CreationId.Id = "{2BEA8DB4-11C1-B7BA-06ED-DC504E2BBEBE}";

        aNonVisualDrawingPropertiesExtension.AppendChild(a16CreationId);

        aNonVisualDrawingPropertiesExtensionList.AppendChild(aNonVisualDrawingPropertiesExtension);

        nonVisualDrawingProperties.AppendChild(aNonVisualDrawingPropertiesExtensionList);
        nonVisualPictureProperties.AppendChild(nonVisualDrawingProperties);
        nonVisualPictureProperties.AppendChild(nonVisualPictureDrawingProperties);
        nonVisualPictureProperties.AppendChild(appNonVisualDrawingProperties);

        var blipFill = new P.BlipFill();

        A.Blip aBlip = new A.Blip() { Embed = imgPartRId };

        A.BlipExtensionList aBlipExtensionList = new A.BlipExtensionList();

        A.BlipExtension aBlipExtension = new A.BlipExtension { Uri = "{28A0092B-C50C-407E-A947-70E740481C1C}" };

        A14.UseLocalDpi a14UseLocalDpi = new A14.UseLocalDpi();

        // "http://schemas.microsoft.com/office/drawing/2010/main"
        var a14 = DocumentFormat.OpenXml.Linq.A14.a14;

        a14UseLocalDpi.AddNamespaceDeclaration(nameof(a14), a14.NamespaceName);

        a14UseLocalDpi.Val = false;

        aBlipExtension.AppendChild(a14UseLocalDpi);

        aBlipExtensionList.AppendChild(aBlipExtension);

        aBlipExtension = new A.BlipExtension { Uri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}" };

        var svgBlip = new DocumentFormat.OpenXml.Office2019.Drawing.SVG.SVGBlip() { Embed = svgPartRId };

        // "http://schemas.microsoft.com/office/drawing/2016/SVG/main"
        var asvg = DocumentFormat.OpenXml.Linq.ASVG.asvg;

        svgBlip.AddNamespaceDeclaration(nameof(asvg), asvg.NamespaceName);

        aBlipExtension.AppendChild(svgBlip);

        aBlipExtensionList.AppendChild(aBlipExtension);

        aBlip.AppendChild(aBlipExtensionList);

        blipFill.AppendChild(aBlip);

        A.Stretch aStretch = new A.Stretch();

        A.FillRectangle aFillRectangle = new A.FillRectangle();

        aStretch.AppendChild(aFillRectangle);

        blipFill.AppendChild(aStretch);

        var transform2D = new A.Transform2D(
            new A.Offset { X = 0, Y = 0 },
            new A.Extents { Cx = 0, Cy = 0 });

        var presetGeometry = new A.PresetGeometry
        { Preset = A.ShapeTypeValues.Rectangle };

        A.AdjustValueList aAdjustValueList = new A.AdjustValueList();

        presetGeometry.AppendChild(aAdjustValueList);

        var shapeProperties = new P.ShapeProperties();
        shapeProperties.AppendChild(transform2D);
        shapeProperties.AppendChild(presetGeometry);

        var pPicture = new P.Picture();
        pPicture.AppendChild(nonVisualPictureProperties);
        pPicture.AppendChild(blipFill);
        pPicture.AppendChild(shapeProperties);

        this.sdkSlidePart.Slide.CommonSlideData!.ShapeTree!.AppendChild(pPicture);

        return pPicture;
    }

    private int NextShapeId()
    {
        if (this.shapes.Any())
        {
            return this.shapes.Select(shape => shape.Id).Prepend(0).Max() + 1;
        }

        return 1;
    }
}