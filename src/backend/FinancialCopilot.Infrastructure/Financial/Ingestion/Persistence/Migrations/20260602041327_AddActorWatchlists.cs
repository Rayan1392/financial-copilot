using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActorWatchlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchlistSymbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistSymbols", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistSymbols_TenantId_ActorId_ActorType_Position",
                table: "WatchlistSymbols",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistSymbols_TenantId_ActorId_ActorType_Symbol",
                table: "WatchlistSymbols",
                columns: new[] { "TenantId", "ActorId", "ActorType", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchlistSymbols");
        }
    }
}
