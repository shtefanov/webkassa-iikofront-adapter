# Windows Codex Handoff Prompt

Project name: Webkassa iikoFront fiscal adapter spike

Goal:
Prepare Windows development environment and create the first compile-level
iikoFront fiscal-register adapter spike for Webkassa integration.

Current gateway project path:

```text
/home/shtefanov/projects/webkassa
```

Windows project path to use:

```text
C:\OpenClaw\work\webkassa-iikofront-adapter
```

Windows access:

```text
SSH alias: windows
Host: OPENCLAW-WORKER / 192.168.10.183
User: wiki
Auth: gateway SSH key, safe path ~/.ssh/id_ed25519_openclaw_windows
```

Allowed Windows roots:

```text
C:\OpenClaw\work
C:\OpenClaw\tools
C:\OpenClaw\logs
C:\OpenClaw\handoff
```

Do not modify:

```text
Existing iiko/Webkassa production/config/business data
```

Allowed work:

1. Install Visual Studio 2022 Build Tools or Visual Studio 2022 Community with:
   - .NET desktop build tools
   - MSBuild
   - .NET Framework 4.7.2 targeting pack
   - NuGet restore support
2. Install Git for Windows if missing.
3. Create isolated folder `C:\OpenClaw\work\webkassa-iikofront-adapter`.
4. Create SDK-style `net472` C# project referencing:
   - `Resto.Front.Api.V9` version `9.5.6059`
   - `Microsoft.NETFramework.ReferenceAssemblies.net472` version `1.0.3`
   - `Newtonsoft.Json` version `13.0.3` if JSON is needed
5. Create minimal plugin skeleton:
   - `Plugin : MarshalByRefObject, IFrontPlugin`
   - register `ICashRegisterFactory` via `PluginContext.Operations.RegisterCashRegisterFactory(...)`
   - implement factory and stub `ICashRegister` using exact V9 signatures from IDE/NuGet
   - all fiscal write methods must throw a controlled `DeviceException` until mapping is implemented
6. Build with MSBuild/dotnet restore/build.

Validation commands to report:

```powershell
dotnet --info
msbuild -version
dotnet restore
dotnet build
```

What to report back:

- installed tools and versions;
- exact Resto.Front.Api.V9 signatures required by `ICashRegister`;
- whether the skeleton compiles;
- output path;
- any missing SDK/targeting pack errors;
- no raw secrets.

Safety:

- Do not deploy to iikoFront.
- Do not change iikoOffice equipment settings.
- Do not run live fiscal operations.
- Do not store Webkassa credentials in files.
