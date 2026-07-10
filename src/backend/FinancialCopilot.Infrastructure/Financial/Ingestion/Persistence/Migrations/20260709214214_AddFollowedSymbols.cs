using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowedSymbols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedSymbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CompanyNameEnglish = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FollowedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedSymbols", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedSymbols_ExternalCompanyId",
                table: "FollowedSymbols",
                column: "ExternalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowedSymbols_TenantId_ActorId_ActorType",
                table: "FollowedSymbols",
                columns: new[] { "TenantId", "ActorId", "ActorType" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedSymbols_TenantId_ActorId_ActorType_ExternalCompanyId",
                table: "FollowedSymbols",
                columns: new[] { "TenantId", "ActorId", "ActorType", "ExternalCompanyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowedSymbols");
        }
    }
}
