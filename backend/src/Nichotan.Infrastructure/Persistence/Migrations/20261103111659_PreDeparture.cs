using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreDeparture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreDepartureCaveats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreDepartureCaveats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PreDepartureLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisaCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ActivatedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PlannedDepartureOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreDepartureLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreDepartureLists_VisaCases_VisaCaseId",
                        column: x => x.VisaCaseId,
                        principalTable: "VisaCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PreDepartureTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Importance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreDepartureTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PreDepartureItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Importance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreDepartureItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreDepartureItems_PreDepartureLists_ListId",
                        column: x => x.ListId,
                        principalTable: "PreDepartureLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreDepartureCaveats_Country",
                table: "PreDepartureCaveats",
                column: "Country",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreDepartureItems_ListId_SortOrder",
                table: "PreDepartureItems",
                columns: new[] { "ListId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PreDepartureLists_VisaCaseId",
                table: "PreDepartureLists",
                column: "VisaCaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreDepartureTemplate_Country_SortOrder",
                table: "PreDepartureTemplate",
                columns: new[] { "Country", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreDepartureCaveats");

            migrationBuilder.DropTable(
                name: "PreDepartureItems");

            migrationBuilder.DropTable(
                name: "PreDepartureTemplate");

            migrationBuilder.DropTable(
                name: "PreDepartureLists");
        }
    }
}
