using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace Webkassa.IikoFrontAdapter.Spike;

public sealed class SidecarClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private bool disposed;

    public SidecarClient(AdapterConfiguration configuration)
        : this(configuration, new HttpClient(), ownsHttpClient: true)
    {
    }

    public SidecarClient(AdapterConfiguration configuration, HttpClient httpClient, bool ownsHttpClient = false)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;

        httpClient.Timeout = TimeSpan.FromMilliseconds(Configuration.Sidecar.TimeoutMs);
    }

    public AdapterConfiguration Configuration { get; }

    public SidecarStatus GetStatus()
    {
        return Send<object, SidecarStatus>(HttpMethod.Get, "/status", null);
    }

    public SidecarLicenseStatusResult GetLicenseStatus()
    {
        return Send<object, SidecarLicenseStatusResult>(HttpMethod.Get, "/license/status", null);
    }

    public SidecarFiscalizationResult FiscalizeSale(IikoChequeDraft draft)
    {
        return Fiscalize("/fiscalize/sale", draft, null);
    }

    public SidecarFiscalizationResult FiscalizeReturn(IikoChequeDraft draft, string? originalSaleExternalCheckNumber)
    {
        return Fiscalize("/fiscalize/return", draft, originalSaleExternalCheckNumber);
    }

    public SidecarReportResult RunXReport()
    {
        return RunReport("/reports/x");
    }

    public SidecarReportResult RunZReport()
    {
        return RunReport("/reports/z");
    }

    public SidecarTicketLookupResult FindTicketsByOrderId(string iikoOrderId)
    {
        if (string.IsNullOrWhiteSpace(iikoOrderId))
            throw new ArgumentException("iikoOrderId is required.", nameof(iikoOrderId));

        var request = new SidecarTicketLookupRequest
        {
            IikoOrderId = iikoOrderId,
            Runtime = new SidecarRuntime
            {
                Environment = Configuration.Environment,
                CompanyId = Configuration.CompanyProfile,
                CashboxUniqueNumber = Configuration.CashboxUniqueNumber,
            }
        };

        return Send<SidecarTicketLookupRequest, SidecarTicketLookupResult>(HttpMethod.Post, "/tickets/by-order", request);
    }

    public SidecarTicketPrintFormatResult GetTicketPrintFormat(string externalCheckNumber)
    {
        if (string.IsNullOrWhiteSpace(externalCheckNumber))
            throw new ArgumentException("externalCheckNumber is required.", nameof(externalCheckNumber));

        var printing = Configuration.Printing ?? new AdapterPrintingOptions();
        var request = new SidecarTicketPrintFormatRequest
        {
            ExternalCheckNumber = externalCheckNumber,
            Runtime = new SidecarRuntime
            {
                Environment = Configuration.Environment,
                CompanyId = Configuration.CompanyProfile,
                CashboxUniqueNumber = Configuration.CashboxUniqueNumber,
                PaperKind = printing.PaperKind,
                AcceptLanguage = printing.AcceptLanguage,
            }
        };

        return Send<SidecarTicketPrintFormatRequest, SidecarTicketPrintFormatResult>(HttpMethod.Post, "/tickets/print-format", request);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private SidecarFiscalizationResult Fiscalize(string path, IikoChequeDraft draft, string? originalSaleExternalCheckNumber)
    {
        if (draft == null)
            throw new ArgumentNullException(nameof(draft));

        var request = new SidecarFiscalizationRequest
        {
            Draft = draft,
            Runtime = new SidecarRuntime
            {
                Environment = Configuration.Environment,
                CompanyId = Configuration.CompanyProfile,
                CashboxUniqueNumber = Configuration.CashboxUniqueNumber,
                AllowOffline = Configuration.Offline != null && Configuration.Offline.Enabled,
                OriginalSaleExternalCheckNumber = originalSaleExternalCheckNumber,
            }
        };

        return Send<SidecarFiscalizationRequest, SidecarFiscalizationResult>(HttpMethod.Post, path, request);
    }

    private SidecarReportResult RunReport(string path)
    {
        var request = new SidecarReportRequest
        {
            Runtime = new SidecarRuntime
            {
                Environment = Configuration.Environment,
                CompanyId = Configuration.CompanyProfile,
                CashboxUniqueNumber = Configuration.CashboxUniqueNumber,
            }
        };

        return Send<SidecarReportRequest, SidecarReportResult>(HttpMethod.Post, path, request);
    }

    private TResponse Send<TRequest, TResponse>(HttpMethod method, string path, TRequest? request)
    {
        var url = $"{Configuration.Sidecar.BaseUrl.TrimEnd('/')}{path}";
        using (var message = new HttpRequestMessage(method, url))
        {
            if (!Equals(request, null))
            {
                var body = SerializeJson(request);
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            string responseText;
            try
            {
                response = httpClient.SendAsync(message).GetAwaiter().GetResult();
                responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (TaskCanceledException error)
            {
                throw new SidecarException($"Sidecar request timed out after {Configuration.Sidecar.TimeoutMs} ms.", error);
            }
            catch (Exception error) when (error is HttpRequestException || error is WebException || error is IOException)
            {
                throw new SidecarException($"Sidecar request failed: {error.Message}", error);
            }

            if (!response.IsSuccessStatusCode)
            {
                var envelope = DeserializeErrorEnvelopeBestEffort(responseText);
                var messageText = FirstNonEmpty(
                    envelope?.OperatorDiagnostic?.Title,
                    envelope?.Error,
                    $"Sidecar returned HTTP {(int)response.StatusCode}: {SafeMessage(responseText)}");
                throw new SidecarException(messageText, envelope?.OperatorDiagnostic);
            }

            var value = DeserializeJson<TResponse>(responseText);
            if (value is ISidecarResponse sidecarResponse && !sidecarResponse.Ok)
                throw new SidecarException(sidecarResponse.Error ?? "Sidecar returned an unsuccessful response.");

            return value;
        }
    }

    private static string SerializeJson<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static T DeserializeJson<T>(string json)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            var value = serializer.ReadObject(stream);
            if (value == null)
                throw new SidecarException("Sidecar returned empty JSON.");
            return (T)value;
        }
    }

    private static string SafeMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty response)";

        return value.Length <= 500 ? value : $"{value.Substring(0, 500)}...";
    }

    private static SidecarErrorEnvelope? DeserializeErrorEnvelopeBestEffort(string json)
    {
        try
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(json) ? "{}" : json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(SidecarErrorEnvelope));
                return serializer.ReadObject(stream) as SidecarErrorEnvelope;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return string.Empty;
    }
}

