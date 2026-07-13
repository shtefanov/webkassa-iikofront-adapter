using System.Reflection;

var packageDll = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable("IIKO_FRONT_API_V9_DLL");

if (string.IsNullOrWhiteSpace(packageDll))
{
    var frontNetPath = Environment.GetEnvironmentVariable("IIKO_FRONT_NET_PATH");
    packageDll = string.IsNullOrWhiteSpace(frontNetPath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "iiko",
            "iikoRMS",
            "Front.Net",
            "Resto.Front.Api.V9.dll")
        : Path.Combine(frontNetPath, "Resto.Front.Api.V9.dll");
}

if (!File.Exists(packageDll))
    throw new FileNotFoundException("Resto.Front.Api.V9.dll was not found. Pass the DLL path as the first argument or set IIKO_FRONT_API_V9_DLL.", packageDll);

Console.WriteLine($"# Assembly path: {packageDll}");
Console.WriteLine();
var assembly = Assembly.LoadFrom(packageDll);

var typeNames = new[]
{
    "Resto.Front.Api.Devices.ICashRegisterFactory",
    "Resto.Front.Api.Devices.ICashRegister",
    "Resto.Front.Api.Devices.IDeviceFactory",
    "Resto.Front.Api.Devices.IDevice",
    "Resto.Front.Api.IOperationService",
    "Resto.Front.Api.Data.Assortment.IProduct",
    "Resto.Front.Api.Data.Assortment.IProductGroup",
    "Resto.Front.Api.Data.Assortment.IProductSize",
    "Resto.Front.Api.Data.Assortment.IBarcodeContainer",
    "Resto.Front.Api.Data.Device.Tasks.ChequeTask",
    "Resto.Front.Api.Data.Device.Settings.CashRegisterSettings",
    "Resto.Front.Api.Data.Device.Settings.CashRegisterDriverParameters",
    "Resto.Front.Api.Data.Device.Results.CashRegisterResult",
    "Resto.Front.Api.Data.Device.Results.CashRegisterStatus",
    "Resto.Front.Api.Data.Device.Results.DeviceInfo",
    "Resto.Front.Api.Data.Device.Results.DirectIoResult",
    "Resto.Front.Api.Data.Device.Results.QueryInfoResult",
    "Resto.Front.Api.Data.Device.Results.SupportedCommand",
    "Resto.Front.Api.Data.Device.Results.RequiredParameter",
    "Resto.Front.Api.Data.Device.State",
};

var visited = new HashSet<string>(StringComparer.Ordinal);

foreach (var typeName in typeNames)
{
    var type = assembly.GetType(typeName, throwOnError: false);
    PrintType(typeName, type, visited);
}

Console.WriteLine("# ChequeTask related property types");
var chequeTaskType = assembly.GetType("Resto.Front.Api.Data.Device.Tasks.ChequeTask", throwOnError: false);
if (chequeTaskType is not null)
{
    foreach (var relatedType in chequeTaskType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Select(property => Unwrap(property.PropertyType))
                 .Where(type => type.Assembly == assembly)
                 .OrderBy(type => type.FullName))
    {
        PrintType(relatedType.FullName ?? relatedType.Name, relatedType, visited);
    }
}

static string FormatType(Type? type)
{
    if (type is null) return "null";
    if (!type.IsGenericType) return type.FullName ?? type.Name;
    var name = type.GetGenericTypeDefinition().FullName ?? type.Name;
    var tick = name.IndexOf('`');
    if (tick >= 0) name = name[..tick];
    return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
}

static Type Unwrap(Type type)
{
    if (type.IsArray)
        return Unwrap(type.GetElementType()!);

    if (type.IsGenericType)
    {
        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(ICollection<>) ||
            definition == typeof(IReadOnlyCollection<>) ||
            definition == typeof(IEnumerable<>) ||
            definition == typeof(IList<>) ||
            definition == typeof(IReadOnlyList<>) ||
            definition == typeof(List<>))
        {
            return Unwrap(type.GetGenericArguments()[0]);
        }
    }

    return type;
}

static void PrintType(string requestedName, Type? type, HashSet<string> visited)
{
    Console.WriteLine($"# {requestedName}");

    if (type is null)
    {
        Console.WriteLine("NOT FOUND");
        Console.WriteLine();
        return;
    }

    var visitKey = type.FullName ?? type.Name;
    if (!visited.Add(visitKey))
    {
        Console.WriteLine("Already printed.");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"Assembly: {type.Assembly.FullName}");
    Console.WriteLine($"FullName: {type.FullName}");
    Console.WriteLine($"Base: {FormatType(type.BaseType)}");
    var interfaces = type.GetInterfaces().Select(FormatType).OrderBy(x => x).ToArray();
    if (interfaces.Length > 0)
        Console.WriteLine($"Interfaces: {string.Join(", ", interfaces)}");

    if (type.IsEnum)
    {
        Console.WriteLine($"Enum values: {string.Join(", ", Enum.GetNames(type))}");
    }

    Console.WriteLine("Properties:");
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name))
    {
        var accessor = property.CanWrite ? "{ get; set; }" : "{ get; }";
        Console.WriteLine($"  {FormatType(property.PropertyType)} {property.Name} {accessor}");
    }

    Console.WriteLine("Constructors:");
    foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                 .OrderBy(ctor => ctor.GetParameters().Length))
    {
        var parameters = constructor.GetParameters()
            .Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}")
            .ToArray();
        Console.WriteLine($"  .ctor({string.Join(", ", parameters)})");
    }

    Console.WriteLine("Methods:");
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                 .Where(method => !method.IsSpecialName)
                 .Where(method => type.Name != "IOperationService" || IsRelevantOperationServiceMethod(method))
                 .OrderBy(method => method.Name)
                 .ThenBy(method => method.GetParameters().Length))
    {
        var parameters = method.GetParameters()
            .Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}")
            .ToArray();
        Console.WriteLine($"  {FormatType(method.ReturnType)} {method.Name}({string.Join(", ", parameters)})");
    }

    Console.WriteLine();
}

static bool IsRelevantOperationServiceMethod(MethodInfo method)
{
    if (method.Name.Contains("CashRegisterFactory", StringComparison.Ordinal))
        return true;

    if (method.Name.Contains("Product", StringComparison.Ordinal) ||
        method.Name.Contains("Assortment", StringComparison.Ordinal) ||
        method.Name.Contains("Nomenclature", StringComparison.Ordinal) ||
        method.Name.Contains("StopList", StringComparison.Ordinal))
        return true;

    return method.GetParameters().Any(parameter =>
    {
        var parameterType = FormatType(parameter.ParameterType);
        return parameterType.Contains("Product", StringComparison.Ordinal) ||
               parameterType.Contains("Assortment", StringComparison.Ordinal) ||
               parameterType.Contains("Nomenclature", StringComparison.Ordinal) ||
               parameterType.Contains("StopList", StringComparison.Ordinal);
    });
}
