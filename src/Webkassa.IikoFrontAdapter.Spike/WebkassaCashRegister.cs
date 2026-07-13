using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Resto.Front.Api;
using Resto.Front.Api.Data.Device;
using Resto.Front.Api.Data.Device.Results;
using Resto.Front.Api.Data.Device.Settings;
using Resto.Front.Api.Data.Device.Tasks;
using Resto.Front.Api.Data.Security;
using Resto.Front.Api.Devices;
using Resto.Front.Api.Exceptions;
using Resto.Front.Api.UI;

namespace Webkassa.IikoFrontAdapter.Spike;

public sealed class WebkassaCashRegister : MarshalByRefObject, ICashRegister
{
    private CashRegisterSettings settings;
    private State state = State.Stopped;
    private bool cashSessionOpen;
    private int cashSessionNumber;
    private decimal cashSum;
    private decimal totalIncomeSum;
    private decimal salesSum;
    private decimal nonCashPaymentSum;
    private int salesCount;
    private int saleNumber;
    private string fiscalizationStatusText = "dry-run fiscalization is enabled";
    private SidecarLicenseStatusResult? cachedLicenseStatus;
    private DateTime lastLicenseStatusCheckUtc = DateTime.MinValue;
    private DateTime lastLicenseWarningPopupUtc = DateTime.MinValue;

