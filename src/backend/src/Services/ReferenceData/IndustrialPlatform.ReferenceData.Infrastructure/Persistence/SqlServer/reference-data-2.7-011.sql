-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-011
IF OBJECT_ID(N'dbo.reference_data_cache_generation',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_cache_generation (
    generation_key nvarchar(64) COLLATE Latin1_General_100_BIN2 PRIMARY KEY NOT NULL CHECK((LEN(generation_key+N'#N')-1)=64),
    generation_token nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(generation_token+N'#')-1)=64),
    last_updated_on datetimeoffset(7) NOT NULL
);
END;
