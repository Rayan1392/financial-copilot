using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalSavedFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FilterCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilterVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedFilters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_Actor_State_Updated",
                table: "SavedFilters",
                columns: new[] { "TenantId", "ActorId", "ActorType", "RemovedAtUtc", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_CatalogReference",
                table: "SavedFilters",
                columns: new[] { "FilterCode", "FilterVersion", "RemovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UIX_SavedFilters_Actor_Name_Active",
                table: "SavedFilters",
                columns: new[] { "TenantId", "ActorId", "ActorType", "NormalizedName" },
                unique: true,
                filter: "\"RemovedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedFilters");
        }
    }
}
