# Industrial Platform Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a documented, Git-versioned .NET 10 monorepo skeleton for BuildingBlocks, Identity, and ReferenceData that restores, builds, and tests successfully.

**Architecture:** Use one `IndustrialPlatform.slnx` at the repository root. Shared capabilities live under `src/BuildingBlocks`; Identity and ReferenceData are isolated four-layer services under `src/Services`, with tests grouped by capability under `tests`. The repository contains normalized copies of the original blueprint and implementation documents while leaving source material outside the repository unchanged.

**Tech Stack:** .NET SDK 10.0.302, `net10.0`, ASP.NET Core minimal API, xUnit, MSBuild central properties, NuGet central package management, GitHub Actions.

## Global Constraints

- Repository root is `D:\Code\Industrial Platform\IndustrialPlatform` and repository name is `IndustrialPlatform`.
- Preserve all files in `D:\Code\Industrial Platform\个人MES平台开发设计` and `D:\Code\Industrial Platform\开发实施` unchanged.
- Use `src/...` and `tests/...`; never create `src/backend/src`.
- Use `Api` consistently as the API project suffix.
- ReferenceData and MasterData remain separate bounded contexts; ReferenceData precedes MasterData.
- Create compile-ready skeletons only; do not implement authentication, dictionaries, EAV, Redis, RabbitMQ, SqlSugar, or production logging.
- All projects target `net10.0`, enable nullable reference types and implicit usings, and use centralized package versions.
- Each task ends with an independently reviewable commit.

---

## File Map

- `README.md`: repository purpose, current scope, quick-start commands, service order.
- `global.json`: pin SDK feature band to 10.0.302 with latest patch roll-forward.
- `Directory.Build.props`: shared target framework and compiler settings.
- `Directory.Packages.props`: central versions for test packages only.
- `.editorconfig`: whitespace and C# style baseline.
- `.gitignore`: .NET, IDE, test, secret, and local artifact exclusions.
- `IndustrialPlatform.slnx`: single solution containing all production and test projects.
- `docs/blueprint/README.md`: blueprint catalog, status, dependencies, and reading order.
- `docs/blueprint/*.md`: normalized copies of all blueprint source files.
- `docs/implementation/README.md`: implementation sequence and scope status.
- `docs/implementation/01-*.md` through `04-*.md`: normalized implementation documents.
- `.github/workflows/build.yml`: restore, release build, and test on pushes and pull requests.
- `src/BuildingBlocks/*/*.csproj`: seven independent shared libraries.
- `src/Services/{Identity,ReferenceData}/*/*.csproj`: four-layer service projects.
- `src/Services/{Identity,ReferenceData}/*/AssemblyMarker.cs`: minimal discoverable assembly marker per class library.
- `src/Services/{Identity,ReferenceData}/*.Api/Program.cs`: minimal API with `/health` endpoint.
- `tests/*/*.Tests.csproj`: xUnit smoke-test projects.
- `tests/*/*Tests.cs`: assembly and API-boundary smoke tests.

---

### Task 1: Establish Repository Foundation

**Files:**
- Create: `.gitignore`
- Create: `.editorconfig`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `README.md`
- Create: `IndustrialPlatform.slnx`

**Interfaces:**
- Consumes: approved design in `docs/superpowers/specs/2026-08-01-industrial-platform-bootstrap-design.md`.
- Produces: repository-wide SDK, compiler, package, naming, and solution conventions used by every later task.

- [ ] **Step 1: Create a local CLI home and verify the pinned SDK exists**

Run:

```powershell
New-Item -ItemType Directory -Force .\.dotnet_cli_home | Out-Null
$env:DOTNET_CLI_HOME = (Resolve-Path .\.dotnet_cli_home)
dotnet --list-sdks
```

Expected: output contains `10.0.302`.

- [ ] **Step 2: Add repository configuration**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

Create `IndustrialPlatform.slnx`:

```xml
<Solution />
```

Create `.gitignore` with `bin/`, `obj/`, `.vs/`, `.idea/`, `TestResults/`, `*.user`, `*.suo`, `.env`, `appsettings.*.local.json`, `.dotnet_cli_home/`, and generated coverage files. Create `.editorconfig` with UTF-8, final newline, four spaces for C# and two spaces for XML/JSON/YAML/Markdown.

- [ ] **Step 3: Add repository README**

Document these exact facts: current deliverable is skeleton-only; service order is BuildingBlocks → Identity → ReferenceData → MasterData; `dotnet restore IndustrialPlatform.slnx`, `dotnet build IndustrialPlatform.slnx --no-restore`, and `dotnet test IndustrialPlatform.slnx --no-build` are the supported verification commands.

- [ ] **Step 4: Validate foundation files**

