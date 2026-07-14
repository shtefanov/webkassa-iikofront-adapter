using System;
using Resto.Front.Api.Data.Device.Settings;
using Resto.Front.Api.Devices;

namespace Resto.Front.Api.Webkassa.V9;

public sealed class WebkassaCashRegisterFactory : MarshalByRefObject, ICashRegisterFactory
{
    public string FactoryCode => "WebkassaFiscalAdapterSpike";

    public string Description => "Webkassa fiscal adapter";

    public DeviceSettings DefaultDeviceSettings => new CashRegisterSettings
    {
        FriendlyName = "Webkassa Fiscal Adapter",
        FactoryCode = FactoryCode,
        Description = Description,
        Autorun = true,
    };

    public ICashRegister Create(Guid deviceId, CashRegisterSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        return new WebkassaCashRegister(deviceId, settings);
    }
}