public sealed class SidecarException : Exception
{
    public SidecarException(string message)
        : base(message)
    {
    }

    public SidecarException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SidecarException(string message, SidecarOperatorDiagnostic? operatorDiagnostic)
        : base(message)
    {
        OperatorDiagnostic = operatorDiagnostic;
    }

    public SidecarOperatorDiagnostic? OperatorDiagnostic { get; }
}

public interface ISidecarResponse
{
    bool Ok { get; }

    string? Error { get; }
}

[DataContract]
public sealed class SidecarErrorEnvelope
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "status")]
    public string? Status { get; set; }

    [DataMember(Name = "error")]
    public string? Error { get; set; }

    [DataMember(Name = "operatorDiagnostic")]
    public SidecarOperatorDiagnostic? OperatorDiagnostic { get; set; }
}

[DataContract]
public sealed class SidecarOperatorDiagnostic
{
    [DataMember(Name = "code")]
    public string? Code { get; set; }

    [DataMember(Name = "title")]
    public string? Title { get; set; }

    [DataMember(Name = "operatorMessage")]
    public string? OperatorMessage { get; set; }

    [DataMember(Name = "nextAction")]
    public string? NextAction { get; set; }

    [DataMember(Name = "severity")]
    public string? Severity { get; set; }

    [DataMember(Name = "externalCheckNumber")]
    public string? ExternalCheckNumber { get; set; }

    [DataMember(Name = "orderId")]
    public string? OrderId { get; set; }

    [DataMember(Name = "webkassaCode")]
    public string? WebkassaCode { get; set; }

    [DataMember(Name = "webkassaText")]
    public string? WebkassaText { get; set; }

    [DataMember(Name = "endpoint")]
    public string? Endpoint { get; set; }

    [DataMember(Name = "httpStatus")]
    public int? HttpStatus { get; set; }

    [DataMember(Name = "technicalMessage")]
    public string? TechnicalMessage { get; set; }
}

[DataContract]
public sealed class SidecarFiscalizationRequest
{
    [DataMember(Name = "draft")]
    public IikoChequeDraft Draft { get; set; } = new IikoChequeDraft();

    [DataMember(Name = "runtime")]
    public SidecarRuntime Runtime { get; set; } = new SidecarRuntime();
}