    public WebkassaCashRegister(Guid deviceId, CashRegisterSettings settings)
    {
        DeviceId = deviceId;
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Guid DeviceId { get; }

    public string DeviceName => settings.FriendlyName ?? "Webkassa Fiscal Adapter Spike";

    public void Setup(DeviceSettings newSettings)
    {
        if (newSettings == null)
            throw new ArgumentNullException(nameof(newSettings));

        var wasRunning = state == State.Running;
        if (wasRunning)
            Stop();

        settings = (CashRegisterSettings)newSettings;
        PluginContext.Log.Info($"Webkassa fiscal adapter spike applied setup. DeviceId={DeviceId}, FriendlyName={DeviceName}");

        if (settings.Autorun && wasRunning)
            Start();
    }

    public void Start()
    {
        state = State.Running;
        var configuration = LoadConfigurationBestEffort();
        fiscalizationStatusText = configuration.Fiscalization.DryRunDoCheque
            ? "dry-run fiscalization is enabled"
            : "live sidecar fiscalization is enabled";
        RestorePersistentStateBestEffort();
        RestoreCashSessionFromIikoStateBestEffort();
        PluginContext.Log.Info($"Webkassa fiscal adapter spike started. Version={ReleaseInfo.Version}, Channel={ReleaseInfo.Channel}, FiscalizationMode={fiscalizationStatusText}, CashSessionOpen={cashSessionOpen}, SalesCount={salesCount}, TotalIncomeSum={totalIncomeSum}");
    }

    public void Stop()
    {
        state = State.Stopped;
        PluginContext.Log.Info("Webkassa fiscal adapter spike stopped.");
    }

    public void RemoveDevice()
    {
        Stop();
    }

    public DeviceInfo GetDeviceInfo()
    {
        return new DeviceInfo(state, $"Webkassa fiscal adapter {ReleaseInfo.Version}; Webkassa protocol {ReleaseInfo.ProtocolVersion}", settings);
    }

    public CashRegisterDriverParameters GetCashRegisterDriverParameters()
    {
        return new CashRegisterDriverParameters
        {
            CanPrintText = false,
            CanPrintBarcode = false,
            CanPrintQRCode = false,
            CanPrintImage = false,
            CanPrintLogo = false,
            CanUseFontSizes = false,
            IsBillTaskSupported = true,
            IsBuyChequeSupported = true,
            IsCancellationSupported = true,
            IsCustomDuplicateCheckSupported = false,
            IsMultipleMarkingCodesPerUnitSupported = true,
            IsRegisterStatusSupported = true,
            NeedToCheckBillNumber = false,
            PrintDetailedChequeWithPrepay = false,
            PrintNonFiscalPrepayCheque = false,
            SupportsPayInAfterSessionClose = false,
            ZeroCashOnClose = false,
            Font0Width = 44,
            Font1Width = 44,
            Font2Width = 44,
        };
    }

    public CashRegisterStatus GetCashRegisterStatus(GetCashRegisterStatusTask task)
    {
        var statusFields = task?.StatusFields == null
            ? "<null>"
            : string.Join(",", task.StatusFields);
        PluginContext.Log.Info($"Webkassa fiscal adapter spike status requested. Fields={statusFields}, CashSessionOpen={cashSessionOpen}, CashSessionNumber={cashSessionNumber}");

        var configuration = LoadConfigurationBestEffort();
        var licenseStatus = GetLicenseStatusBestEffort(configuration, force: false);
        var statusBarInfo = state == State.Running
            ? $"Webkassa fiscal adapter ready; {fiscalizationStatusText}; cash session is {(cashSessionOpen ? "open" : "closed")}."
            : "Webkassa fiscal adapter stopped.";
        if (licenseStatus != null && NeedsLicenseWarning(licenseStatus))
            statusBarInfo = $"Внимание: {BuildLicenseWarningMessage(licenseStatus)}";

        return new CashRegisterStatus
        {
            RegisterDateTime = DateTime.Now,
            HaveRegisterDateTime = true,
            IsOfdConnected = true,
            OfflineMode = false,
            RestaurantMode = false,
            SessionNumber = cashSessionOpen ? Math.Max(cashSessionNumber, 1) : 0,
            SessionStatus = cashSessionOpen ? 1 : 0,
            SalesCount = salesCount,
            SalesSum = (double)salesSum,
            SalesSumTotal = (double)totalIncomeSum,
            CashPaymentSum = (double)cashSum,
            NonCashPaymentsSum = (double)nonCashPaymentSum,
            SerialNumber = ReleaseInfo.DeviceSerialPlaceholder,
            RegistrationNumber = ReleaseInfo.CashboxRegistrationPlaceholder,
            StatusBarInfo = statusBarInfo,
        };
    }

    public CashRegisterResult GetCashRegisterData()
    {
        return BuildSnapshotResult();
    }

    public CashRegisterResult DoCheque(
        ChequeTask chequeTask,
        IViewManager viewManager,
        IOperationDataContext context,
        IOperationService operationService)
    {
        if (chequeTask == null)
            throw new ArgumentNullException(nameof(chequeTask));

        PluginContext.Log.Info(
            $"Webkassa fiscal adapter spike received DoCheque. OrderId={chequeTask.OrderId}, IsRefund={chequeTask.IsRefund}, IsProductRefund={chequeTask.IsProductRefund}, IsCancellation={chequeTask.IsCancellation}, CancellingSaleNumber={chequeTask.CancellingSaleNumber}, Sales={chequeTask.Sales.Count}, ResultSum={chequeTask.ResultSum}");

        var draft = ChequeTaskDraftMapper.Map(chequeTask);
        var enrichedNktPositions = NktIdentifierEnricher.Enrich(draft);
        var externalCheckNumber = ChequeTaskDraftMapper.BuildExternalCheckNumber(draft);
        PluginContext.Log.Info(
            $"Webkassa fiscal adapter spike mapped draft. ExternalCheckNumber={externalCheckNumber}, IsReturn={draft.IsReturn}, Positions={draft.Positions.Count}, NktEnrichedPositions={enrichedNktPositions}, Payments={draft.Payments.Count}, Warnings={draft.Warnings.Count}");

        var configuration = LoadConfigurationBestEffort();
        ShowLicenseWarningIfNeeded(viewManager, configuration);
        if (configuration.Fiscalization.DryRunDoCheque)
        {
            saleNumber++;
            ApplyChequeTotals(draft, chequeTask);
            SavePersistentStateBestEffort();
            PluginContext.Log.Info(
                $"Webkassa fiscal adapter spike dry-run DoCheque accepted. ExternalCheckNumber={externalCheckNumber}, OrderNumber={draft.OrderNumber}, ResultSum={draft.ResultSum}, CashSum={cashSum}, TotalIncomeSum={totalIncomeSum}, SalesCount={salesCount}, WriteFiscalData={configuration.Fiscalization.WriteFiscalData}");

            return BuildSnapshotResult(
                message: $"Webkassa dry-run cheque accepted: {externalCheckNumber}",
                documentNumber: externalCheckNumber,
                saleNumber: saleNumber);
        }

        if (configuration.Sidecar == null || !configuration.Sidecar.Enabled)
            throw NotImplemented("Webkassa fiscalization sidecar is disabled.");

        try
        {
            using (var sidecar = new SidecarClient(configuration))
            {
                var result = draft.IsReturn
                    ? sidecar.FiscalizeReturn(draft, null)
                    : sidecar.FiscalizeSale(draft);

                saleNumber++;
                ApplyChequeTotals(draft, chequeTask);
                SavePersistentStateBestEffort();
                TryAutoPrintFiscalReceipt(draft, result, viewManager);

                var documentNumber = FirstNonEmpty(result.CheckNumber, result.ExternalCheckNumber, externalCheckNumber);
                var message = result.QueuedOffline
                    ? $"Webkassa cheque queued offline: {documentNumber}"
                    : $"Webkassa cheque accepted: {documentNumber}";
                PluginContext.Log.Info(
                    $"Webkassa fiscal adapter sidecar DoCheque accepted. Status={result.Status}, QueuedOffline={result.QueuedOffline}, OfflineExpiresAt={result.OfflineExpiresAt}, ExternalCheckNumber={result.ExternalCheckNumber}, CheckNumber={result.CheckNumber}, ShiftNumber={result.ShiftNumber}, IsReturn={draft.IsReturn}, CashSum={cashSum}, TotalIncomeSum={totalIncomeSum}, SalesCount={salesCount}");

                return BuildSnapshotResult(
                    message: message,
                    documentNumber: documentNumber,
                    saleNumber: saleNumber);
            }
        }
            catch (SidecarException error)
            {
                PluginContext.Log.Error($"Webkassa fiscal adapter sidecar DoCheque failed. ExternalCheckNumber={externalCheckNumber}, Error={SafeLogMessage(error.Message)}");
                ShowSidecarErrorPopup(viewManager, error, "Ошибка фискализации Webkassa");
                throw new DeviceException($"Webkassa fiscalization failed: {SafeDeviceMessage(BuildDeviceErrorMessage(error))}");
            }
    }

    public CashRegisterResult DoBillCheque(BillTask billTask, IViewManager viewManager)
    {
        if (billTask == null)
            throw new ArgumentNullException(nameof(billTask));

        EnsureRunning();
        PluginContext.Log.Info(
            $"Webkassa fiscal adapter spike accepted dry-run bill cheque. OrderId={billTask.OrderId}, OrderNumber={billTask.OrderNumber}, Sales={billTask.Sales.Count}, ResultSum={billTask.ResultSum}");

        return BuildSnapshotResult(
            message: $"Webkassa dry-run bill accepted: order-{billTask.OrderNumber}",
            documentNumber: $"bill-{billTask.OrderNumber}");
    }

    public CashRegisterResult DoOpenSession(OpenSessionTask task, IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        cashSessionOpen = true;
        cashSessionNumber = Math.Max(cashSessionNumber + 1, 1);
        SavePersistentStateBestEffort();
        PluginContext.Log.Info($"Webkassa fiscal adapter spike accepted open-session command. Cashier={cashier?.Name}, CashSessionNumber={cashSessionNumber}");
        return BuildSnapshotResult(message: $"Webkassa dry-run cash session opened: {cashSessionNumber}");
    }

    public CashRegisterResult DoXReport(XReportTask task, IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        var configuration = LoadConfigurationBestEffort();
        ShowLicenseWarningIfNeeded(viewManager, configuration);
        if (!configuration.Fiscalization.DryRunDoCheque && configuration.Sidecar != null && configuration.Sidecar.Enabled)
        {
            try
            {
                using (var sidecar = new SidecarClient(configuration))
                {
                    var result = sidecar.RunXReport();
                    PluginContext.Log.Info($"Webkassa fiscal adapter sidecar X-report accepted. Cashier={cashier?.Name}, Status={result.Status}, ReportNumber={result.ReportNumber}, ShiftNumber={result.ShiftNumber}, DocumentCount={result.DocumentCount}");
                    TryPrintReport(result, viewManager);
                    return BuildSnapshotResult(message: $"Webkassa X-report accepted: {result.ReportNumber}");
                }
            }
            catch (SidecarException error)
            {
                PluginContext.Log.Error($"Webkassa fiscal adapter sidecar X-report failed. Error={SafeLogMessage(error.Message)}");
                ShowSidecarErrorPopup(viewManager, error, "Ошибка X-отчета Webkassa");
                throw new DeviceException($"Webkassa X-report failed: {SafeDeviceMessage(BuildDeviceErrorMessage(error))}");
            }
        }

        PluginContext.Log.Info($"Webkassa fiscal adapter spike accepted dry-run X-report command. Cashier={cashier?.Name}");
        return BuildSnapshotResult();
    }

    public CashRegisterResult DoZReport(ICafeSession cafeSession, ZReportTask task, IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        var configuration = LoadConfigurationBestEffort();
        ShowLicenseWarningIfNeeded(viewManager, configuration);
        if (!configuration.Fiscalization.DryRunDoCheque && configuration.Sidecar != null && configuration.Sidecar.Enabled)
        {
            try
            {
                using (var sidecar = new SidecarClient(configuration))
                {
                    var result = sidecar.RunZReport();
                    PluginContext.Log.Info($"Webkassa fiscal adapter sidecar Z-report accepted. Cashier={cashier?.Name}, Status={result.Status}, ReportNumber={result.ReportNumber}, ShiftNumber={result.ShiftNumber}, DocumentCount={result.DocumentCount}");
                    CloseCashSessionState();
                    TryPrintReport(result, viewManager);
                    return BuildSnapshotResult(message: $"Webkassa Z-report accepted: {result.ReportNumber}");
                }
            }
            catch (SidecarException error)
            {
                if (IsShiftAlreadyClosedError(error))
                {
                    PluginContext.Log.Info($"Webkassa fiscal adapter sidecar Z-report reconciled as already closed. Cashier={cashier?.Name}, Error={SafeLogMessage(error.Message)}");
                    CloseCashSessionState();
                    return BuildSnapshotResult(message: "Webkassa Z-report already closed; local iiko session reconciled.");
                }

                PluginContext.Log.Error($"Webkassa fiscal adapter sidecar Z-report failed. Error={SafeLogMessage(error.Message)}");
                ShowSidecarErrorPopup(viewManager, error, "Ошибка Z-отчета Webkassa");
                throw new DeviceException($"Webkassa Z-report failed: {SafeDeviceMessage(BuildDeviceErrorMessage(error))}");
            }
        }

        PluginContext.Log.Info($"Webkassa fiscal adapter spike accepted dry-run Z-report command. Cashier={cashier?.Name}, CashSessionNumber={cashSessionNumber}");
        CloseCashSessionState();
        return BuildSnapshotResult(message: $"Webkassa dry-run cash session closed: {cashSessionNumber}");
    }

    private void CloseCashSessionState()
    {
        cashSessionOpen = false;
        cashSum = 0m;
        totalIncomeSum = 0m;
        salesSum = 0m;
        nonCashPaymentSum = 0m;
        salesCount = 0;
        SavePersistentStateBestEffort();
    }

    public CashRegisterResult DoPayIn(PayInTask task, IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        var amount = ResolveMoneyTaskAmount(task);
        if (amount > 0m)
            cashSum += amount;
        SavePersistentStateBestEffort();
        PluginContext.Log.Info($"Webkassa fiscal adapter accepted local pay-in. Cashier={cashier?.Name}, Amount={amount}, CashSum={cashSum}");
        return BuildSnapshotResult(message: $"Webkassa local pay-in accepted: {amount.ToString(CultureInfo.InvariantCulture)}");
    }

    public CashRegisterResult DoPayOut(PayOutTask task, IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        var amount = ResolveMoneyTaskAmount(task);
        if (amount > 0m)
            cashSum = Math.Max(0m, cashSum - amount);
        else if (cashSum > 0m)
            cashSum = 0m;
        SavePersistentStateBestEffort();
        PluginContext.Log.Info($"Webkassa fiscal adapter accepted local pay-out. Cashier={cashier?.Name}, Amount={amount}, CashSum={cashSum}");
        return BuildSnapshotResult(message: $"Webkassa local pay-out accepted: {amount.ToString(CultureInfo.InvariantCulture)}");
    }

    public bool IsDrawerOpened()
    {
        return true;
    }

    public void OpenDrawer(IUser cashier, IViewManager viewManager)
    {
        EnsureRunning();
        PluginContext.Log.Info($"Webkassa fiscal adapter spike accepted dry-run open-drawer command. Cashier={cashier?.Name}");
    }

    public void CustomerDisplayIdle(TimeSpan timeToIdle)
    {
    }

    public void CustomerDisplayText(CustomerDisplayTextTask task)
    {
    }

    public CashRegisterResult PrintText(PrintTextTask task, IUser cashier, IViewManager viewManager)
    {
        throw NotImplemented("Print text is not implemented in the spike.");
    }

    public DirectIoResult DirectIo(DirectIoTask task, IUser cashier, IViewManager viewManager)
    {
        throw NotImplemented("Direct IO is not implemented in the spike.");
    }

    public QueryInfoResult GetQueryInfo()
    {
        return new QueryInfoResult(new List<SupportedCommand>(), false);
    }

    private void EnsureRunning()
    {
        if (state != State.Running)
            throw NotImplemented("Cash register is not started.");
    }

    private AdapterConfiguration LoadConfigurationBestEffort()
    {
        try
        {
            return AdapterConfigurationLoader.LoadFromDefaultLocation();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException || error is ArgumentException)
        {
            PluginContext.Log.Info($"Webkassa fiscal adapter spike uses default dry-run configuration. Config load failed: {error.GetType().Name}: {error.Message}");
            return new AdapterConfiguration();
        }
    }

    private CashRegisterResult BuildSnapshotResult(string? message = null, string? documentNumber = null, int? saleNumber = null)
    {
        return new CashRegisterResult(
            cashSum: cashSum,
            totalIncomeSum: totalIncomeSum,
            session: cashSessionOpen ? Math.Max(cashSessionNumber, 1) : 0,
            serialNumber: ReleaseInfo.DeviceSerialPlaceholder,
            documentNumber: documentNumber,
            saleNumber: saleNumber,
            billNumber: null,
            rtcDateTime: DateTime.Now.ToString("O"),
            documentSign: null,
            fnSerialNumber: null,
            nearPaperEnd: false)
        {
            Success = true,
            Message = message ?? $"Webkassa fiscal adapter {ReleaseInfo.Version} snapshot only.",
        };
    }

    private static void TryAutoPrintFiscalReceipt(
        IikoChequeDraft draft,
        SidecarFiscalizationResult result,
        IViewManager viewManager)
    {
        if (!WebkassaPrintRequests.Consume(draft.OrderId))
            return;

        if (result.QueuedOffline)
        {
            try
            {
                WebkassaReceiptPrinter.PrintOfflineQueuedNotice(PluginContext.Operations, result, draft.OrderNumber);
                PluginContext.Log.Info($"Webkassa offline queued receipt notice auto-printed. OrderId={draft.OrderId}, ExternalCheckNumber={result.ExternalCheckNumber}, OfflineExpiresAt={result.OfflineExpiresAt}");
            }
            catch (Exception error)
            {
                PluginContext.Log.Error($"Webkassa offline queued receipt notice auto-print failed. OrderId={draft.OrderId}, Error={error.GetType().Name}: {error.Message}");
                try
                {
                    viewManager.ShowErrorPopup($"Операция Webkassa поставлена в offline-очередь, но бумажное уведомление не распечаталось: {error.Message}", "Закрыть");
                }
                catch
                {
                    // Payment must not fail after successful offline queueing because of a local print popup failure.
                }
            }

            return;
        }

        try
        {
            var record = WebkassaReceiptPrinter.FromFiscalizationResult(result);
            WebkassaReceiptPrinter.Print(PluginContext.Operations, record, draft.OrderNumber);
            PluginContext.Log.Info($"Webkassa fiscal receipt auto-printed. OrderId={draft.OrderId}, ExternalCheckNumber={record.ExternalCheckNumber}, CheckNumber={record.CheckNumber}");
        }
        catch (Exception error)
        {
            PluginContext.Log.Error($"Webkassa fiscal receipt auto-print failed. OrderId={draft.OrderId}, Error={error.GetType().Name}: {error.Message}");
            try
            {
                viewManager.ShowErrorPopup($"Чек Webkassa фискализирован, но бумажная печать не удалась: {error.Message}", "Закрыть");
            }
            catch
            {
                // Payment must not fail after successful fiscalization because of a local print popup failure.
            }
        }
    }

    private static void TryPrintReport(SidecarReportResult result, IViewManager viewManager)
    {
        try
        {
            WebkassaReceiptPrinter.PrintReport(PluginContext.Operations, result);
            PluginContext.Log.Info($"Webkassa {result.ReportType}-report printed. ReportNumber={result.ReportNumber}, ShiftNumber={result.ShiftNumber}");
        }
        catch (Exception error)
        {
            PluginContext.Log.Error($"Webkassa {result.ReportType}-report print failed. ReportNumber={result.ReportNumber}, Error={error.GetType().Name}: {error.Message}");
            try
            {
                viewManager.ShowErrorPopup($"Отчет Webkassa сформирован, но печать не удалась: {error.Message}", "Закрыть");
            }
            catch
            {
                // Report creation must not be treated as failed after Webkassa accepted it.
            }
        }
    }

    private void ApplyChequeTotals(IikoChequeDraft draft, ChequeTask chequeTask)
    {
        var sign = CashRegisterTotalsSign(draft, chequeTask);
        var cashPayment = draft.Payments
            .Where(payment => string.Equals(payment.PaymentType, "cash", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => Math.Abs(payment.Sum));
        var nonCashPayment = draft.Payments
            .Where(payment => !string.Equals(payment.PaymentType, "cash", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => Math.Abs(payment.Sum));

        cashSum += sign * cashPayment;
        totalIncomeSum += sign * Math.Abs(draft.ResultSum);
        salesSum += sign * Math.Abs(draft.ResultSum);
        nonCashPaymentSum += sign * nonCashPayment;
        salesCount++;
    }

    private static decimal CashRegisterTotalsSign(IikoChequeDraft draft, ChequeTask chequeTask)
    {
        if (!draft.IsReturn)
            return 1m;

        if (chequeTask.IsRefund || chequeTask.IsProductRefund || chequeTask.IsCancellation || chequeTask.CancellingSaleNumber > 0)
            return -1m;

        return 1m;
    }

    private static decimal ResolveMoneyTaskAmount(object? task)
    {
        if (task == null)
            return 0m;

        foreach (var name in new[] { "Sum", "Amount", "CashSum", "MoneySum", "Value" })
        {
            var property = task.GetType().GetProperty(name);
            if (property == null)
                continue;

            var value = property.GetValue(task, null);
            if (TryConvertToDecimal(value, out var amount))
                return Math.Abs(amount);
        }

        return 0m;
    }

    private static bool TryConvertToDecimal(object? value, out decimal amount)
    {
        amount = 0m;
        if (value == null)
            return false;

        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    amount = decimalValue;
                    return true;
                case double doubleValue:
                    amount = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                case float floatValue:
                    amount = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                case int intValue:
                    amount = intValue;
                    return true;
                case long longValue:
                    amount = longValue;
                    return true;
                case string stringValue:
                    return decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
                        || decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
            }

            amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception error) when (error is FormatException || error is InvalidCastException || error is OverflowException)
        {
            return false;
        }
    }

    private void RestorePersistentStateBestEffort()
    {
        try
        {
            var path = PersistentStatePath();
            if (!File.Exists(path))
                return;

            var root = XDocument.Load(path).Root;
            if (root == null)
                return;

            cashSessionOpen = ReadBool(root, "CashSessionOpen", cashSessionOpen);
            cashSessionNumber = ReadInt(root, "CashSessionNumber", cashSessionNumber);
            cashSum = ReadDecimal(root, "CashSum", cashSum);
            totalIncomeSum = ReadDecimal(root, "TotalIncomeSum", totalIncomeSum);
            salesSum = ReadDecimal(root, "SalesSum", salesSum);
            nonCashPaymentSum = ReadDecimal(root, "NonCashPaymentSum", nonCashPaymentSum);
            salesCount = ReadInt(root, "SalesCount", salesCount);
            saleNumber = ReadInt(root, "SaleNumber", saleNumber);
            PluginContext.Log.Info($"Webkassa fiscal adapter spike restored persistent state. Path={path}, CashSessionOpen={cashSessionOpen}, SalesCount={salesCount}, TotalIncomeSum={totalIncomeSum}");
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException || error is ArgumentException || error is System.Xml.XmlException)
        {
            PluginContext.Log.Info($"Webkassa fiscal adapter spike could not restore persistent state: {error.GetType().Name}: {error.Message}");
        }
    }

    private void SavePersistentStateBestEffort()
    {
        try
        {
            var path = PersistentStatePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var document = new XDocument(
                new XElement("WebkassaCashRegisterState",
                    new XAttribute("deviceId", DeviceId),
                    new XAttribute("updatedAt", DateTimeOffset.Now.ToString("O")),
                    new XElement("CashSessionOpen", cashSessionOpen),
                    new XElement("CashSessionNumber", cashSessionNumber),
                    new XElement("CashSum", cashSum.ToString(CultureInfo.InvariantCulture)),
                    new XElement("TotalIncomeSum", totalIncomeSum.ToString(CultureInfo.InvariantCulture)),
                    new XElement("SalesSum", salesSum.ToString(CultureInfo.InvariantCulture)),
                    new XElement("NonCashPaymentSum", nonCashPaymentSum.ToString(CultureInfo.InvariantCulture)),
                    new XElement("SalesCount", salesCount),
                    new XElement("SaleNumber", saleNumber)));

            document.Save(path);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException || error is ArgumentException || error is System.Xml.XmlException)
        {
            PluginContext.Log.Info($"Webkassa fiscal adapter spike could not save persistent state: {error.GetType().Name}: {error.Message}");
        }
    }

    private string PersistentStatePath()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            programData = AppDomain.CurrentDomain.BaseDirectory;

        return Path.Combine(
            programData,
            "WebkassaIikoFrontAdapter",
            "state",
            $"cash-register-{DeviceId}.xml");
    }

