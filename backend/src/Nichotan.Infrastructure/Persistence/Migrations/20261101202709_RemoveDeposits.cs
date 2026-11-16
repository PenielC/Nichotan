using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The deposit step is gone, so the "proof of deposit" document type and anything uploaded as one go with it.
            migrationBuilder.Sql("""
                DELETE FROM "DocumentReviews" WHERE "DocumentId" IN (SELECT d."Id" FROM "CandidateDocuments" d JOIN "DocumentTypes" t ON t."Id" = d."DocumentTypeId" WHERE t."Purpose" = 'DepositProof');
                DELETE FROM "DocumentVersions" WHERE "DocumentId" IN (SELECT d."Id" FROM "CandidateDocuments" d JOIN "DocumentTypes" t ON t."Id" = d."DocumentTypeId" WHERE t."Purpose" = 'DepositProof');
                DELETE FROM "CandidateDocuments" WHERE "DocumentTypeId" IN (SELECT "Id" FROM "DocumentTypes" WHERE "Purpose" = 'DepositProof');
                DELETE FROM "DocumentRequirements" WHERE "DocumentTypeId" IN (SELECT "Id" FROM "DocumentTypes" WHERE "Purpose" = 'DepositProof');
                DELETE FROM "ApplicationChecklistItems" WHERE "DocumentTypeId" IN (SELECT "Id" FROM "DocumentTypes" WHERE "Purpose" = 'DepositProof');
                DELETE FROM "DocumentTypes" WHERE "Purpose" = 'DepositProof';
                """);

            migrationBuilder.DropTable(
                name: "AdmissionDeposits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdmissionDeposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdmissionDeposits_AdmissionApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "AdmissionApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionDeposits_ApplicationId",
                table: "AdmissionDeposits",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionDeposits_Status",
                table: "AdmissionDeposits",
                column: "Status");
        }
    }
}
