using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Security.Principal;
using Resto.Front.Api.Data.Orders;
using Resto.Front.Api;
using Resto.Front.Api.Attributes;
using Resto.Front.Api.Devices;
using Resto.Front.Api.UI;

namespace Resto.Front.Api.Webkassa.V9;

[PluginLicenseModuleId(ReleaseInfo.IikoLicenseModuleId)]
public sealed class Plugin : MarshalByRefObject, IFrontPlugin
{
    private const string PrintTicketCaption = "Печать Webkassa чека";
    private const string PaymentPrintCaption = "Печатать фискальный чек";
    private const string SettingsCaption = "Настройки Webkassa";
    private const string PrintIconGeometry = "M3,6 L21,6 L21,18 L3,18 Z M6,3 L18,3 L18,8 L6,8 Z M7,13 L17,13 M7,16 L17,16";

    private readonly IDisposable cashRegisterFactoryRegistration;
    private readonly IDisposable settingsButtonRegistration;
    private readonly IDisposable closedOrderButtonRegistration;
    private readonly IDisposable returnButtonRegistration;
    private readonly IDisposable paymentButtonRegistration;
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
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        paymentButtonRegistration.Dispose();
        returnButtonRegistration.Dispose();
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
                viewManager.ShowErrorPopup(
                    "Настройки Webkassa доступны только в административном сеансе Windows. Используйте Webkassa.IikoFrontAdapter.Setup.exe с повышенными правами.",
                    "Закрыть");
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
        var viewManager = args.Item1;
        var order = args.Item2;
        var orderId = order.Id.ToString("D");
        var orderNumber = order.Number.ToString(CultureInfo.InvariantCulture);

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

                WebkassaReceiptPrinter.Print(PluginContext.Operations, record, orderNumber);
                viewManager.ShowOkPopup("Webkassa", "Фискальный чек отправлен на печать.", "OK");
                PluginContext.Log.Info($"Webkassa fiscal receipt printed from closed order screen. OrderId={orderId}, ExternalCheckNumber={record.ExternalCheckNumber}, CheckNumber={record.CheckNumber}");
            }
        }
        catch (Exception error)
        {
            PluginContext.Log.Error($"Webkassa fiscal receipt print failed from closed order screen. OrderId={orderId}, Error={error.GetType().Name}: {error.Message}");
            viewManager.ShowErrorPopup($"Не удалось напечатать чек Webkassa: {error.Message}", "Закрыть");
        }
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
}
