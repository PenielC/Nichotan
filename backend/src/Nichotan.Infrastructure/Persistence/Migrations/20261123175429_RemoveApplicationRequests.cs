using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveApplicationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HandledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandledByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IntakeTerm = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IntakeYear = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreferredProgram = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationRequests_AdmissionApplications_AdmissionApplicat~",
                        column: x => x.AdmissionApplicationId,
                        principalTable: "AdmissionApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApplicationRequests_CandidateProfiles_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationRequests_AdmissionApplicationId",
                table: "ApplicationRequests",
                column: "AdmissionApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationRequests_CandidateId",
                table: "ApplicationRequests",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationRequests_Status_CreatedAtUtc",
                table: "ApplicationRequests",
                columns: new[] { "Status", "CreatedAtUtc" });
        }
    }
}
