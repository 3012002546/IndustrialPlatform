using System.Xml.Linq;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class ProjectReferenceArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["src/BuildingBlocks/IndustrialPlatform.Application.Abstractions/IndustrialPlatform.Application.Abstractions.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/BuildingBlocks/IndustrialPlatform.EventBus/IndustrialPlatform.EventBus.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/BuildingBlocks/IndustrialPlatform.Infrastructure/IndustrialPlatform.Infrastructure.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.SharedKernel"],
            ["src/BuildingBlocks/IndustrialPlatform.Logging/IndustrialPlatform.Logging.csproj"] = [],
            ["src/BuildingBlocks/IndustrialPlatform.Security/IndustrialPlatform.Security.csproj"] =
                ["IndustrialPlatform.Application.Abstractions"],
            ["src/BuildingBlocks/IndustrialPlatform.SharedKernel/IndustrialPlatform.SharedKernel.csproj"] = [],
            ["src/BuildingBlocks/IndustrialPlatform.Web/IndustrialPlatform.Web.csproj"] =
                ["IndustrialPlatform.Application.Abstractions"],
            ["src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj"] =
                ["IndustrialPlatform.Identity.Application", "IndustrialPlatform.Identity.Infrastructure", "IndustrialPlatform.Web"],
            ["src/Services/Identity/IndustrialPlatform.Identity.Application/IndustrialPlatform.Identity.Application.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.Identity.Domain"],
            ["src/Services/Identity/IndustrialPlatform.Identity.Domain/IndustrialPlatform.Identity.Domain.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/IndustrialPlatform.Identity.Infrastructure.csproj"] =
                ["IndustrialPlatform.Identity.Application", "IndustrialPlatform.Identity.Domain", "IndustrialPlatform.Infrastructure", "IndustrialPlatform.Logging", "IndustrialPlatform.Security"],
            ["src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Api/IndustrialPlatform.ReferenceData.Api.csproj"] =
                ["IndustrialPlatform.ReferenceData.Application", "IndustrialPlatform.ReferenceData.Infrastructure", "IndustrialPlatform.Web"],
            ["src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Application/IndustrialPlatform.ReferenceData.Application.csproj"] =
                ["IndustrialPlatform.Application.Abstractions", "IndustrialPlatform.ReferenceData.Domain"],
            ["src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Domain/IndustrialPlatform.ReferenceData.Domain.csproj"] =
                ["IndustrialPlatform.SharedKernel"],
            ["src/Services/ReferenceData/IndustrialPlatform.ReferenceData.Infrastructure/IndustrialPlatform.ReferenceData.Infrastructure.csproj"] =
                ["IndustrialPlatform.EventBus", "IndustrialPlatform.Infrastructure", "IndustrialPlatform.Logging", "IndustrialPlatform.ReferenceData.Application", "IndustrialPlatform.ReferenceData.Domain"],
        };

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

    [Theory]
    [InlineData("Identity", "ReferenceData")]
    [InlineData("ReferenceData", "Identity")]
    public void ServiceDoesNotReferenceAnotherService(string serviceName, string forbiddenServiceName)
    {
        var serviceRoot = Path.Combine(FindRepositoryRoot(), "src", "Services", serviceName);

        var referencedProjects = Directory
            .EnumerateFiles(serviceRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(ReadProjectReferences);

        Assert.DoesNotContain(referencedProjects, reference =>
            reference.StartsWith($"IndustrialPlatform.{forbiddenServiceName}.", StringComparison.Ordinal));
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        return XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IndustrialPlatform.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
