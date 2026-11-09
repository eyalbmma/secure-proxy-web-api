/*
  Local development sample data only.
  The sample destination and operation are intentionally inactive. Replace the
  placeholder URL with an approved test upstream before enabling either row.
  This script contains no credentials, keys, or real endpoint address.
*/
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @DestinationName nvarchar(150) = N'local-demo-placeholder';
    DECLARE @OperationKey nvarchar(150) = N'customer.get';
    DECLARE @DestinationId int;
    DECLARE @OperationId int;

    SELECT @DestinationId = Id
    FROM dbo.Destinations
    WHERE Name = @DestinationName;

    IF @DestinationId IS NULL
    BEGIN
        INSERT INTO dbo.Destinations
            (Name, BaseUrl, IsActive, RequestTimeoutMilliseconds, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@DestinationName,
             N'https://replace-with-approved-upstream.example.test',
             0,
             5000,
             SYSUTCDATETIME(),
             SYSUTCDATETIME());

        SET @DestinationId = CONVERT(int, SCOPE_IDENTITY());
    END;

    SELECT @OperationId = Id
    FROM dbo.Operations
    WHERE OperationKey = @OperationKey;

    IF @OperationId IS NULL
    BEGIN
        INSERT INTO dbo.Operations
            (OperationKey, DestinationId, HttpMethod, RelativePath, IsActive,
             MaxRequestSizeBytes, AllowUnknownParameters, RequiresEncryption, EncryptionProfileId)
        VALUES
            (@OperationKey, @DestinationId, N'POST', N'v1/customers/{urlId}', 0,
             200000, 0, 0, NULL);

        SET @OperationId = CONVERT(int, SCOPE_IDENTITY());
    END;

    -- Bring forward only this named, inactive placeholder sample created by an earlier script version.
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Operations AS op
        INNER JOIN dbo.Destinations AS destination ON destination.Id = op.DestinationId
        WHERE op.Id = @OperationId
          AND op.IsActive = 0
          AND destination.Name = @DestinationName
          AND destination.IsActive = 0
    )
    BEGIN
        UPDATE dbo.Operations
        SET RelativePath = N'v1/customers/{urlId}'
        WHERE Id = @OperationId;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ParameterRules
        WHERE OperationId = @OperationId
          AND ParameterName = N'customerId'
    )
    BEGIN
        INSERT INTO dbo.ParameterRules
            (OperationId, ParameterName, IsRequired, DataType, MaximumLength,
             MinimumValue, MaximumValue, Location)
        VALUES
            (@OperationId, N'customerId', 1, N'integer', NULL, NULL, NULL, N'Body');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ParameterRules
        WHERE OperationId = @OperationId
          AND ParameterName = N'urlId'
    )
    AND EXISTS
    (
        SELECT 1
        FROM dbo.Operations AS op
        INNER JOIN dbo.Destinations AS destination ON destination.Id = op.DestinationId
        WHERE op.Id = @OperationId
          AND op.IsActive = 0
          AND destination.Name = @DestinationName
          AND destination.IsActive = 0
    )
    BEGIN
        INSERT INTO dbo.ParameterRules
            (OperationId, ParameterName, IsRequired, DataType, MaximumLength,
             MinimumValue, MaximumValue, Location)
        VALUES
            (@OperationId, N'urlId', 1, N'string', 150, NULL, NULL, N'Path');
    END;

    COMMIT TRANSACTION;

    SELECT
        destination.Name AS DestinationName,
        destination.BaseUrl,
        destination.IsActive AS DestinationIsActive,
        op.OperationKey,
        op.HttpMethod,
        op.RelativePath,
        op.IsActive AS OperationIsActive
    FROM dbo.Operations AS op
    INNER JOIN dbo.Destinations AS destination ON destination.Id = op.DestinationId
    WHERE op.Id = @OperationId;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
