using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserInsightStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserInsightStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InsightEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInsightStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserInsightStates_Actor_DismissedAtUtc",
                table: "UserInsightStates",
                columns: new[] { "TenantId", "ActorId", "ActorType", "DismissedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInsightStates_InsightEventId",
                table: "UserInsightStates",
                column: "InsightEventId");

            migrationBuilder.CreateIndex(
                name: "UIX_UserInsightStates_Actor_InsightEventId",
                table: "UserInsightStates",
                columns: new[] { "TenantId", "ActorId", "ActorType", "InsightEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserInsightStates");
        }
    }
}
