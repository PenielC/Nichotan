using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisaCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisaCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubmittedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    BiometricsAppointmentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    BiometricsLocation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DecisionOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisaCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisaCases_AdmissionApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "AdmissionApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisaCases_CandidateProfiles_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisaStatusEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisaCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    To = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsCorrection = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisaStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisaStatusEvents_VisaCases_VisaCaseId",
                        column: x => x.VisaCaseId,
                        principalTable: "VisaCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisaCases_ApplicationId",
                table: "VisaCases",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisaCases_CandidateId",
                table: "VisaCases",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_VisaCases_Status",
                table: "VisaCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VisaStatusEvents_To_OccurredOn",
                table: "VisaStatusEvents",
                columns: new[] { "To", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_VisaStatusEvents_VisaCaseId_RecordedAtUtc",
                table: "VisaStatusEvents",
                columns: new[] { "VisaCaseId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisaStatusEvents");

            migrationBuilder.DropTable(
                name: "VisaCases");
        }
    }
}
