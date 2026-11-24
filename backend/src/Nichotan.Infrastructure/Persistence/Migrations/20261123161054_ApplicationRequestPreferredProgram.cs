using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationRequestPreferredProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredProgram",
                table: "ApplicationRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredProgram",
                table: "ApplicationRequests");
        }
    }
}
