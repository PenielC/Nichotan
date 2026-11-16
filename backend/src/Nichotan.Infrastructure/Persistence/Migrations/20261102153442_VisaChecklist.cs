using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisaChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationChecklistItems_ApplicationId_DocumentTypeId",
                table: "ApplicationChecklistItems");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "DocumentRequirements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Admission");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "ApplicationChecklistItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Admission");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationChecklistItems_ApplicationId_Stage_DocumentTypeId",
                table: "ApplicationChecklistItems",
                columns: new[] { "ApplicationId", "Stage", "DocumentTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationChecklistItems_ApplicationId_Stage_DocumentTypeId",
                table: "ApplicationChecklistItems");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "DocumentRequirements");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "ApplicationChecklistItems");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationChecklistItems_ApplicationId_DocumentTypeId",
                table: "ApplicationChecklistItems",
                columns: new[] { "ApplicationId", "DocumentTypeId" },
                unique: true);
        }
    }
}