    private static bool ReadBool(XElement root, string name, bool fallback)
    {
        var value = root.Element(name)?.Value;
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(XElement root, string name, int fallback)
    {
        var value = root.Element(name)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static decimal ReadDecimal(XElement root, string name, decimal fallback)
    {
        var value = root.Element(name)?.Value;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "unknown";
    }

    private static bool IsShiftAlreadyClosedError(SidecarException error)
    {
        var message = error.Message ?? string.Empty;
        return message.IndexOf("\u0421\u043c\u0435\u043d\u0430 \u0443\u0436\u0435 \u0437\u0430\u043a\u0440\u044b\u0442\u0430", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("shift already closed", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SafeLogMessage(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value!.Replace("{", "{{").Replace("}", "}}");
    }

    private static string SafeDeviceMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value!
            .Replace("{", "[")
            .Replace("}", "]")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string BuildDeviceErrorMessage(SidecarException error)
    {
        var diagnostic = error.OperatorDiagnostic;
        if (diagnostic == null)
            return error.Message;

        return FirstNonEmpty(diagnostic.Title, error.Message);
    }

    private SidecarLicenseStatusResult? GetLicenseStatusBestEffort(AdapterConfiguration configuration, bool force)
    {
        var monitoring = configuration.LicenseMonitoring ?? new AdapterLicenseMonitoringOptions();
        if (!monitoring.Enabled)
            return null;

        if (configuration.Sidecar == null || !configuration.Sidecar.Enabled)
            return null;

        var intervalMinutes = Math.Max(5, monitoring.CheckIntervalMinutes);
        if (!force && cachedLicenseStatus != null && DateTime.UtcNow - lastLicenseStatusCheckUtc < TimeSpan.FromMinutes(intervalMinutes))
            return cachedLicenseStatus;

        try
        {
            using (var sidecar = new SidecarClient(configuration))
            {
                cachedLicenseStatus = sidecar.GetLicenseStatus();
                lastLicenseStatusCheckUtc = DateTime.UtcNow;
                if (cachedLicenseStatus != null && NeedsLicenseWarning(cachedLicenseStatus))
                    PluginContext.Log.Warn($"Webkassa license warning. Status={cachedLicenseStatus.Status}, DaysRemaining={cachedLicenseStatus.LicenseDaysRemaining}, Expiration={cachedLicenseStatus.LicenseExpirationDate}");
                return cachedLicenseStatus;
            }
        }
        catch (Exception error) when (error is SidecarException || error is InvalidOperationException || error is IOException)
        {
            PluginContext.Log.Warn($"Webkassa license monitor check failed: {error.GetType().Name}: {SafeLogMessage(error.Message)}");
            lastLicenseStatusCheckUtc = DateTime.UtcNow;
            return cachedLicenseStatus;
        }
    }

    private void ShowLicenseWarningIfNeeded(IViewManager? viewManager, AdapterConfiguration configuration)
    {
        if (viewManager == null)
            return;

        var status = GetLicenseStatusBestEffort(configuration, force: false);
        if (status == null || !NeedsLicenseWarning(status))
            return;

        if (DateTime.UtcNow - lastLicenseWarningPopupUtc < TimeSpan.FromHours(24))
            return;

        lastLicenseWarningPopupUtc = DateTime.UtcNow;
        try
        {
            viewManager.ShowErrorPopup(BuildLicenseWarningMessage(status), "Закрыть");
        }
        catch (Exception popupError)
        {
            PluginContext.Log.Warn($"Webkassa license warning popup failed. Error={popupError.GetType().Name}: {popupError.Message}");
        }
    }

    private static bool NeedsLicenseWarning(SidecarLicenseStatusResult status)
    {
        return status.LicenseExpired || status.LicenseWarning || status.OfdExpired || status.OfdWarning;
    }

    private static string BuildLicenseWarningMessage(SidecarLicenseStatusResult status)
    {
        var lines = new List<string>();
        if (status.LicenseExpired)
        {
            lines.Add($"Лицензия Webkassa истекла: {FirstNonEmpty(status.LicenseExpirationDate, "-")}.");
        }
        else if (status.LicenseWarning)
        {
            var days = status.LicenseDaysRemaining.HasValue ? status.LicenseDaysRemaining.Value.ToString(CultureInfo.InvariantCulture) : "-";
            lines.Add($"Лицензия Webkassa заканчивается менее чем через {status.WarningDays} дней.");
            lines.Add($"Осталось дней: {days}. Дата окончания: {FirstNonEmpty(status.LicenseExpirationDate, "-")}.");
        }

        if (status.OfdExpired)
        {
            lines.Add($"Срок ОФД истек: {FirstNonEmpty(status.OfdExpirationDate, "-")}.");
        }
        else if (status.OfdWarning)
        {
            var days = status.OfdDaysRemaining.HasValue ? status.OfdDaysRemaining.Value.ToString(CultureInfo.InvariantCulture) : "-";
            lines.Add($"Срок ОФД заканчивается менее чем через {status.WarningDays} дней. Осталось дней: {days}. Дата окончания: {FirstNonEmpty(status.OfdExpirationDate, "-")}.");
        }

        lines.Add("Что сделать: продлите лицензию Webkassa/ОФД или свяжитесь с ЦТО. Продажи не блокируются этим предупреждением.");
        return SafeDeviceMessage(string.Join(Environment.NewLine + Environment.NewLine, lines));
    }

    private static void ShowSidecarErrorPopup(IViewManager? viewManager, SidecarException error, string caption)
    {
        if (viewManager == null)
            return;

        try
        {
            viewManager.ShowErrorPopup(BuildOperatorPopupMessage(error), "Закрыть");
        }
        catch (Exception popupError)
        {
            PluginContext.Log.Warn($"Webkassa operator error popup failed. Caption={caption}, Error={popupError.GetType().Name}: {popupError.Message}");
        }
    }

    private static string BuildOperatorPopupMessage(SidecarException error)
    {
        var diagnostic = error.OperatorDiagnostic;
        if (diagnostic == null)
            return $"Ошибка Webkassa: {SafeDeviceMessage(error.Message)}";

        var lines = new List<string>
        {
            FirstNonEmpty(diagnostic.Title, "Ошибка Webkassa")
        };

        AppendIfNotEmpty(lines, "Что произошло", diagnostic.OperatorMessage);
        AppendIfNotEmpty(lines, "Что сделать", diagnostic.NextAction);
        AppendIfNotEmpty(lines, "Код Webkassa", diagnostic.WebkassaCode);
        AppendIfNotEmpty(lines, "Текст Webkassa", diagnostic.WebkassaText);
        AppendIfNotEmpty(lines, "ExternalCheckNumber", diagnostic.ExternalCheckNumber);
        AppendIfNotEmpty(lines, "Endpoint", diagnostic.Endpoint);
        if (diagnostic.HttpStatus.HasValue)
            lines.Add($"HTTP: {diagnostic.HttpStatus.Value}");

        return SafeDeviceMessage(string.Join(Environment.NewLine + Environment.NewLine, lines));
    }

    private static void AppendIfNotEmpty(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value}");
    }

    private void RestoreCashSessionFromIikoStateBestEffort()
    {
        if (cashSessionOpen)
            return;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                return;

            var path = Path.Combine(appData, "iiko", "CashServer", "CashServerInfo.xml");
            if (!File.Exists(path))
                return;

            var document = XDocument.Load(path);
            var root = document.Root;
            if (root == null)
                return;

            var sessionNumberText = root.Element("CurrentCafeSessionNumber")?.Value;
            var registerIdText = root.Element("StartPageCashRegisterId")?.Value;
            if (!int.TryParse(sessionNumberText, out var currentSessionNumber) || currentSessionNumber <= 0)
                return;

            if (!Guid.TryParse(registerIdText, out var startPageRegisterId) || startPageRegisterId != DeviceId)
                return;

            cashSessionNumber = Math.Max(cashSessionNumber, currentSessionNumber);
            cashSessionOpen = true;
            PluginContext.Log.Info($"Webkassa fiscal adapter spike restored cash session from iiko state. CashSessionNumber={cashSessionNumber}, CashRegisterId={DeviceId}");
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException || error is ArgumentException || error is System.Xml.XmlException)
        {
            PluginContext.Log.Info($"Webkassa fiscal adapter spike could not restore cash session from iiko state: {error.GetType().Name}: {error.Message}");
        }
    }

    private DeviceException NotImplemented(string message)
    {
        return new DeviceException($"{message} State={state}");
    }
}
