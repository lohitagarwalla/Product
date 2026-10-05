using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductPriceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceHistory", x => x.Id);
                    table.CheckConstraint("CK_ProductPriceHistory_Entry", "([EntryType] = 'Baseline' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NULL) OR ([EntryType] = 'Created' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NOT NULL) OR ([EntryType] = 'PriceChanged' AND [PreviousPrice] IS NOT NULL AND [PreviousPrice] <> [NewPrice] AND [ChangedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ProductPriceHistory_Price", "[NewPrice] >= 0 AND ([PreviousPrice] IS NULL OR [PreviousPrice] >= 0)");
                    table.ForeignKey(
                        name: "FK_ProductPriceHistory_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPriceHistory_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistory_ChangedByUserId",
                table: "ProductPriceHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistory_ProductId_ChangedAtUtc_Id",
                table: "ProductPriceHistory",
                columns: new[] { "ProductId", "ChangedAtUtc", "Id" });

            // Older prices and administrators are unknown. Capture only the current price,
            // including soft-deleted products, using the time this migration is applied.
            migrationBuilder.Sql("""
                DECLARE @baselineTime datetime2 = SYSUTCDATETIME();
                INSERT INTO [ProductPriceHistory]
                    ([ProductId], [PreviousPrice], [NewPrice], [ChangedByUserId], [ChangedAtUtc], [EntryType])
                SELECT [Id], NULL, [Price], NULL, @baselineTime, 'Baseline'
                FROM [Products];
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPriceHistory");
        }
    }
}
