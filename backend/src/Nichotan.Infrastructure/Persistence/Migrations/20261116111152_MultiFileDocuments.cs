using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiFileDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "DocumentVersions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAtUtc",
                table: "DocumentVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultipleFiles",
                table: "DocumentTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Academic records are the first document that is a set of files.
            migrationBuilder.Sql("UPDATE \"DocumentTypes\" SET \"AllowsMultipleFiles\" = TRUE WHERE \"Name\" = 'Academic records';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "RemovedAtUtc",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "AllowsMultipleFiles",
                table: "DocumentTypes");
        }
    }
}