[DataContract]
public sealed class SidecarReportRequest
{
    [DataMember(Name = "runtime")]
    public SidecarRuntime Runtime { get; set; } = new SidecarRuntime();
}

[DataContract]
public sealed class SidecarTicketLookupRequest
{
    [DataMember(Name = "iikoOrderId")]
    public string IikoOrderId { get; set; } = string.Empty;

    [DataMember(Name = "runtime")]
    public SidecarRuntime Runtime { get; set; } = new SidecarRuntime();
}

[DataContract]
public sealed class SidecarRuntime
{
    [DataMember(Name = "environment")]
    public string Environment { get; set; } = string.Empty;

    [DataMember(Name = "companyId")]
    public string CompanyId { get; set; } = string.Empty;

    [DataMember(Name = "cashboxUniqueNumber")]
    public string CashboxUniqueNumber { get; set; } = string.Empty;

    [DataMember(Name = "originalSaleExternalCheckNumber")]
    public string? OriginalSaleExternalCheckNumber { get; set; }

    [DataMember(Name = "allowOffline")]
    public bool AllowOffline { get; set; }

    [DataMember(Name = "paperKind")]
    public int? PaperKind { get; set; }

    [DataMember(Name = "acceptLanguage")]
    public string? AcceptLanguage { get; set; }
}

[DataContract]
public sealed class SidecarTicketPrintFormatRequest
{
    [DataMember(Name = "externalCheckNumber")]
    public string ExternalCheckNumber { get; set; } = string.Empty;

    [DataMember(Name = "runtime")]
    public SidecarRuntime Runtime { get; set; } = new SidecarRuntime();
}

[DataContract]
public sealed class SidecarFiscalizationResult : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "queuedOffline")]
    public bool QueuedOffline { get; set; }

    [DataMember(Name = "externalCheckNumber")]
    public string? ExternalCheckNumber { get; set; }

    [DataMember(Name = "checkNumber")]
    public string? CheckNumber { get; set; }

    [DataMember(Name = "shiftNumber")]
    public int? ShiftNumber { get; set; }

    [DataMember(Name = "operation")]
    public string? Operation { get; set; }

    [DataMember(Name = "originalSaleExternalCheckNumber")]
    public string? OriginalSaleExternalCheckNumber { get; set; }

    [DataMember(Name = "dateTime")]
    public string? DateTime { get; set; }

    [DataMember(Name = "cashboxRegistrationNumber")]
    public string? CashboxRegistrationNumber { get; set; }

    [DataMember(Name = "ticketUrl")]
    public string? TicketUrl { get; set; }

    [DataMember(Name = "ticketPrintUrl")]
    public string? TicketPrintUrl { get; set; }

    [DataMember(Name = "total")]
    public decimal? Total { get; set; }

    [DataMember(Name = "offlineExpiresAt")]
    public string? OfflineExpiresAt { get; set; }

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}

[DataContract]
public sealed class SidecarTicketLookupResult : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "records")]
    public List<SidecarTicketRecord> Records { get; set; } = new List<SidecarTicketRecord>();

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}

[DataContract]
public sealed class SidecarTicketPrintFormatResult : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "externalCheckNumber")]
    public string? ExternalCheckNumber { get; set; }

    [DataMember(Name = "lines")]
    public List<SidecarTicketPrintLine> Lines { get; set; } = new List<SidecarTicketPrintLine>();

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}

[DataContract]
public sealed class SidecarTicketPrintLine
{
    [DataMember(Name = "order")]
    public int Order { get; set; }

    [DataMember(Name = "type")]
    public int Type { get; set; }

    [DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;

    [DataMember(Name = "style")]
    public int Style { get; set; }
}

[DataContract]
public sealed class SidecarTicketRecord
{
    [DataMember(Name = "operation")]
    public string? Operation { get; set; }

    [DataMember(Name = "status")]
    public string? Status { get; set; }

    [DataMember(Name = "externalCheckNumber")]
    public string? ExternalCheckNumber { get; set; }

    [DataMember(Name = "originalSaleExternalCheckNumber")]
    public string? OriginalSaleExternalCheckNumber { get; set; }

    [DataMember(Name = "checkNumber")]
    public string? CheckNumber { get; set; }

    [DataMember(Name = "shiftNumber")]
    public int? ShiftNumber { get; set; }

    [DataMember(Name = "dateTime")]
    public string? DateTime { get; set; }