Run:

```powershell
dotnet --version
dotnet sln IndustrialPlatform.slnx list
git diff --check
```

Expected: SDK is `10.0.302`; solution reports no projects; diff check emits no errors.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore .editorconfig global.json Directory.Build.props Directory.Packages.props README.md IndustrialPlatform.slnx
git commit -m "chore: establish repository foundation"
```

---

### Task 2: Normalize Blueprint and Implementation Documents

**Files:**
- Create: `docs/blueprint/README.md`
- Create: `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md` through `docs/blueprint/31-Industrial Platform权限体系与安全架构设计.md`
- Create: `docs/blueprint/后续设计.md`
- Create: `docs/implementation/README.md`
- Create: `docs/implementation/01-Industrial Platform开发启动实施方案.md` through `docs/implementation/04-Industrial Platform ReferenceData Service开发实施方案.md`

**Interfaces:**
- Consumes: read-only documents from the two parent directories.
- Produces: repository-local documentation source of truth for project paths and service order.

- [ ] **Step 1: Copy all source documents without changing the originals**

Use `Copy-Item` to copy the 32 blueprint files and 4 implementation files into their target directories, replacing only the final `.md.md` suffix with `.md`. Confirm source and destination counts are 32 and 4 respectively.

- [ ] **Step 2: Add the blueprint catalog**

Create `docs/blueprint/README.md` as a table with columns `编号`, `文档`, `状态`, `前置阅读`, `说明`. Mark documents 01–13 and 15–31 as `已整理/规划`, document 14 as `已整理/后续阶段`, and `后续设计.md` as `路线参考`. Add a “服务顺序” section that explicitly states BuildingBlocks → Identity → ReferenceData → MasterData and explains that ReferenceData owns dictionary/configuration/metadata/coding rules while MasterData owns material/equipment/organization/BOM.

- [ ] **Step 3: Correct high-impact blueprint conflicts**

Apply these exact mechanical corrections in repository copies only:

- Remove a trailing `.md` from first-level headings that repeat the filename.
- Replace all intended repository paths `src/backend/src/...` with `src/...`.
- In the overall architecture, roadmap, directory, initialization, Identity, and MasterData documents, insert ReferenceData before MasterData wherever the platform service implementation order is enumerated.
- Do not replace legitimate prose that merely mentions MasterData; only change ordered service lists and dependency diagrams.
- Add a short “ReferenceData 边界” note to the overall architecture and MasterData detailed design using the definitions in Task 2 Step 2.

- [ ] **Step 4: Normalize implementation documents**

Create `docs/implementation/README.md` listing 01 as repository bootstrap, 02 as BuildingBlocks skeleton, 03 as Identity skeleton, and 04 as ReferenceData skeleton. Change the first heading in document 04 from `03-...` to `04-...`; replace `src/backend/src` with `src`; use `.Api` consistently in project names; add a scope banner to documents 02–04 stating that the current milestone creates project skeletons only and defers business implementation.

- [ ] **Step 5: Verify documentation invariants**

Run:

```powershell
rg -n "\.md\.md|src/backend/src|^# 03-Industrial Platform ReferenceData" docs/blueprint docs/implementation
rg -n "ReferenceData|MasterData" docs/blueprint/README.md docs/implementation/README.md
(Get-ChildItem docs/blueprint -File).Count
(Get-ChildItem docs/implementation -File).Count
```

Expected: first search has no matches; service-boundary search has matches; counts are 33 blueprint files including the index and 5 implementation files including the index.

- [ ] **Step 6: Commit**

```powershell
git add docs/blueprint docs/implementation
git commit -m "docs: normalize platform blueprint"
```

---

### Task 3: Scaffold BuildingBlocks and Its Smoke Tests

**Files:**
- Create: `src/BuildingBlocks/IndustrialPlatform.{SharedKernel,Application.Abstractions,Infrastructure,EventBus,Logging,Web,Security}/*.csproj`
- Create: `src/BuildingBlocks/*/AssemblyMarker.cs`
- Create: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj`
- Create: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/AssemblyBoundaryTests.cs`
- Modify: `IndustrialPlatform.slnx`

**Interfaces:**
- Consumes: central build and package configuration from Task 1.
- Produces: seven shared assemblies; service Domain projects consume `IndustrialPlatform.SharedKernel`, Application projects consume `IndustrialPlatform.Application.Abstractions`, and API projects consume `IndustrialPlatform.Web`.

- [ ] **Step 1: Write the smoke test before creating production projects**

Create the xUnit test project and `AssemblyBoundaryTests.cs`:

```csharp
namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class AssemblyBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.SharedKernel")]
    [InlineData("IndustrialPlatform.Application.Abstractions")]
    [InlineData("IndustrialPlatform.Infrastructure")]
    [InlineData("IndustrialPlatform.EventBus")]
    [InlineData("IndustrialPlatform.Logging")]
    [InlineData("IndustrialPlatform.Web")]
    [InlineData("IndustrialPlatform.Security")]
    public void RequiredAssemblyCanBeLoaded(string assemblyName)
    {
        Assert.Equal(assemblyName, System.Reflection.Assembly.Load(assemblyName).GetName().Name);
    }
}
```

The test project references all seven target projects after they are created and uses centrally versioned test packages with `PrivateAssets="all"` for runner and coverage packages.

- [ ] **Step 2: Verify the test cannot build yet**

Run `dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj`.

Expected: FAIL because the seven referenced projects do not exist.

- [ ] **Step 3: Create the seven class libraries**

Run `dotnet new classlib --framework net10.0 --no-restore` for each exact project name, remove generated `Class1.cs`, and add this marker with the namespace matching each project:

```csharp
namespace IndustrialPlatform.SharedKernel;

