using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAdminAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_security_admin_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ActionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Before = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    After = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_security_admin_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_security_admin_audits_TenantId_OccurredAt",
                table: "auth_security_admin_audits",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_security_admin_audits");
        }
    }
}
