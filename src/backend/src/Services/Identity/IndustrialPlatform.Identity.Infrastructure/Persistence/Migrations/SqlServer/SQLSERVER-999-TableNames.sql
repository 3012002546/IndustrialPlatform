-- 在历史迁移与说明脚本之后改名，保留数据、对象 ID 和扩展属性。
IF OBJECT_ID(N'dbo.pf05_service_call_nonce',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.identity_service_call_nonce',N'U') IS NOT NULL
    THROW 51000, N'安全表改名冲突：service_call_nonce 同时存在旧名和新名。', 1;
IF OBJECT_ID(N'dbo.pf05_step_up_grant',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.identity_step_up_grant',N'U') IS NOT NULL
    THROW 51000, N'安全表改名冲突：step_up_grant 同时存在旧名和新名。', 1;
IF OBJECT_ID(N'dbo.pf05_service_call_nonce',N'U') IS NOT NULL
    EXEC sys.sp_rename N'dbo.pf05_service_call_nonce', N'identity_service_call_nonce', N'OBJECT';
IF OBJECT_ID(N'dbo.pf05_step_up_grant',N'U') IS NOT NULL
    EXEC sys.sp_rename N'dbo.pf05_step_up_grant', N'identity_step_up_grant', N'OBJECT';
IF OBJECT_ID(N'dbo.identity_service_call_nonce',N'U') IS NULL OR OBJECT_ID(N'dbo.identity_step_up_grant',N'U') IS NULL
    THROW 51000, N'安全表改名后缺少目标表。', 1;

DECLARE @pk sysname, @qualified nvarchar(512);
SELECT @pk=name FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.identity_service_call_nonce') AND type='PK';
IF @pk <> N'pk_identity_service_call_nonce'
BEGIN
    SET @qualified=N'dbo.'+QUOTENAME(@pk);
    EXEC sys.sp_rename @qualified, N'pk_identity_service_call_nonce', N'OBJECT';
END;
SELECT @pk=name FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.identity_step_up_grant') AND type='PK';
IF @pk <> N'pk_identity_step_up_grant'
BEGIN
    SET @qualified=N'dbo.'+QUOTENAME(@pk);
    EXEC sys.sp_rename @qualified, N'pk_identity_step_up_grant', N'OBJECT';
END;
IF OBJECT_ID(N'dbo.uq_pf05_step_up_grant_proof',N'UQ') IS NOT NULL
    EXEC sys.sp_rename N'dbo.uq_pf05_step_up_grant_proof', N'uq_identity_step_up_grant_proof', N'OBJECT';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_service_call_nonce') AND name=N'ix_pf05_service_call_nonce_expires')
    EXEC sys.sp_rename N'dbo.identity_service_call_nonce.ix_pf05_service_call_nonce_expires', N'ix_identity_service_call_nonce_expires', N'INDEX';
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_step_up_grant') AND name=N'ix_pf05_step_up_grant_expiry')
    EXEC sys.sp_rename N'dbo.identity_step_up_grant.ix_pf05_step_up_grant_expiry', N'ix_identity_step_up_grant_expiry', N'INDEX';

EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Identity 受信服务调用防重放记录；按调用方和一次性随机值去重，保留至过期清理。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'identity_service_call_nonce';
EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'受信内部请求的签发服务标识；与 nonce 共同构成防重放唯一键。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'identity_service_call_nonce', @level2type=N'COLUMN', @level2name=N'issuer';
EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Identity 敏感操作再次认证授权；绑定用户、会话、操作和请求摘要，记录过期及消费结果。', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'identity_step_up_grant';