public static class AssemblyMarker;
```

Repeat with the six other exact namespaces. Add all production and test projects to `IndustrialPlatform.slnx`.

- [ ] **Step 4: Add only necessary BuildingBlocks references**

- `Application.Abstractions` → `SharedKernel`
- `Infrastructure` → `Application.Abstractions`, `SharedKernel`
- `EventBus` → `SharedKernel`
- `Web` → `Application.Abstractions`
- `Security` → `Application.Abstractions`
- `Logging` has no project reference.

- [ ] **Step 5: Run BuildingBlocks tests**

Run:

```powershell
dotnet restore IndustrialPlatform.slnx
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --no-restore
```

Expected: seven theory cases pass.

- [ ] **Step 6: Commit**

```powershell
git add src/BuildingBlocks tests/BuildingBlocks IndustrialPlatform.slnx
git commit -m "feat: scaffold building blocks projects"
```

---

### Task 4: Scaffold Identity Service and Tests

**Files:**
- Create: `src/Services/Identity/IndustrialPlatform.Identity.{Api,Application,Domain,Infrastructure}/*`
- Create: `tests/Identity/IndustrialPlatform.Identity.Tests/*`
- Modify: `IndustrialPlatform.slnx`

**Interfaces:**
- Consumes: SharedKernel, Application.Abstractions, Infrastructure, Web, Logging, and Security assemblies.
- Produces: Identity four-layer assembly boundary and an HTTP `/health` endpoint returning status 200 with JSON `{ "status": "Healthy", "service": "Identity" }`.

- [ ] **Step 1: Write the failing Identity boundary test**

Create `ServiceBoundaryTests.cs`:

```csharp
namespace IndustrialPlatform.Identity.Tests;

public sealed class ServiceBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.Identity.Domain")]
    [InlineData("IndustrialPlatform.Identity.Application")]
    [InlineData("IndustrialPlatform.Identity.Infrastructure")]
    [InlineData("IndustrialPlatform.Identity.Api")]
    public void RequiredAssemblyCanBeLoaded(string assemblyName)
    {
        Assert.Equal(assemblyName, System.Reflection.Assembly.Load(assemblyName).GetName().Name);
    }
}
```

- [ ] **Step 2: Verify the test cannot build yet**

Run the Identity test project and expect missing project-reference failures.

- [ ] **Step 3: Create Identity projects and markers**

Create Domain, Application, and Infrastructure with `dotnet new classlib`; create Api with `dotnet new webapi --use-controllers false --no-openapi --no-https --framework net10.0 --no-restore`. Remove template sample endpoints and add:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Identity" }));
app.Run();

public partial class Program;
```

Add `AssemblyMarker` classes to the three class libraries and add all projects to the solution.

- [ ] **Step 4: Set Identity references**

- Domain → SharedKernel
- Application → Domain, Application.Abstractions
- Infrastructure → Application, Domain, shared Infrastructure, Logging, Security
- Api → Application, Infrastructure, Web
- Tests → all four Identity projects

- [ ] **Step 5: Run Identity tests and compile the API**

Run `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj` and `dotnet build src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj --no-restore`.

Expected: four theory cases pass and API build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/Services/Identity tests/Identity IndustrialPlatform.slnx
git commit -m "feat: scaffold identity service"
```

---

### Task 5: Scaffold ReferenceData Service and Tests

**Files:**
- Create: `src/Services/ReferenceData/IndustrialPlatform.ReferenceData.{Api,Application,Domain,Infrastructure}/*`
- Create: `tests/ReferenceData/IndustrialPlatform.ReferenceData.Tests/*`
- Modify: `IndustrialPlatform.slnx`

**Interfaces:**
- Consumes: SharedKernel, Application.Abstractions, Infrastructure, Web, Logging, and EventBus assemblies.
- Produces: ReferenceData four-layer assembly boundary and `/health` returning status 200 with JSON `{ "status": "Healthy", "service": "ReferenceData" }`; produces no direct Identity or MasterData reference.

- [ ] **Step 1: Write the failing ReferenceData boundary test**

Create `ServiceBoundaryTests.cs`:

```csharp
namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class ServiceBoundaryTests
{
    [Theory]
    [InlineData("IndustrialPlatform.ReferenceData.Domain")]
    [InlineData("IndustrialPlatform.ReferenceData.Application")]
    [InlineData("IndustrialPlatform.ReferenceData.Infrastructure")]
    [InlineData("IndustrialPlatform.ReferenceData.Api")]
    public void RequiredAssemblyCanBeLoaded(string assemblyName)
    {
        Assert.Equal(assemblyName, System.Reflection.Assembly.Load(assemblyName).GetName().Name);
    }
}
```

- [ ] **Step 2: Verify the test cannot build yet**

Run the ReferenceData test project and expect missing project-reference failures.

- [ ] **Step 3: Create ReferenceData projects and markers**

Create Domain, Application, and Infrastructure with `dotnet new classlib --framework net10.0 --no-restore`; create Api with `dotnet new webapi --use-controllers false --no-openapi --no-https --framework net10.0 --no-restore`. Use exact `IndustrialPlatform.ReferenceData.*` project names, remove generated `Class1.cs` and template sample endpoints, add namespace-matching `AssemblyMarker` classes to the three class libraries, and add all four projects to the solution. The API `Program.cs` is:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ReferenceData" }));
app.Run();

public partial class Program;
```

- [ ] **Step 4: Set ReferenceData references**

- Domain → SharedKernel
- Application → Domain, Application.Abstractions
- Infrastructure → Application, Domain, shared Infrastructure, Logging, EventBus
- Api → Application, Infrastructure, Web
- Tests → all four ReferenceData projects

Use `dotnet list <project> reference` to confirm there is no Identity or MasterData reference.

- [ ] **Step 5: Run ReferenceData tests and compile the API**

Run the ReferenceData test project and API build. Expected: four theory cases pass and API build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/Services/ReferenceData tests/ReferenceData IndustrialPlatform.slnx
git commit -m "feat: scaffold reference data service"
```

---

### Task 6: Add CI and Perform Repository-Level Verification

**Files:**
- Create: `.github/workflows/build.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: complete solution from Tasks 1–5.
- Produces: repeatable local and GitHub-hosted verification of restore, build, and tests.

- [ ] **Step 1: Add the GitHub Actions workflow**

Create `.github/workflows/build.yml` triggered by `push` and `pull_request`, with `permissions: contents: read`, `actions/setup-dotnet@v5` using `global-json-file: global.json`, and these commands:

```yaml
- run: dotnet restore IndustrialPlatform.slnx
- run: dotnet build IndustrialPlatform.slnx --configuration Release --no-restore
- run: dotnet test IndustrialPlatform.slnx --configuration Release --no-build --logger trx
```

- [ ] **Step 2: Document repository status**

Update `README.md` to link `docs/blueprint/README.md`, `docs/implementation/README.md`, the approved design, and this plan. State explicitly that business functions remain future work.

- [ ] **Step 3: Run full verification from a clean build state**

Run:

```powershell
dotnet clean IndustrialPlatform.slnx
dotnet restore IndustrialPlatform.slnx
dotnet build IndustrialPlatform.slnx --configuration Release --no-restore
dotnet test IndustrialPlatform.slnx --configuration Release --no-build --logger "console;verbosity=normal"
git diff --check
git status --short
```

Expected: restore succeeds; build has zero errors and zero warnings; all 15 smoke-test cases pass; diff check is silent; status contains only Task 6 files before commit.

- [ ] **Step 4: Audit references and documentation**

Run:

```powershell
dotnet sln IndustrialPlatform.slnx list
rg -n "\.md\.md|src/backend/src|^# 03-Industrial Platform ReferenceData" docs/blueprint docs/implementation
rg -n "Identity" src/Services/ReferenceData
rg -n "ReferenceData" src/Services/Identity
```

Expected: solution lists 18 projects (7 BuildingBlocks, 8 service, 3 test); all three searches after the solution listing have no matches.

- [ ] **Step 5: Commit the verified CI state**

```powershell
git add .github/workflows/build.yml README.md
git commit -m "ci: verify platform solution"
git status --short --branch
```

Expected: branch is clean. GitHub remote creation and push remain a separate, user-authorized action after local verification.
