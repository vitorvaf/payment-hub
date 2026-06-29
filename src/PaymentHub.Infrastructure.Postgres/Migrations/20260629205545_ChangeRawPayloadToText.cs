using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentHub.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRawPayloadToText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "raw_payload",
                table: "webhook_events",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "raw_payload",
                table: "webhook_events",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
