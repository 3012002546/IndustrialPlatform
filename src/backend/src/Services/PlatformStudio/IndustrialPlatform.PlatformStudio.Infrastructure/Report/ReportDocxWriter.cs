using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using IndustrialPlatform.PlatformStudio.Domain;
using W = DocumentFormat.OpenXml.Wordprocessing;
using DrawingMl = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>Writes the frozen document model without decoding images or querying report data.</summary>
internal static class ReportDocxWriter
{
    public static void Write(string path, IEnumerable<JsonObject> blocks, CancellationToken token)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new W.Body();
        main.Document = new W.Document(body);
        uint imageId = 0;
        foreach (var block in blocks)
        {
            token.ThrowIfCancellationRequested();
            switch (StudioJson.Text(block["type"]))
            {
                case "paragraph":
                    var paragraph = Paragraph(StudioJson.Text(block["text"]) ?? "", block["bold"]?.GetValue<bool>() == true);
                    if (block["heading"]?.GetValue<bool>() == true)
                        paragraph.PrependChild(new W.ParagraphProperties(new W.KeepNext()));
                    body.Append(paragraph);
                    break;
                case "table":
                    var rows = block["rows"]!.AsArray().OfType<JsonArray>().ToArray();
                    if (rows.Length == 0) break;
                    // Word merges adjacent same-style tables unless a paragraph separates them.
                    if (body.LastChild is W.Table)
                        body.Append(new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines
                        { Before = "0", After = "0", Line = "20", LineRule = W.LineSpacingRuleValues.Exact })));
                    var columns = Math.Max(1, rows.Max(row => row.Count));
                    var table = new W.Table(new W.TableProperties(
                        new W.TableWidth { Width = "9000", Type = W.TableWidthUnitValues.Dxa },
                        new W.TableBorders(
                            new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
                            new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                            new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                            new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                            new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                            new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 })),
                        new W.TableGrid(Enumerable.Range(0, columns).Select(_ =>
                            new W.GridColumn { Width = (9000 / columns).ToString(System.Globalization.CultureInfo.InvariantCulture) })));
                    for (var r = 0; r < rows.Length; r++)
                    {
                        token.ThrowIfCancellationRequested();
                        var row = new W.TableRow();
                        if (r < (block["headerRows"]?.GetValue<int>() ?? 0))
                            row.Append(new W.TableRowProperties(new W.TableHeader()));
                        for (var c = 0; c < columns; c++)
                            row.Append(new W.TableCell(Paragraph(c < rows[r].Count ? StudioJson.Text(rows[r][c]) ?? "" : "")));
                        table.Append(row);
                    }
                    body.Append(table);
                    break;
                case "image":
                    var image = ReportExporter.Image(block);
                    var part = main.AddImagePart(ImagePartType.Png);
                    using (var input = new MemoryStream(image.Bytes)) part.FeedData(input);
                    var width = (long)Math.Round(image.Width * 36000);
                    var height = (long)Math.Round(image.Height * 36000);
                    var id = ++imageId;
                    var picture = new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = id, Name = $"report-{id}.png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(new DrawingMl.Blip { Embed = main.GetIdOfPart(part) },
                            new DrawingMl.Stretch(new DrawingMl.FillRectangle())),
                        new PIC.ShapeProperties(
                            new DrawingMl.Transform2D(new DrawingMl.Offset { X = 0, Y = 0 }, new DrawingMl.Extents { Cx = width, Cy = height }),
                            new DrawingMl.PresetGeometry(new DrawingMl.AdjustValueList()) { Preset = DrawingMl.ShapeTypeValues.Rectangle }));
                    body.Append(new W.Paragraph(new W.Run(new W.Drawing(new DW.Inline(
                        new DW.Extent { Cx = width, Cy = height },
                        new DW.DocProperties { Id = id, Name = $"report-{id}.png" },
                        new DW.NonVisualGraphicFrameDrawingProperties(new DrawingMl.GraphicFrameLocks { NoChangeAspect = true }),
                        new DrawingMl.Graphic(new DrawingMl.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
                        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 }))));
                    break;
                default:
                    throw new StudioException("REPORT_OUTPUT_INVALID", "未知文档正文类型。", 422);
            }
        }
        main.Document.Save();
    }

    private static W.Paragraph Paragraph(string text, bool bold = false)
    {
        var properties = new W.RunProperties(
            new W.RunFonts { Ascii = "Microsoft YaHei", HighAnsi = "Microsoft YaHei", EastAsia = "Microsoft YaHei" });
        if (bold) properties.Append(new W.Bold());
        properties.Append(new W.FontSize { Val = "20" });
        var run = new W.Run(properties);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new W.Break());
            var tabs = lines[i].Split('\t');
            for (var j = 0; j < tabs.Length; j++)
            {
                if (j > 0) run.Append(new W.TabChar());
                run.Append(new W.Text(tabs[j]) { Space = SpaceProcessingModeValues.Preserve });
            }
        }
        return new W.Paragraph(run);
    }
}
