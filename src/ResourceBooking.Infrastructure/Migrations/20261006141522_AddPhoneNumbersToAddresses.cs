using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneNumbersToAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "UserAddresses",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress_PhoneNumber",
                table: "Orders",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress_PhoneNumber",
                table: "Orders");
        }
    }
}
