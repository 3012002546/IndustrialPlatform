-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-009
IF OBJECT_ID(N'dbo.reference_data_outbox_message',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_outbox_message (
    event_id uniqueidentifier PRIMARY KEY,
    module_key nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(module_key IN
        (N'dictionary',N'parameter',N'dynamic-property',N'metadata',N'coding-rule',N'state-machine',N'unit-of-measure')),
    event_name nvarchar(96) COLLATE Latin1_General_100_BIN2 NOT NULL,
    aggregate_id uniqueidentifier NOT NULL,
    revision bigint NOT NULL CHECK(revision>0),
    payload nvarchar(max) NOT NULL,
    CHECK(payload IS NULL OR ISJSON(payload)=1),
    status nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Pending',N'Published',N'Failed')),
    attempt_count int NOT NULL CHECK(attempt_count BETWEEN 0 AND 10),
    next_attempt_on datetimeoffset(7) NULL,
    last_error nvarchar(500) COLLATE Latin1_General_100_BIN2 NULL,
    created_time datetimeoffset(7) NOT NULL,
    published_on datetimeoffset(7) NULL,
    CHECK((status=N'Published' AND published_on IS NOT NULL)
        OR (status IN (N'Pending',N'Failed') AND published_on IS NULL)),
    CHECK(status<>N'Failed' OR (attempt_count=10 AND next_attempt_on IS NULL))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_outbox_message') AND name=N'reference_data_outbox_pending_ix')
CREATE INDEX reference_data_outbox_pending_ix ON dbo.reference_data_outbox_message (status,next_attempt_on,created_time);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_outbox_message') AND name=N'reference_data_outbox_module_ix')
CREATE INDEX reference_data_outbox_module_ix ON dbo.reference_data_outbox_message (module_key,status);
