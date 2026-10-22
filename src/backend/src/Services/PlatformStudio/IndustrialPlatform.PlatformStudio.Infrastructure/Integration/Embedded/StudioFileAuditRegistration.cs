using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Auditing;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Persistence;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Infrastructure.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>最小真实运行组件装配；不注册SystemData/通知/聊天/租户控制面。</summary>
public static class StudioFileAuditRegistration
{
    public static IServiceCollection AddStudioEmbeddedFileAudit(this IServiceCollection services, StudioDeploymentProfile profile)
    {
        if (profile.DeploymentMode != "ExternalMes" || profile.Audit.Provider != "Embedded") throw new StudioException("STUDIO_VALIDATION", "Embedded文件审计仅用于ExternalMes。");
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<StudioRuntimeDatabase>();
        services.AddSingleton<FileAuditOperationalStore>(sp => new(sp.GetRequiredService<StudioRuntimeDatabase>().Context));
        services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<FileAuditOperationalStore>());
        services.AddSingleton<ISystemDataWriteTransaction>(sp => new RuntimeWriteTransaction(sp.GetRequiredService<StudioRuntimeDatabase>().Context));
        services.AddSingleton<ILocalAuditCommand, RuntimeLocalAuditCommand>();
        // Delivery failure is retained in studio_audit_outbox; no separate failure spool can lose the original event.
        services.AddSingleton<IAuditService>(sp => new AuditService(sp.GetRequiredService<IAuditStore>(), sp.GetRequiredService<TimeProvider>(), transaction: sp.GetRequiredService<ISystemDataWriteTransaction>()));
        services.AddSingleton<IAuditFilteredQueries>(sp => (IAuditFilteredQueries)sp.GetRequiredService<IAuditService>());
        if (profile.Files.Provider == "Embedded")
        {
            if (string.IsNullOrWhiteSpace(profile.Files.Root) || !Path.IsPathFullyQualified(profile.Files.Root) || Path.GetFullPath(profile.Files.Root).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("wwwroot", StringComparer.OrdinalIgnoreCase)) throw new StudioException("STUDIO_VALIDATION", "文件Root必须是wwwroot以外的绝对受控目录。");
            var root = Path.GetFullPath(profile.Files.Root);
            services.AddDataProtection().SetApplicationName("IndustrialPlatform.PlatformStudio");
            services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<FileAuditOperationalStore>());
            services.AddSingleton<IFileStatusOutbox>(sp => sp.GetRequiredService<FileAuditOperationalStore>());
            services.AddSingleton<IFileContentStore>(_ => new LocalFileContentStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SystemData:FileStorageRoot"] = root }).Build()));
            services.AddSingleton<IFileUploadCoordinator>(_ => new LocalFileUploadCoordinator(Options.Create(new SystemDataFileStorageOptions { FileStorageRoot = root })));
            services.AddSingleton<IUploadResumeTicketService, ProtectedUploadResumeTickets>();
            services.AddSingleton<IFileScanner>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var endpoint = profile.Files.ScannerEndpointKey;
                var host = string.IsNullOrWhiteSpace(endpoint) ? null : configuration[$"PlatformStudio:ScannerEndpoints:{endpoint}:Host"];
                var port = string.IsNullOrWhiteSpace(endpoint) ? null : configuration[$"PlatformStudio:ScannerEndpoints:{endpoint}:Port"];
                return new ClamAvFileScanner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SystemData:ClamAv:Host"] = host, ["SystemData:ClamAv:Port"] = port ?? "3310", ["SystemData:ClamAv:DevelopmentBypass"] = "false" }).Build());
            });
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<IOwnedFileService>(sp => new OwnedFileService(sp.GetRequiredService<StudioRuntimeDatabase>().Context, sp.GetRequiredService<IFileService>(), sp.GetRequiredService<IFileStore>(), sp.GetRequiredService<IFileContentStore>(), sp.GetRequiredService<FileAuditOperationalStore>(), sp.GetRequiredService<ISystemDataWriteTransaction>(), sp.GetRequiredService<ILocalAuditCommand>(), sp.GetRequiredService<IFileUploadCoordinator>(), sp.GetRequiredService<IDataProtectionProvider>(), sp.GetRequiredService<TimeProvider>()));
            services.AddHostedService<FileLifecycleHostedService>();
            services.AddScoped<IStudioFileStatusConsumer, StudioFileStatusAuditConsumer>();
            services.AddHostedService<StudioFileStatusDispatcher>();
        }
        return services.AddStudioFileAuditConsumers(profile);
    }

    public static IServiceCollection AddStudioPlatformFileAudit(this IServiceCollection services, StudioDeploymentProfile profile)
    {
        if (profile.DeploymentMode != "PlatformNative" || profile.Audit.Provider != "Platform") throw new StudioException("STUDIO_VALIDATION", "Platform适配组合无效。");
        // Register the owned service in the platform File owner's DI; do not replace its chat-aware IFileStore.
        services.TryAddSingleton<FileAuditOperationalStore>(sp => sp.GetRequiredService<IFileStore>() as FileAuditOperationalStore ?? throw new StudioException("STUDIO_FILES_UNAVAILABLE", "平台File公开运行服务尚未注册。", 503));
        if (profile.Files.Provider == "Platform")
        {
            services.AddDataProtection();
            services.TryAddSingleton<IOwnedFileService, OwnedFileService>();
        }
        return services.AddStudioFileAuditConsumers(profile);
    }

    private static IServiceCollection AddStudioFileAuditConsumers(this IServiceCollection services, StudioDeploymentProfile profile)
    {
        services.AddScoped<IStudioAudit, StudioAuditDelivery>();
        services.AddHostedService<StudioAuditDispatcher>();
        services.AddScoped<IStudioFiles, StudioFiles>();
        services.AddScoped<StudioFileTransfer>();
        services.AddScoped<IStudioOwnedFiles, StudioOwnedFiles>();
        services.AddScoped<StudioFileCapabilityProbe>();
        if (profile.Files.Provider != "Disabled")
        {
            services.AddScoped<IStudioFileScopeAuthorizer, StudioFileScopeAuthorizer>();
            services.AddScoped<IStudioFileTransfer, ScopedFileTransfer>();
        }
        return services;
    }
}

/// <summary>独立DbContext保留RuntimeSupport注解，不将其映射安装进Studio实体Scope。</summary>
public sealed class StudioRuntimeDatabase : IDisposable
{
    public SqlSugarDbContext Context { get; }
    public StudioRuntimeDatabase(StudioDatabase database) => Context = new(Options.Create(new SqlSugarOptions { DbType = database.Provider, ConnectionString = database.ConnectionString, CommandTimeoutSeconds = 30 }));
    public void Dispose() => Context.Dispose();
}
