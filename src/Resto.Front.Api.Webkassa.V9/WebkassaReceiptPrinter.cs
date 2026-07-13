using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Resto.Front.Api;
using Resto.Front.Api.Data.Cheques;
using Resto.Front.Api.Data.Print;

namespace Resto.Front.Api.Webkassa.V9;

public static class WebkassaReceiptPrinter
{
    private const string WindowsPdfPrinterName = "Microsoft Print to PDF";
    private const string WindowsPdfOutputDirectory = @"C:\OpenClaw\logs\webkassa-receipts";

    public static void Print(IOperationService operationService, SidecarTicketRecord record, string? orderNumber = null)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(record.ExternalCheckNumber))
            throw new InvalidOperationException("Webkassa ticket has no ExternalCheckNumber for Ticket/PrintFormat.");

        var printing = LoadPrintingOptions();
        var printFormat = LoadTicketPrintFormat(record.ExternalCheckNumber!);
        if (printFormat.Lines.Count == 0)
            throw new InvalidOperationException("Webkassa Ticket/PrintFormat returned no printable lines.");

        PrintLines(operationService, record, printFormat.Lines, printing);
    }

    public static void PrintOfflineQueuedNotice(IOperationService operationService, SidecarFiscalizationResult result, string? orderNumber = null)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        var printing = LoadPrintingOptions();
        var record = new SidecarTicketRecord
        {
            ExternalCheckNumber = result.ExternalCheckNumber,
            CheckNumber = result.ExternalCheckNumber,
        };
        var lines = BuildOfflineQueuedNoticeLines(result, orderNumber);
        PrintLines(operationService, record, lines, printing);
    }

    public static void PrintReport(IOperationService operationService, SidecarReportResult result)
    {
        if (operationService == null)
            throw new ArgumentNullException(nameof(operationService));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (result.PrintLines == null || result.PrintLines.Count == 0)
            throw new InvalidOperationException("Webkassa report returned no printable lines.");

        var printing = LoadPrintingOptions();
        var reportType = string.Equals(result.ReportType, "z", StringComparison.OrdinalIgnoreCase) ? "z-report" : "x-report";
        var id = $"{reportType}-{result.ReportNumber?.ToString() ?? DateTime.Now.ToString("yyyyMMdd-HHmmss")}";
        var record = new SidecarTicketRecord
        {
            Operation = reportType,
            ExternalCheckNumber = id,
            CheckNumber = id,
            ShiftNumber = result.ShiftNumber,
            CashboxRegistrationNumber = result.CashboxRegistrationNumber,
        };

        PrintLines(operationService, record, result.PrintLines, printing);
    }

    public static SidecarTicketRecord FromFiscalizationResult(SidecarFiscalizationResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        return new SidecarTicketRecord
        {
            Operation = result.Operation,
            Status = result.Status,
            ExternalCheckNumber = result.ExternalCheckNumber,
            OriginalSaleExternalCheckNumber = result.OriginalSaleExternalCheckNumber,
            CheckNumber = result.CheckNumber,
            ShiftNumber = result.ShiftNumber,
            DateTime = result.DateTime,
            CashboxRegistrationNumber = result.CashboxRegistrationNumber,
            TicketUrl = result.TicketUrl,
            TicketPrintUrl = result.TicketPrintUrl,
            Total = result.Total,
        };
    }

    private static void PrintLines(
        IOperationService operationService,
        SidecarTicketRecord record,
        IList<SidecarTicketPrintLine> lines,
        AdapterPrintingOptions printing)
    {
        var document = (Document)BuildMarkup(lines);
        if (ShouldTryIikoPrinter(printing) && TryPrintWithIikoReceiptPrinter(operationService, document))
            return;

        if (string.Equals(printing.Mode, AdapterPrintingOptions.IikoReceiptPrinterOnlyMode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("iikoFront receipt cheque printer is not configured.");

        var target = PrintWithWindowsPrinter(record, lines, ResolveWindowsPrinterName(printing), printing);
        PluginContext.Log.Info($"Webkassa receipt printed through Windows printer. Target={target}");
    }

    private static SidecarTicketPrintFormatResult LoadTicketPrintFormat(string externalCheckNumber)
    {
        var configuration = AdapterConfigurationLoader.LoadFromDefaultLocation();
        using (var sidecar = new SidecarClient(configuration))
        {
            return sidecar.GetTicketPrintFormat(externalCheckNumber);
        }
    }

    private static XElement BuildMarkup(IEnumerable<SidecarTicketPrintLine> lines)
    {
        var doc = new XElement(Tags.Doc);
        foreach (var line in lines.OrderBy(item => item.Order))
        {
            if (line.Type == 2)
                doc.Add(new XElement(Tags.QRCode, line.Value));
            else if (line.Type == 1)
                doc.Add(new XElement(Tags.Center, "[image]"));
            else
                doc.Add(new XElement(Tags.Line, line.Value ?? string.Empty));
        }

        doc.Add(new XElement(Tags.Br));
        return doc;
    }

    private static bool TryPrintWithIikoReceiptPrinter(IOperationService operationService, Document document)
    {
        try
        {
            var printer = operationService.TryGetReceiptChequePrinter(checkIsConfigured: false);
            if (printer == null)
            {
                PluginContext.Log.Warn("iikoFront receipt cheque printer is not configured; falling back to Microsoft Print to PDF.");
                return false;
            }

            var printed = operationService.Print(printer, document, checkIsCompleted: true);
            if (!printed)
            {
                PluginContext.Log.Warn("iikoFront did not confirm receipt print completion; falling back to Microsoft Print to PDF.");
                return false;
            }

            return true;
        }
        catch (Exception error)
        {
            if (!IsReceiptPrinterNotConfigured(error))
                throw;

            PluginContext.Log.Warn($"iikoFront receipt cheque printer is unavailable; falling back to Microsoft Print to PDF. Error={error.Message}");
            return false;
        }
    }

    private static AdapterPrintingOptions LoadPrintingOptions()
    {
        try
        {
            return AdapterConfigurationLoader.LoadFromDefaultLocation().Printing ?? new AdapterPrintingOptions();
        }
        catch
        {
            return new AdapterPrintingOptions();
        }
    }

    private static bool ShouldTryIikoPrinter(AdapterPrintingOptions printing)
    {
        return string.Equals(printing.Mode, AdapterPrintingOptions.IikoReceiptPrinterWithWindowsFallbackMode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(printing.Mode, AdapterPrintingOptions.IikoReceiptPrinterOnlyMode, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWindowsPrinterName(AdapterPrintingOptions printing)
    {
        if (string.Equals(printing.Mode, AdapterPrintingOptions.WindowsPrinterMode, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(printing.PreferredWindowsPrinterName))
            return printing.PreferredWindowsPrinterName;

        if (!string.IsNullOrWhiteSpace(printing.FallbackWindowsPrinterName))
            return printing.FallbackWindowsPrinterName;

        return WindowsPdfPrinterName;
    }

    private static string PrintWithWindowsPrinter(
        SidecarTicketRecord record,
        IEnumerable<SidecarTicketPrintLine> lines,
        string printerName,
        AdapterPrintingOptions printing)
    {
        if (!HasInstalledPrinter(printerName))
            throw new InvalidOperationException($"{printerName} is not installed in Windows.");

        var printToPdf = IsPdfPrinter(printerName);
        var pdfPath = string.Empty;
        if (printToPdf)
        {
            var outputDirectory = FirstNonEmpty(printing.PdfOutputDirectory, WindowsPdfOutputDirectory)!;
            Directory.CreateDirectory(outputDirectory);
            pdfPath = Path.Combine(outputDirectory, BuildPdfFileName(record));
        }

        var printLines = lines.OrderBy(line => line.Order).ToArray();
        using (var printDocument = new PrintDocument())
        {
            printDocument.DocumentName = $"Webkassa {FirstNonEmpty(record.CheckNumber, record.ExternalCheckNumber, "receipt")}";
            printDocument.PrinterSettings.PrinterName = printerName;
            if (printToPdf)
            {
                printDocument.PrinterSettings.PrintToFile = true;
                printDocument.PrinterSettings.PrintFileName = pdfPath;
            }
            printDocument.DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);
            printDocument.DefaultPageSettings.PaperSize = new PaperSize("WebkassaReceipt", 315, 1200);

            printDocument.PrintPage += (_, args) =>
            {
                using (var font = new Font("Consolas", 8, FontStyle.Regular))
                using (var boldFont = new Font("Consolas", 8, FontStyle.Bold))
                {
                    float y = args.MarginBounds.Top;
                    foreach (var line in printLines)
                    {
                        if (line.Type == 2)
                        {
                            y = DrawQrCode(args.Graphics, line.Value ?? string.Empty, args.MarginBounds.Left, y, args.MarginBounds.Width);
                            continue;
                        }

                        if (line.Type == 1)
                        {
                            y = DrawBase64Image(args.Graphics, line.Value ?? string.Empty, args.MarginBounds.Left, y, args.MarginBounds.Width);
                            continue;
                        }

                        args.Graphics.DrawString(line.Value ?? string.Empty, line.Style == 1 ? boldFont : font, Brushes.Black, args.MarginBounds.Left, y);
                        y += 15;
                    }
                }

                args.HasMorePages = false;
            };

            printDocument.Print();
        }

        return printToPdf ? pdfPath : printerName;
    }

    private static float DrawBase64Image(Graphics graphics, string value, float x, float y, int maxWidth)
    {
        try
        {
            var commaIndex = value.IndexOf(',');
            var base64 = commaIndex >= 0 ? value.Substring(commaIndex + 1) : value;
            var bytes = Convert.FromBase64String(base64);
            using (var stream = new MemoryStream(bytes))
            using (var image = Image.FromStream(stream))
            {
                var width = Math.Min(maxWidth, image.Width);
                var height = image.Height * width / image.Width;
                graphics.DrawImage(image, x, y, width, height);
                return y + height + 8;
            }
        }
        catch
        {
            using (var font = new Font("Consolas", 8, FontStyle.Regular))
            {
                graphics.DrawString("[image]", font, Brushes.Black, x, y);
            }
            return y + 15;
        }
    }

    private static float DrawQrCode(Graphics graphics, string value, float x, float y, int maxWidth)
    {
        try
        {
            var targetPixels = Math.Min(Math.Max(maxWidth, 120), 220);
            using (var image = QrCodeRenderer.Render(value, targetPixels))
            {
                graphics.DrawImage(image, x, y, image.Width, image.Height);
                return y + image.Height + 8;
            }
        }
        catch
        {
            using (var font = new Font("Consolas", 8, FontStyle.Regular))
            {
                graphics.DrawString("[qr unavailable]", font, Brushes.Black, x, y);
            }
            return y + 15;
        }
    }

    private static SidecarTicketPrintLine[] BuildOfflineQueuedNoticeLines(SidecarFiscalizationResult result, string? orderNumber)
    {
        return new[]
        {
            new SidecarTicketPrintLine { Order = 1, Type = 0, Style = 1, Value = "WEBKASSA" },
            new SidecarTicketPrintLine { Order = 2, Type = 0, Style = 1, Value = "НЕ ФИСКАЛЬНЫЙ ЧЕК" },
            new SidecarTicketPrintLine { Order = 3, Type = 0, Style = 0, Value = "Ожидает синхронизации с Webkassa" },
            new SidecarTicketPrintLine { Order = 4, Type = 0, Style = 0, Value = $"Заказ iiko: {FirstNonEmpty(orderNumber, "-")}" },
            new SidecarTicketPrintLine { Order = 5, Type = 0, Style = 0, Value = $"External: {FirstNonEmpty(result.ExternalCheckNumber, "-")}" },
            new SidecarTicketPrintLine { Order = 6, Type = 0, Style = 0, Value = $"Срок offline: {FirstNonEmpty(result.OfflineExpiresAt, "-")}" },
            new SidecarTicketPrintLine { Order = 7, Type = 0, Style = 0, Value = "Фискальный чек напечатайте после sync." },
        };
    }

    private static string BuildPdfFileName(SidecarTicketRecord record)
    {
        var id = FirstNonEmpty(record.CheckNumber, record.ExternalCheckNumber, "receipt")!;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            id = id.Replace(invalid, '_');

        return $"webkassa-{id}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
    }

    private static bool HasInstalledPrinter(string printerName)
    {
        foreach (string installed in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(installed, printerName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsPdfPrinter(string printerName)
    {
        return string.Equals(printerName, WindowsPdfPrinterName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReceiptPrinterNotConfigured(Exception error)
    {
        return error.Message.IndexOf("Receipt cheque printer is not configured", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.Message.IndexOf("receipt printer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
