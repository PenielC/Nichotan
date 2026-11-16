using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReopenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationForms_CandidateProfiles_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationFormVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Signature_Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Signature_SignerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Signature_SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Signature_IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Signature_Image = table.Column<byte[]>(type: "bytea", nullable: true),
                    Signature_ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Signature_DeclarationVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Signature_DeclarationText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationFormVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationFormVersions_ApplicationForms_FormId",
                        column: x => x.FormId,
                        principalTable: "ApplicationForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationForms_CandidateId",
                table: "ApplicationForms",
                column: "CandidateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationFormVersions_FormId_VersionNumber",
                table: "ApplicationFormVersions",
                columns: new[] { "FormId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationFormVersions");

            migrationBuilder.DropTable(
                name: "ApplicationForms");
        }
    }
}
