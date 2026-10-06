-- SQL Server：校准长度约束，保留既有迁移 checksum，通过新 ID 升级。
DECLARE @drop nvarchar(max);

SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_definition DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_definition'),N'template','ColumnId')
         OR definition LIKE N'%[[]template[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'ck_sql_length_coding_rule_definition_template')
    ALTER TABLE dbo.reference_data_coding_rule_definition WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_definition_template CHECK((LEN(template+N'#')-1) BETWEEN 1 AND 1024);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_sequence DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_sequence') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_sequence'),N'period_key','ColumnId')
         OR definition LIKE N'%[[]period_key[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_sequence') AND name=N'ck_sql_length_coding_rule_sequence_period_key')
    ALTER TABLE dbo.reference_data_coding_rule_sequence WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_sequence_period_key CHECK((LEN(period_key+N'#')-1) BETWEEN 1 AND 8);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_idempotency_record DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record'),N'idempotency_key_hash','ColumnId')
         OR definition LIKE N'%[[]idempotency_key_hash[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND name=N'ck_sql_length_coding_rule_idempotency_record_idempotency_key_hash')
    ALTER TABLE dbo.reference_data_coding_rule_idempotency_record WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_idempotency_record_idempotency_key_hash CHECK((LEN(idempotency_key_hash+N'#')-1)=64);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_idempotency_record DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record'),N'request_hash','ColumnId')
         OR definition LIKE N'%[[]request_hash[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND name=N'ck_sql_length_coding_rule_idempotency_record_request_hash')
    ALTER TABLE dbo.reference_data_coding_rule_idempotency_record WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_idempotency_record_request_hash CHECK((LEN(request_hash+N'#')-1)=64);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_idempotency_record DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record'),N'code','ColumnId')
         OR definition LIKE N'%[[]code[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND name=N'ck_sql_length_coding_rule_idempotency_record_code')
    ALTER TABLE dbo.reference_data_coding_rule_idempotency_record WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_idempotency_record_code CHECK((LEN(code+N'#')-1) BETWEEN 1 AND 128);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_coding_rule_idempotency_record DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record'),N'period_key','ColumnId')
         OR definition LIKE N'%[[]period_key[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND name=N'ck_sql_length_coding_rule_idempotency_record_period_key')
    ALTER TABLE dbo.reference_data_coding_rule_idempotency_record WITH CHECK ADD CONSTRAINT ck_sql_length_coding_rule_idempotency_record_period_key CHECK((LEN(period_key+N'#')-1) BETWEEN 1 AND 8);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_metadata_attribute_definition DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_metadata_attribute_definition') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_metadata_attribute_definition'),N'pattern','ColumnId')
         OR definition LIKE N'%[[]pattern[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_metadata_attribute_definition') AND name=N'ck_sql_length_metadata_attribute_definition_pattern')
    ALTER TABLE dbo.reference_data_metadata_attribute_definition WITH CHECK ADD CONSTRAINT ck_sql_length_metadata_attribute_definition_pattern CHECK(pattern IS NULL OR (LEN(pattern+N'#')-1)<=256);


SET @drop=N'';
SELECT @drop=@drop+N'ALTER TABLE dbo.reference_data_cache_generation DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_cache_generation') AND definition LIKE N'%#N%'
    AND (parent_column_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.reference_data_cache_generation'),N'generation_key','ColumnId')
         OR definition LIKE N'%[[]generation_key[]]%');
IF @drop<>N'' EXEC sys.sp_executesql @drop;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.reference_data_cache_generation') AND name=N'ck_sql_length_cache_generation_generation_key')
    ALTER TABLE dbo.reference_data_cache_generation WITH CHECK ADD CONSTRAINT ck_sql_length_cache_generation_generation_key CHECK((LEN(generation_key+N'#')-1)=64);
