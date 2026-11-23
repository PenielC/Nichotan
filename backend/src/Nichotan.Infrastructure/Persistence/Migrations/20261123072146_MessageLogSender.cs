using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MessageLogSender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sender",
                table: "MessageLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Automated");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sender",
                table: "MessageLogs");
        }
    }
}
