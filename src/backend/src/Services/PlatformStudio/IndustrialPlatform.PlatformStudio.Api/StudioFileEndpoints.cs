using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>公共上传控件的Studio端点适配；不暴露全租户文件管理列表。</summary>
internal static class StudioFileEndpoints
{
    private static JsonSerializerOptions JsonOptions => StudioHttpJson.Options;
    private static IResult Json(object? value)=>Results.Json(ApiResult.Ok(value),JsonOptions);
    public static void MapStudioFiles(this RouteGroupBuilder group)
    {
        group.MapPost("/files/upload-sessions",async(JsonObject request,StudioFileTransfer files,CancellationToken ct)=>
        {
            var usage=request["usage"]?.Deserialize<StudioFileUsageV1>(JsonOptions)??throw new StudioException("STUDIO_FILE_USAGE_INVALID","上传用途缺失。",422);
            var copy=request.DeepClone().AsObject();copy.Remove("usage");
            var input=copy.Deserialize<CreateUploadSessionRequest>(JsonOptions)??throw new StudioException("STUDIO_VALIDATION","上传信息缺失。");
            return Json(await files.CreateSessionAsync(input,usage,ct));
        });
        group.MapGet("/files/upload-sessions/{id}",async(string id,StudioFileTransfer files,CancellationToken ct)=>Json(await files.GetSessionAsync(id,ct)));
        group.MapPut("/files/upload-sessions/{id}/content-hash",async(string id,ContentHashRequest request,StudioFileTransfer files,CancellationToken ct)=>Json(await files.SetContentHashAsync(id,request,ct)));
        group.MapPost("/files/upload-sessions/{id}/resume-proof",async(string id,ResumeProofRequest request,StudioFileTransfer files,CancellationToken ct)=>Json(await files.ResumeProofAsync(id,request,ct)));
        group.MapPost("/files/upload-sessions/{id}/resume",async(string id,ResumeProofRequest request,StudioFileTransfer files,CancellationToken ct)=>Json(await files.ResumeAsync(id,request,ct)));
        group.MapPost("/files/upload-sessions/{id}/pause",async(string id,StudioFileTransfer files,CancellationToken ct)=>Json(await files.PauseAsync(id,ct)));
        group.MapPost("/files/upload-sessions/{id}/cancel",async(string id,SetUploadStateRequest request,StudioFileTransfer files,CancellationToken ct)=>Json(await files.CancelAsync(id,request,ct)));
        group.MapPost("/files/upload-sessions/{id}/complete",async(string id,StudioFileTransfer files,CancellationToken ct)=>Json(await files.CompleteAsync(id,ct)));
        group.MapMethods("/files/uploads/{transportId}",["HEAD"],async(string transportId,HttpContext http,StudioFileTransfer files,CancellationToken ct)=>
        {
            var session=await files.GetByTransportAsync(transportId,ct);Headers(http,session);http.Response.Headers["Upload-Length"]=session.Length.ToString(CultureInfo.InvariantCulture);return Results.Ok();
        });
        group.MapMethods("/files/uploads/{transportId}",["PATCH"],async(string transportId,HttpContext http,StudioFileTransfer files,CancellationToken ct)=>
        {
            if(!long.TryParse(http.Request.Headers["Upload-Offset"],NumberStyles.None,CultureInfo.InvariantCulture,out var offset)||!int.TryParse(http.Request.Headers["Upload-Epoch"],NumberStyles.None,CultureInfo.InvariantCulture,out var epoch))throw new StudioException("FILE_UPLOAD_PROTOCOL_INVALID","缺少有效上传偏移或写入轮次。");
            if(http.Request.ContentType!="application/offset+octet-stream")throw new StudioException("FILE_UPLOAD_PROTOCOL_INVALID","上传正文类型无效。");
            var session=await files.AppendAsync(transportId,offset,epoch,http.Request.Body,http.Request.Headers["X-Upload-Resume-Ticket"].FirstOrDefault(),ct);Headers(http,session);return Json(session);
        });
        group.MapGet("/files/{id}",async(string id,StudioFileTransfer files,CancellationToken ct)=>Json(await files.GetFileAsync(id,ct)));
        group.MapGet("/files/{id}/content",async(string id,string? referenceNId,HttpContext http,StudioFileTransfer files,CancellationToken ct)=>
        {
            var metadata=await files.GetFileAsync(id,ct);
            http.Response.Headers.CacheControl="no-store";http.Response.Headers.XContentTypeOptions="nosniff";
            return Results.Stream(await files.OpenContentAsync(id,referenceNId,ct),metadata.ContentType,metadata.FileName,enableRangeProcessing:true);
        });
        group.MapPut("/files/{id}/references/{reference}",async(string id,string reference,FileReferenceRequest request,StudioFileTransfer files,CancellationToken ct)=>Json(await files.AddReferenceAsync(id,reference,request,ct)));
        group.MapDelete("/files/{id}/references/{reference}",async(string id,string reference,StudioFileTransfer files,CancellationToken ct)=>{await files.DeleteReferenceAsync(id,reference,ct);return Json(new {removed=true});});
    }
    private static void Headers(HttpContext http,UploadSessionV1 session)
    {
        http.Response.Headers["Tus-Resumable"]="1.0.0";http.Response.Headers["Upload-Offset"]=session.Offset.ToString(CultureInfo.InvariantCulture);http.Response.Headers["Upload-Epoch"]=session.WriterEpoch.ToString(CultureInfo.InvariantCulture);
        if(session.ResumeTicket is {} ticket)http.Response.Headers["X-Upload-Resume-Ticket"]=ticket;
    }
}
