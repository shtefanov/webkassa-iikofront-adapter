using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Security.Principal;
using Resto.Front.Api.Data.Orders;
using Resto.Front.Api;
using Resto.Front.Api.Attributes;
using Resto.Front.Api.Devices;
using Resto.Front.Api.Data.Payments;
using Resto.Front.Api.UI;

namespace Resto.Front.Api.Webkassa.V9;

[PluginLicenseModuleId(ReleaseInfo.IikoLicenseModuleId)]
public sealed class Plugin : MarshalByRefObject, IFrontPlugin
{
    private const string PrintTicketCaption = "Печать фискального чека";
    private const string ShowTicketQrCaption = "QR фискального чека";
    private const string PaymentPrintCaption = "Печатать фискальный чек";
    private const string SettingsCaption = "Настройки Webkassa";
    private const string SetupExecutableName = "Webkassa.IikoFrontAdapter.Setup.exe";
    private const string SetupPathFileName = "Webkassa.IikoFrontAdapter.Setup.path";
    private const string PrintIconGeometry = "M3,6 L21,6 L21,18 L3,18 Z M6,3 L18,3 L18,8 L6,8 Z M7,13 L17,13 M7,16 L17,16";

    private readonly IDisposable cashRegisterFactoryRegistration;
    private readonly IDisposable settingsButtonRegistration;
    private readonly IDisposable closedOrderButtonRegistration;
    private readonly IDisposable closedOrderQrButtonRegistration;
    private readonly IDisposable pastOrderButtonRegistration;
    private readonly IDisposable pastOrderQrButtonRegistration;
    private readonly IDisposable returnButtonRegistration;
    private readonly IDisposable paymentButtonRegistration;
    private static int updateNotificationShown;
    private bool disposed;

    public override object InitializeLifetimeService()
    {
        return null!;
    }

    public Plugin()
    {
        cashRegisterFactoryRegistration = PluginContext.Operations.RegisterCashRegisterFactory(new WebkassaCashRegisterFactory());
        settingsButtonRegistration = PluginContext.Operations.AddButtonToPluginsMenu(
            SettingsCaption,
            OnSettingsButton);
        closedOrderButtonRegistration = PluginContext.Operations.AddButtonToClosedOrderScreen(
            PrintTicketCaption,
            OnClosedOrderPrintButton,
            PrintIconGeometry);
        closedOrderQrButtonRegistration = PluginContext.Operations.AddButtonToClosedOrderScreen(
            ShowTicketQrCaption,
            OnClosedOrderQrButton);
        pastOrderButtonRegistration = PluginContext.Operations.AddButtonToPastOrderScreen(
            PrintTicketCaption,
            OnPastOrderPrintButton,
            PrintIconGeometry);
        pastOrderQrButtonRegistration = PluginContext.Operations.AddButtonToPastOrderScreen(
            ShowTicketQrCaption,
            OnPastOrderQrButton);
        returnButtonRegistration = PluginContext.Operations.AddButtonToProductsReturnScreen(
            PrintTicketCaption,
            OnReturnPrintToggle);
        var paymentButton = PluginContext.Operations.AddButtonToPaymentScreen(
            PaymentPrintCaption,
            isChecked: false,
            isEnabled: true,
            OnPaymentPrintToggle,
            PrintIconGeometry);
        paymentButtonRegistration = paymentButton.Item2;
        PluginContext.Log.Info($"Webkassa fiscal adapter spike registered cash register factory and print UI. Version={ReleaseInfo.Version}");
        ThreadPool.QueueUserWorkItem(_ => WarmUpNktIndexBestEffort());
        ThreadPool.QueueUserWorkItem(_ => CheckForUpdatesBestEffort());
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        paymentButtonRegistration.Dispose();
        returnButtonRegistration.Dispose();
        pastOrderQrButtonRegistration.Dispose();
        pastOrderButtonRegistration.Dispose();
        closedOrderQrButtonRegistration.Dispose();
        closedOrderButtonRegistration.Dispose();
        settingsButtonRegistration.Dispose();
        cashRegisterFactoryRegistration.Dispose();
        PluginContext.Log.Info("Webkassa fiscal adapter spike disposed.");
    }

    private static void OnSettingsButton(ValueTuple<IViewManager, IReceiptPrinter> args)
    {
        var viewManager = args.Item1;
        try
        {
            if (!IsElevatedAdministrator())
            {
                LaunchElevatedSettings();
                return;
            }
            WebkassaSettingsDialog.Show();
        }
        catch (Exception error)
        {
            PluginContext.Log.Error($"Webkassa settings dialog failed. Error={error.GetType().Name}: {error.Message}");
            viewManager.ShowErrorPopup($"Не удалось открыть настройки Webkassa: {error.Message}", "Закрыть");
        }
    }

