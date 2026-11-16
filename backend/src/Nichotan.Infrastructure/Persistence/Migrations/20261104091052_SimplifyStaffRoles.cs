using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nichotan.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyStaffRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // There are now only administrators, counsellors and students. Anyone who held one of the retired staff roles becomes a
            // counsellor (unless they already are one, or an administrator), and the retired roles are removed.
            migrationBuilder.Sql("""
                INSERT INTO "UserRoles" ("UserId", "RoleId")
                SELECT DISTINCT ur."UserId", c."Id"
                FROM "UserRoles" ur
                JOIN "Roles" old ON old."Id" = ur."RoleId" AND old."Name" IN ('VisaOfficer', 'DocumentReviewer', 'ReportViewer')
                JOIN "Roles" c ON c."Name" = 'AdmissionsCounsellor'
                WHERE NOT EXISTS (
                    SELECT 1 FROM "UserRoles" x JOIN "Roles" xr ON xr."Id" = x."RoleId"
                    WHERE x."UserId" = ur."UserId" AND xr."Name" IN ('AdmissionsCounsellor', 'Administrator'));
                DELETE FROM "UserRoles" WHERE "RoleId" IN (SELECT "Id" FROM "Roles" WHERE "Name" IN ('VisaOfficer', 'DocumentReviewer', 'ReportViewer'));
                DELETE FROM "RoleClaims" WHERE "RoleId" IN (SELECT "Id" FROM "Roles" WHERE "Name" IN ('VisaOfficer', 'DocumentReviewer', 'ReportViewer'));
                DELETE FROM "Roles" WHERE "Name" IN ('VisaOfficer', 'DocumentReviewer', 'ReportViewer');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
