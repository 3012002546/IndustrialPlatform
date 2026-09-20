using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Models;

/// <summary>一名 MES 人员在独立协作宿主中的租户范围和目录投影。</summary>
public sealed record DirectoryUserWithTenant(string TenantNId, DirectoryUser User);
