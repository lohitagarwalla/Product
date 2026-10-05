BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153219_AddProductPriceHistory'
)
BEGIN
    CREATE TABLE [ProductPriceHistory] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [PreviousPrice] decimal(18,2) NULL,
        [NewPrice] decimal(18,2) NOT NULL,
        [ChangedByUserId] nvarchar(450) NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [EntryType] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_ProductPriceHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductPriceHistory_Entry] CHECK (([EntryType] = 'Baseline' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NULL) OR ([EntryType] = 'Created' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NOT NULL) OR ([EntryType] = 'PriceChanged' AND [PreviousPrice] IS NOT NULL AND [PreviousPrice] <> [NewPrice] AND [ChangedByUserId] IS NOT NULL)),
        CONSTRAINT [CK_ProductPriceHistory_Price] CHECK ([NewPrice] >= 0 AND ([PreviousPrice] IS NULL OR [PreviousPrice] >= 0)),
        CONSTRAINT [FK_ProductPriceHistory_AspNetUsers_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductPriceHistory_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153219_AddProductPriceHistory'
)
BEGIN
    CREATE INDEX [IX_ProductPriceHistory_ChangedByUserId] ON [ProductPriceHistory] ([ChangedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153219_AddProductPriceHistory'
)
BEGIN
    CREATE INDEX [IX_ProductPriceHistory_ProductId_ChangedAtUtc_Id] ON [ProductPriceHistory] ([ProductId], [ChangedAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153219_AddProductPriceHistory'
)
BEGIN
    DECLARE @baselineTime datetime2 = SYSUTCDATETIME();
    INSERT INTO [ProductPriceHistory]
        ([ProductId], [PreviousPrice], [NewPrice], [ChangedByUserId], [ChangedAtUtc], [EntryType])
    SELECT [Id], NULL, [Price], NULL, @baselineTime, 'Baseline'
    FROM [Products];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153219_AddProductPriceHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004153219_AddProductPriceHistory', N'10.0.12');
END;

COMMIT;
GO