    [DataMember(Name = "cashboxRegistrationNumber")]
    public string? CashboxRegistrationNumber { get; set; }

    [DataMember(Name = "ticketUrl")]
    public string? TicketUrl { get; set; }

    [DataMember(Name = "ticketPrintUrl")]
    public string? TicketPrintUrl { get; set; }

    [DataMember(Name = "total")]
    public decimal? Total { get; set; }
}

[DataContract]
public sealed class SidecarReportResult : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "reportType")]
    public string ReportType { get; set; } = string.Empty;

    [DataMember(Name = "reportNumber")]
    public int? ReportNumber { get; set; }

    [DataMember(Name = "shiftNumber")]
    public int? ShiftNumber { get; set; }

    [DataMember(Name = "documentCount")]
    public int? DocumentCount { get; set; }

    [DataMember(Name = "cashboxUniqueNumber")]
    public string? CashboxUniqueNumber { get; set; }

    [DataMember(Name = "cashboxRegistrationNumber")]
    public string? CashboxRegistrationNumber { get; set; }

    [DataMember(Name = "taxpayerName")]
    public string? TaxpayerName { get; set; }

    [DataMember(Name = "taxpayerIn")]
    public string? TaxpayerIn { get; set; }

    [DataMember(Name = "cashboxAddress")]
    public string? CashboxAddress { get; set; }

    [DataMember(Name = "startOn")]
    public string? StartOn { get; set; }

    [DataMember(Name = "reportOn")]
    public string? ReportOn { get; set; }

    [DataMember(Name = "closeOn")]
    public string? CloseOn { get; set; }

    [DataMember(Name = "cashierName")]
    public string? CashierName { get; set; }

    [DataMember(Name = "putMoneySum")]
    public decimal? PutMoneySum { get; set; }

    [DataMember(Name = "takeMoneySum")]
    public decimal? TakeMoneySum { get; set; }

    [DataMember(Name = "sumInCashbox")]
    public decimal? SumInCashbox { get; set; }

    [DataMember(Name = "controlSum")]
    public long? ControlSum { get; set; }

    [DataMember(Name = "ofdName")]
    public string? OfdName { get; set; }

    [DataMember(Name = "printLines")]
    public List<SidecarTicketPrintLine> PrintLines { get; set; } = new List<SidecarTicketPrintLine>();

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}

[DataContract]
public sealed class SidecarStatus : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "version")]
    public string Version { get; set; } = string.Empty;

    [DataMember(Name = "protocolVersion")]
    public string ProtocolVersion { get; set; } = string.Empty;

    [DataMember(Name = "writeFiscalData")]
    public bool WriteFiscalData { get; set; }

    [DataMember(Name = "offlineAutonomousHours")]
    public int OfflineAutonomousHours { get; set; }

    [DataMember(Name = "webNktSupported")]
    public bool WebNktSupported { get; set; }

    [DataMember(Name = "fiscalServiceConfigured")]
    public bool FiscalServiceConfigured { get; set; }

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}

[DataContract]
public sealed class SidecarLicenseStatusResult : ISidecarResponse
{
    [DataMember(Name = "ok")]
    public bool Ok { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [DataMember(Name = "warningDays")]
    public int WarningDays { get; set; }

    [DataMember(Name = "cashboxStatus")]
    public int? CashboxStatus { get; set; }

    [DataMember(Name = "licenseStatus")]
    public int? LicenseStatus { get; set; }

    [DataMember(Name = "licenseExpirationDate")]
    public string? LicenseExpirationDate { get; set; }

    [DataMember(Name = "licenseDaysRemaining")]
    public int? LicenseDaysRemaining { get; set; }

    [DataMember(Name = "licenseExpired")]
    public bool LicenseExpired { get; set; }

    [DataMember(Name = "licenseWarning")]
    public bool LicenseWarning { get; set; }

    [DataMember(Name = "ofdExpirationDate")]
    public string? OfdExpirationDate { get; set; }

    [DataMember(Name = "ofdDaysRemaining")]
    public int? OfdDaysRemaining { get; set; }

    [DataMember(Name = "ofdExpired")]
    public bool OfdExpired { get; set; }

    [DataMember(Name = "ofdWarning")]
    public bool OfdWarning { get; set; }

    [DataMember(Name = "message")]
    public string? Message { get; set; }

    [DataMember(Name = "error")]
    public string? Error { get; set; }
}
