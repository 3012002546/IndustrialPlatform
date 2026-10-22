using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioLedgerTests
{
    private sealed class Files : IStudioFiles
    {
        public bool Enabled=>false;
        public Task ValidateReferenceAsync(StudioPrincipal principal,string fileNId,string referenceNId,CancellationToken cancellationToken)=>throw new InvalidOperationException();
    }
    private static JsonObject Model()=>JsonNode.Parse("""{"schemaVersion":1,"numbering":{"mode":"Manual"},"displayFieldId":"code","fields":[{"id":"code","name":"code","type":"String","required":true,"nullable":false,"unique":true,"queryable":true,"sortable":true,"maxLength":128},{"id":"quantity","name":"quantity","type":"Decimal","scale":6,"nullable":true,"queryable":true,"sortable":true},{"id":"enabled","name":"enabled","type":"Boolean","nullable":true,"queryable":true}]}""")!.AsObject();
    private static async Task<(StudioLedger Ledger,string Revision)> Setup(StudioTestContext fixture)
    {
        var resource=await fixture.Publishing.CreateAsync(fixture.Principal,new("ledger-a","LedgerModel","台账",null,null,Model()),"create",CancellationToken.None);
        var plan=await fixture.Publishing.ValidateAsync(fixture.Principal,resource.NId,Model(),CancellationToken.None);
        var revision=await fixture.Publishing.PublishAsync(fixture.Principal,resource.NId,new(resource.OptimisticVersion,resource.ConcurrencyVersion,"首版",true,plan.DependencyPlanHash),"publish",CancellationToken.None);
        return (new(fixture.Store,fixture.Publishing,fixture.Authorization,fixture.Commands,new Files(),TimeProvider.System),revision.NId);
    }
    [Fact]
    public async Task Records_preserve_exact_decimal_and_query_real_type_indexes()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        for(var i=0;i<3;i++)await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new() {["code"]="C"+i,["quantity"]=i==2?"999999999999.999999":"1.000001",["enabled"]=true},revision,NId:"R"+i),"write"+i,CancellationToken.None);
        var page=await ledger.QueryAsync(f.Principal,"ledger-a",revision,new(1,1,"quantity","desc",Filters:[new("quantity","gt",JsonValue.Create("1.000000"))]),CancellationToken.None);
        Assert.Equal("3",page.TotalCount);Assert.Single(page.Items);Assert.Equal("R2",page.Items[0].NId);Assert.Equal("999999999999.999999",page.Items[0].Values["quantity"]!.GetValue<string>());
        Assert.Equal(TimeSpan.Zero,page.Items[0].CreatedOn.Offset);
        Assert.InRange(DateTimeOffset.UtcNow-page.Items[0].CreatedOn,TimeSpan.Zero,TimeSpan.FromMinutes(1));
    }
    [Fact]
    public async Task Stale_write_rolls_back_values_indexes_and_audit()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        var record=await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new() {["code"]="C1",["quantity"]="1.2",["enabled"]=true},revision,NId:"R1"),"one",CancellationToken.None);
        var first=await ledger.WriteAsync(f.Principal,"ledger-a","R1",new(new() {["code"]="C2",["quantity"]="2.2",["enabled"]=false},revision,record.OptimisticVersion,record.ConcurrencyVersion),"two",CancellationToken.None);
        var error=await Assert.ThrowsAsync<StudioException>(()=>ledger.WriteAsync(f.Principal,"ledger-a","R1",new(new() {["code"]="overwritten",["quantity"]="3",["enabled"]=true},revision,record.OptimisticVersion,record.ConcurrencyVersion),"stale",CancellationToken.None));
        Assert.Equal("STUDIO_VERSION_CONFLICT",error.Code);
        Assert.Equal("C2",(await ledger.GetAsync(f.Principal,"ledger-a","R1",revision,CancellationToken.None)).Values["code"]!.GetValue<string>());
        Assert.Equal("1",first.OptimisticVersion);
    }
    [Fact]
    public async Task Soft_delete_releases_unique_index_restore_conflict_rolls_back()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        var one=await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new() {["code"]="C1"},revision,NId:"R1"),"one",CancellationToken.None);
        var deleted=await ledger.SetStateAsync(f.Principal,"ledger-a","R1","delete",new(one.OptimisticVersion,one.ConcurrencyVersion),"delete",CancellationToken.None);
        await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new() {["code"]="C1"},revision,NId:"R2"),"two",CancellationToken.None);
        await Assert.ThrowsAsync<StudioException>(()=>ledger.SetStateAsync(f.Principal,"ledger-a","R1","restore",new(deleted.OptimisticVersion,deleted.ConcurrencyVersion),"restore",CancellationToken.None));
        var tombstone=await f.Store.FindLedgerRecordAsync(f.Principal.TenantNId,(await f.Store.FindResourceAsync(f.Principal.TenantNId,"ledger-a",CancellationToken.None))!.Id,"R1",CancellationToken.None);
        Assert.True(tombstone!.IsDeleted);
        Assert.Equal("1",(await ledger.QueryAsync(f.Principal,"ledger-a",revision,new(),CancellationToken.None)).TotalCount);
    }
    [Fact]
    public async Task Repeated_state_commands_do_not_change_audit_actor_without_advancing_version()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        var row=await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new() {["code"]="C1"},revision,NId:"R1"),"create",CancellationToken.None);
        var model=(await f.Store.FindResourceAsync(f.Principal.TenantNId,"ledger-a",CancellationToken.None))!;
        var grants=await f.Store.GetGrantsAsync(f.Principal.TenantNId,model.Id,CancellationToken.None);
        await f.Store.ReplaceGrantsAsync(f.Principal.TenantNId,model.Id,[..grants,new StudioGrant {NId="second-writer",TenantNId=f.Principal.TenantNId,ResourceId=model.Id,SubjectKind="User",SubjectNId="user-b",Capability="Run",GrantedBy=f.Principal.SubjectNId,CreatedOn=DateTimeOffset.UtcNow}],CancellationToken.None);
        var other=f.Principal with {SubjectNId="user-b"};
        var deleted=await ledger.SetStateAsync(f.Principal,"ledger-a","R1","delete",new(row.OptimisticVersion,row.ConcurrencyVersion),"delete",CancellationToken.None);
        var repeated=await ledger.SetStateAsync(other,"ledger-a","R1","delete",new(deleted.OptimisticVersion,deleted.ConcurrencyVersion),"delete-again",CancellationToken.None);
        Assert.Equal(deleted.OptimisticVersion,repeated.OptimisticVersion);
        Assert.Equal(deleted.ConcurrencyVersion,repeated.ConcurrencyVersion);
        Assert.Equal(deleted.LastUpdatedOn,repeated.LastUpdatedOn);
        Assert.Equal(deleted.LastUpdatedBy,repeated.LastUpdatedBy);
        var restored=await ledger.SetStateAsync(other,"ledger-a","R1","restore",new(repeated.OptimisticVersion,repeated.ConcurrencyVersion),"restore",CancellationToken.None);
        repeated=await ledger.SetStateAsync(f.Principal,"ledger-a","R1","restore",new(restored.OptimisticVersion,restored.ConcurrencyVersion),"restore-again",CancellationToken.None);
        Assert.Equal("user-b",restored.LastUpdatedBy);
        Assert.Equal(restored.OptimisticVersion,repeated.OptimisticVersion);
        Assert.Equal(restored.ConcurrencyVersion,repeated.ConcurrencyVersion);
        Assert.Equal(restored.LastUpdatedOn,repeated.LastUpdatedOn);
        Assert.Equal(restored.LastUpdatedBy,repeated.LastUpdatedBy);
        var persisted=(await f.Store.FindLedgerRecordAsync(f.Principal.TenantNId,model.Id,"R1",CancellationToken.None))!;
        Assert.Equal(restored.LastUpdatedBy,persisted.LastUpdatedBy);
        Assert.Equal("1",(await ledger.QueryAsync(f.Principal,"ledger-a",revision,new(),CancellationToken.None)).TotalCount);
    }
    [Fact]
    public async Task Ledger_function_and_resource_permissions_remain_independent()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        var error=await Assert.ThrowsAsync<StudioException>(()=>ledger.QueryAsync(f.Principal with {SubjectNId="other"},"ledger-a",revision,new(),CancellationToken.None));
        Assert.Equal(404,error.StatusCode);
        error=await Assert.ThrowsAsync<StudioException>(()=>ledger.WriteAsync(f.Principal with {Permissions=f.Principal.Permissions.Where(p=>p!="studio.ledger.write").ToArray()},"ledger-a",null,new(new(){["code"]="A"},revision,NId:"R1"),"one",CancellationToken.None));
        Assert.Equal(403,error.StatusCode);
        Assert.Equal("0",(await ledger.QueryAsync(f.Principal,"ledger-a",revision,new(),CancellationToken.None)).TotalCount);
    }
    [Fact]
    public async Task Projection_under_new_model_preserves_saved_schema_revision_until_a_real_write()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,original)=await Setup(f);
        var row=await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new(){["code"]="C1"},original,NId:"R1"),"record",CancellationToken.None);
        var draft=await f.Publishing.GetDraftAsync(f.Principal,"ledger-a",CancellationToken.None);
        var model=Model();model["fields"]!.AsArray().Add(new JsonObject{["id"]="note",["name"]="note",["type"]="String",["nullable"]=true});
        var saved=await f.Publishing.SaveDraftAsync(f.Principal,"ledger-a",new(draft["optimisticVersion"]!.GetValue<string>(),draft["concurrencyVersion"]!.GetValue<Guid>(),"升级",null,null,model),CancellationToken.None);
        var plan=await f.Publishing.ValidateAsync(f.Principal,"ledger-a",model,CancellationToken.None);
        var next=await f.Publishing.PublishAsync(f.Principal,"ledger-a",new(saved["optimisticVersion"]!.GetValue<string>(),saved["concurrencyVersion"]!.GetValue<Guid>(),"兼容升级",true,plan.DependencyPlanHash),"upgrade",CancellationToken.None);
        var projected=await ledger.GetAsync(f.Principal,"ledger-a","R1",next.NId,CancellationToken.None);
        Assert.Equal(original,projected.SchemaRevisionNId);Assert.Null(projected.Values["note"]);
        Assert.Equal(original,(await ledger.QueryAsync(f.Principal,"ledger-a",next.NId,new(),CancellationToken.None)).Items.Single().SchemaRevisionNId);
        var written=await ledger.WriteAsync(f.Principal,"ledger-a","R1",new(new(){["code"]="C1",["note"]="new"},next.NId,row.OptimisticVersion,row.ConcurrencyVersion),"edit",CancellationToken.None);
        Assert.Equal(next.NId,written.SchemaRevisionNId);
    }
    [Fact]
    public async Task Unique_text_ignores_outer_whitespace_without_rewriting_business_truth()
    {
        await using var f=await StudioTestContext.CreateAsync();var (ledger,revision)=await Setup(f);
        var row=await ledger.WriteAsync(f.Principal,"ledger-a",null,new(new(){["code"]="  C1  "},revision,NId:"R1"),"first",CancellationToken.None);
        Assert.Equal("  C1  ",row.Values["code"]!.GetValue<string>());
        var error=await Assert.ThrowsAsync<StudioException>(()=>ledger.WriteAsync(f.Principal,"ledger-a",null,new(new(){["code"]="C1"},revision,NId:"R2"),"second",CancellationToken.None));
        Assert.Equal(409,error.StatusCode);
        Assert.Equal("1",(await ledger.QueryAsync(f.Principal,"ledger-a",revision,new(),CancellationToken.None)).TotalCount);
    }
}
