using System.Xml.Linq;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ProjectReferenceArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/IndustrialPlatform.Application.Abstractions.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.EventBus/IndustrialPlatform.EventBus.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/IndustrialPlatform.Infrastructure.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.SharedKernel"],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.Logging/IndustrialPlatform.Logging.csproj"] = [],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.Security/IndustrialPlatform.Security.csproj"] =
                ["IndustrialPlatform.Application.Abstractions"],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.SharedKernel/IndustrialPlatform.SharedKernel.csproj"] = [],
            ["src/backend/src/BuildingBlocks/IndustrialPlatform.Web/IndustrialPlatform.Web.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.SharedKernel"],
            ["src/backend/src/Gateway/IndustrialPlatform.Gateway/IndustrialPlatform.Gateway.csproj"] =
                ["IndustrialPlatform.Logging", "IndustrialPlatform.Web"],
            ["src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj"] =
                ["IndustrialPlatform.Identity.Api", "IndustrialPlatform.ReferenceData.Api", "IndustrialPlatform.SystemData.Api"],
            ["src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj"] =
                ["IndustrialPlatform.Identity.Application", "IndustrialPlatform.Identity.Contracts", "IndustrialPlatform.Identity.Infrastructure", "IndustrialPlatform.Web"],
            ["src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/IndustrialPlatform.Identity.Application.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.Identity.Contracts", "IndustrialPlatform.Identity.Domain"],
            ["src/backend/src/Services/Identity/IndustrialPlatform.Identity.Contracts/IndustrialPlatform.Identity.Contracts.csproj"] =
                ["IndustrialPlatform.EventBus"],
            ["src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/IndustrialPlatform.Identity.Domain.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/IndustrialPlatform.Identity.Infrastructure.csproj"] =
                ["IndustrialPlatform.EventBus", "IndustrialPlatform.Identity.Application", "IndustrialPlatform.Identity.Domain", "IndustrialPlatform.Infrastructure", "IndustrialPlatform.Logging", "IndustrialPlatform.Security"],
            ["src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj"] =
                ["IndustrialPlatform.ReferenceData.Application", "IndustrialPlatform.ReferenceData.Infrastructure", "IndustrialPlatform.Web"],
            ["src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Application/IndustrialPlatform.ReferenceData.Application.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.ReferenceData.Domain"],
            ["src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Domain/IndustrialPlatform.ReferenceData.Domain.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/backend/src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/IndustrialPlatform.ReferenceData.Infrastructure.csproj"] =
                ["IndustrialPlatform.EventBus", "IndustrialPlatform.Infrastructure", "IndustrialPlatform.Logging", "IndustrialPlatform.ReferenceData.Application", "IndustrialPlatform.ReferenceData.Domain"],
            ["src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/IndustrialPlatform.SystemData.Api.csproj"] =
                ["IndustrialPlatform.SystemData.Application", "IndustrialPlatform.SystemData.Contracts", "IndustrialPlatform.SystemData.Infrastructure", "IndustrialPlatform.Web"],
            ["src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/IndustrialPlatform.SystemData.Application.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.SystemData.Contracts", "IndustrialPlatform.SystemData.Domain"],
            ["src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Contracts/IndustrialPlatform.SystemData.Contracts.csproj"] =
                [],
            ["src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/IndustrialPlatform.SystemData.Domain.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/IndustrialPlatform.SystemData.Infrastructure.csproj"] =
                ["IndustrialPlatform.Infrastructure", "IndustrialPlatform.Logging", "IndustrialPlatform.Security", "IndustrialPlatform.SystemData.Application", "IndustrialPlatform.SystemData.Domain"],
        };

    private static readonly string[] ApprovedSystemDataTestingReferences =
    [
        "IndustrialPlatform.SystemData.Application",
        "IndustrialPlatform.SystemData.Contracts",
        "IndustrialPlatform.SystemData.Domain",
        "IndustrialPlatform.SystemData.Infrastructure",
    ];

    [Fact]
    public void ProductionProjectGraphMatchesApprovedSkeleton()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actualProjectPaths = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedReferences.Keys.Order(StringComparer.Ordinal), actualProjectPaths);

        foreach (var (projectPath, expectedReferences) in ExpectedReferences)
        {
            var actualReferences = ReadProjectReferences(Path.Combine(repositoryRoot, projectPath));

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void SystemDataTestingFixtureReferencesOnlyApprovedProjects()
    {
        // 共享测试 fixture 只允许引用 SystemData 四层应用项目,禁止引用测试项目或其他服务/组件。
        var repositoryRoot = FindRepositoryRoot();
        const string fixtureProjectPath =
            "tests/SystemData/IndustrialPlatform.SystemData.Testing/IndustrialPlatform.SystemData.Testing.csproj";

        var actualReferences = ReadProjectReferences(Path.Combine(repositoryRoot, fixtureProjectPath));

        Assert.Equal(ApprovedSystemDataTestingReferences.Order(StringComparer.Ordinal), actualReferences);
    }

    [Theory]
    [InlineData("Identity", "ReferenceData")]
    [InlineData("ReferenceData", "Identity")]
    [InlineData("Identity", "SystemData")]
    [InlineData("ReferenceData", "SystemData")]
    [InlineData("SystemData", "Identity")]
    [InlineData("SystemData", "ReferenceData")]
    public void ServiceDoesNotReferenceAnotherService(string serviceName, string forbiddenServiceName)
    {
        var serviceRoot = Path.Combine(FindRepositoryRoot(), "src", "backend", "src", "Services", serviceName);

        var referencedProjects = Directory
            .EnumerateFiles(serviceRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(ReadProjectReferences);

        Assert.DoesNotContain(referencedProjects, reference =>
            reference.StartsWith($"IndustrialPlatform.{forbiddenServiceName}.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"..\IndustrialPlatform.SharedKernel\IndustrialPlatform.SharedKernel.csproj", "../IndustrialPlatform.SharedKernel/IndustrialPlatform.SharedKernel.csproj")]
    [InlineData("../IndustrialPlatform.SharedKernel/IndustrialPlatform.SharedKernel.csproj", "../IndustrialPlatform.SharedKernel/IndustrialPlatform.SharedKernel.csproj")]
    public void ProjectReferencePathsUsePortableSeparators(string projectReferencePath, string expected)
    {
        Assert.Equal(expected, NormalizeProjectReferencePath(projectReferencePath));
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        return XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileNameWithoutExtension(NormalizeProjectReferencePath(path!)))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeProjectReferencePath(string path) => path.Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "backend", "IndustrialPlatform.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
