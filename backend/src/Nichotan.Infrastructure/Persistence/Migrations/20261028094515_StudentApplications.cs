using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProgrammeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntakeTerm = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IntakeYear = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StaffNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HandledByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HandledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolSuggestions_CandidateProfiles_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "CandidateProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolSuggestions_CandidateId",
                table: "SchoolSuggestions",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolSuggestions_Status_CreatedAtUtc",
                table: "SchoolSuggestions",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolSuggestions");
        }
    }
}