    private static void LaunchElevatedSettings()
    {
        var setupPath = ResolveSetupExecutablePath();
        if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
            throw new FileNotFoundException(
                "Графический конфигуратор Webkassa не установлен. Переустановите адаптер из актуального пакета.",
                setupPath);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "--gui",
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            PluginContext.Log.Info("Webkassa elevated settings launch was cancelled by the operator.");
        }
    }

    private static string ResolveSetupExecutablePath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? string.Empty;
        var pathFile = Path.Combine(assemblyDirectory, SetupPathFileName);
        if (File.Exists(pathFile))
        {
            var configuredPath = File.ReadAllText(pathFile).Trim();
            if (string.Equals(Path.GetFileName(configuredPath), SetupExecutableName, StringComparison.OrdinalIgnoreCase))
                return configuredPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WebkassaIikoFrontAdapter",
            "setup",
            SetupExecutableName);
    }

    private static bool IsElevatedAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static void WarmUpNktIndexBestEffort()
    {
        try
        {
            var indexPath = NationalCatalogSyncQueue.WarmUpIndex();
            PluginContext.Log.Info($"Webkassa NKT catalog index warmed up. IndexPath={indexPath}");
        }
        catch (Exception error)
        {
            PluginContext.Log.Warn($"Webkassa NKT catalog index warm-up skipped. Error={error.GetType().Name}: {error.Message}");
        }
    }

    private static void CheckForUpdatesBestEffort()
    {
        try
        {
            // Give iikoFront time to finish constructing its initial UI. The
            // network call itself runs outside the startup/UI thread.
            Thread.Sleep(TimeSpan.FromSeconds(5));
            var result = UpdateAvailabilityChecker.CheckOnceAsync().GetAwaiter().GetResult();
            if (!result.CheckSucceeded)
            {
                PluginContext.Log.Info($"Webkassa update check skipped. Error={result.Error}");
                return;
            }

            PluginContext.Log.Info($"Webkassa update check completed. CurrentVersion={result.CurrentVersion}, LatestVersion={result.LatestVersion}, UpdateAvailable={result.UpdateAvailable}");
            if (!result.UpdateAvailable || Interlocked.Exchange(ref updateNotificationShown, 1) != 0)
                return;

            PluginContext.Operations.AddNotificationMessage(
                $"Доступна новая версия Webkassa {result.LatestVersion}. Текущая версия: {result.CurrentVersion}. Откройте «Настройки Webkassa» для информации.",
                "Webkassa",
                TimeSpan.FromSeconds(25));
        }
        catch (Exception error)
        {
            PluginContext.Log.Info($"Webkassa update check failed without blocking startup. Error={error.GetType().Name}: {error.Message}");
        }
    }

    private static void OnPaymentPrintToggle(ValueTuple<IOrder, IOperationService, IViewManager, ValueTuple<Guid, string, bool, string>> args)
    {
        var order = args.Item1;
        var operationService = args.Item2;
        var button = args.Item4;
        var orderId = order.Id.ToString("D");
        var enabled = WebkassaPrintRequests.Toggle(orderId);

        operationService.UpdatePaymentScreenButtonState(
            button.Item1,
            PaymentPrintCaption,
            enabled,
            true,
            PrintIconGeometry);
        PluginContext.Log.Info($"Webkassa fiscal receipt auto-print {(enabled ? "enabled" : "disabled")} for order {orderId}.");
    }

    private static void OnReturnPrintToggle(ValueTuple<IViewManager, Guid, Resto.Front.Api.Data.Device.ICashRegisterInfo> args)
    {
        var orderId = args.Item2.ToString("D");
        var enabled = WebkassaPrintRequests.Toggle(orderId);
        PluginContext.Log.Info($"Webkassa fiscal receipt auto-print {(enabled ? "enabled" : "disabled")} for return order {orderId}.");
    }

    private static void OnClosedOrderPrintButton(ValueTuple<IViewManager, IOrder, Resto.Front.Api.Data.Device.ICashRegisterInfo> args)
    {
        var order = args.Item2;
        ExecuteClosedOrderTicketAction(
            args.Item1,
            order.Id.ToString("D"),
            order.Number.ToString(CultureInfo.InvariantCulture),
            TicketAction.Print,
            "closed order");
    }

    private static void OnClosedOrderQrButton(ValueTuple<IViewManager, IOrder, Resto.Front.Api.Data.Device.ICashRegisterInfo> args)
    {
        var order = args.Item2;
        ExecuteClosedOrderTicketAction(
            args.Item1,
            order.Id.ToString("D"),
            order.Number.ToString(CultureInfo.InvariantCulture),
            TicketAction.ShowQr,
            "closed order");
    }

    private static void OnPastOrderPrintButton(ValueTuple<IViewManager, PastOrder, OrganizationDetailsInfo> args)
    {
        var order = args.Item2;
        ExecuteClosedOrderTicketAction(
            args.Item1,
            order.OrderId.ToString("D"),
            order.Number.ToString(CultureInfo.InvariantCulture),
            TicketAction.Print,
            "past order");
    }

    private static void OnPastOrderQrButton(ValueTuple<IViewManager, PastOrder, OrganizationDetailsInfo> args)
    {
        var order = args.Item2;
        ExecuteClosedOrderTicketAction(
            args.Item1,
            order.OrderId.ToString("D"),
            order.Number.ToString(CultureInfo.InvariantCulture),
            TicketAction.ShowQr,
            "past order");
    }

    private static void ExecuteClosedOrderTicketAction(
        IViewManager viewManager,
        string orderId,
        string orderNumber,
        TicketAction action,
        string sourceScreen)
    {

        try
        {
            var configuration = AdapterConfigurationLoader.LoadFromDefaultLocation();
            if (configuration.Sidecar == null || !configuration.Sidecar.Enabled)
                throw new InvalidOperationException("Webkassa sidecar is disabled.");

            using (var sidecar = new SidecarClient(configuration))
            {
                var lookup = sidecar.FindTicketsByOrderId(orderId);
                if (lookup.Records.Count == 0)
                {
                    viewManager.ShowClosePopup("Webkassa", "Фискальный чек Webkassa для заказа не найден.", "Закрыть");
                    return;
                }

                var record = ChooseRecord(viewManager, lookup.Records);
                if (record == null)
                    return;

                if (action == TicketAction.Print)
                {
                    WebkassaReceiptPrinter.Print(PluginContext.Operations, record, orderNumber);
                    PluginContext.Log.Info($"Webkassa fiscal receipt printed from {sourceScreen} screen. OrderId={orderId}, ExternalCheckNumber={record.ExternalCheckNumber}, CheckNumber={record.CheckNumber}");
                    return;
                }

                var ticketUrl = ResolveExternalTicketUrl(sidecar, record);
                WebkassaTicketQrDialog.Show(ticketUrl, record.CheckNumber ?? record.ExternalCheckNumber);
                PluginContext.Log.Info($"Webkassa external ticket QR shown from {sourceScreen} screen. OrderId={orderId}, ExternalCheckNumber={record.ExternalCheckNumber}, CheckNumber={record.CheckNumber}");
            }
        }
        catch (Exception error)
        {
            var actionTitle = action == TicketAction.Print ? "печать" : "QR внешней ссылки";
            PluginContext.Log.Error($"Webkassa ticket {actionTitle} failed from {sourceScreen} screen. OrderId={orderId}, Error={error.GetType().Name}: {error.Message}");
            viewManager.ShowErrorPopup($"Не удалось выполнить действие «{actionTitle}»: {error.Message}", "Закрыть");
        }
    }

    private static string ResolveExternalTicketUrl(SidecarClient sidecar, SidecarTicketRecord record)
    {
        var storedUrl = FirstNonEmpty(record.TicketUrl, record.TicketPrintUrl);
        if (WebkassaTicketQrDialog.IsSafeExternalUrl(storedUrl))
            return storedUrl!;

        if (string.IsNullOrWhiteSpace(record.ExternalCheckNumber))
            throw new InvalidOperationException("У чека отсутствует ExternalCheckNumber для поиска внешней ссылки.");

        var printFormat = sidecar.GetTicketPrintFormat(record.ExternalCheckNumber!);
        foreach (var line in printFormat.Lines)
        {
            if (line.Type == 2 && WebkassaTicketQrDialog.IsSafeExternalUrl(line.Value))
                return line.Value;
        }

        throw new InvalidOperationException("Webkassa не вернула внешнюю HTTPS-ссылку для просмотра чека.");
    }

    private static SidecarTicketRecord? ChooseRecord(IViewManager viewManager, IList<SidecarTicketRecord> records)
    {
        if (records.Count == 1)
            return records[0];

        var items = new List<string>();
        foreach (var record in records)
            items.Add($"{OperationTitle(record.Operation)} {record.CheckNumber ?? record.ExternalCheckNumber} {FormatMoney(record.Total)} тг.");

        var selected = viewManager.ShowChooserPopup(
            "Выберите чек Webkassa",
            items,
            selectedItemIndex: records.Count - 1,
            buttonWidth: ButtonWidth.Wider,
            closeBtnText: "Отмена");

        return selected >= 0 && selected < records.Count ? records[selected] : null;
    }

    private static string OperationTitle(string? operation)
    {
        return string.Equals(operation, "sale_return", StringComparison.OrdinalIgnoreCase)
            ? "Возврат"
            : "Продажа";
    }

    private static string FormatMoney(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "-";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!.Trim();
        }

        return null;
    }

    private enum TicketAction
    {
        Print,
        ShowQr,
    }
}
