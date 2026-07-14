using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodalAlertSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodalAlertSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnnouncementTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                    MinimumImportance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RawAlertEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AiSummaryEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodalAlertSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodalAlertSubscriptions_Actor",
                table: "CodalAlertSubscriptions",
                columns: new[] { "TenantId", "ActorId", "ActorType" });

            migrationBuilder.CreateIndex(
                name: "IX_CodalAlertSubscriptions_Company_State",
                table: "CodalAlertSubscriptions",
                columns: new[] { "ExternalCompanyId", "State" });

            migrationBuilder.CreateIndex(
                name: "UIX_CodalAlertSubscriptions_Actor_Company",
                table: "CodalAlertSubscriptions",
                columns: new[] { "TenantId", "ActorId", "ActorType", "ExternalCompanyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodalAlertSubscriptions");
        }
    }
}
