using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrichCodalCompanyMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LinkageBasis",
                table: "Symbols",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "ProviderSyncRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyIsin",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanySymbol",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndustryId",
                table: "Companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstrumentCode",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstrumentRefPlaceholder",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MarketId",
                table: "Companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameEnglish",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceModifiedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SymbolIsin",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TseSymbol",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Industries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Industries_Industries_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IndustryGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustryGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Markets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Markets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_GroupId",
                table: "Companies",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_IndustryId",
                table: "Companies",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_MarketId",
                table: "Companies",
                column: "MarketId");

            migrationBuilder.CreateIndex(
                name: "IX_Industries_ParentId",
                table: "Industries",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Industries_ProviderName_ExternalId",
                table: "Industries",
                columns: new[] { "ProviderName", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryGroups_ProviderName_ExternalId",
                table: "IndustryGroups",
                columns: new[] { "ProviderName", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Markets_ProviderName_ExternalId",
                table: "Markets",
                columns: new[] { "ProviderName", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Industries_IndustryId",
                table: "Companies",
                column: "IndustryId",
                principalTable: "Industries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_IndustryGroups_GroupId",
                table: "Companies",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Markets_MarketId",
                table: "Companies",
                column: "MarketId",
                principalTable: "Markets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Industries_IndustryId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_IndustryGroups_GroupId",
                table: "Companies");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Markets_MarketId",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "Industries");

            migrationBuilder.DropTable(
                name: "IndustryGroups");

            migrationBuilder.DropTable(
                name: "Markets");

            migrationBuilder.DropIndex(
                name: "IX_Companies_GroupId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_IndustryId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_MarketId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LinkageBasis",
                table: "Symbols");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "ProviderSyncRuns");

            migrationBuilder.DropColumn(
                name: "CompanyIsin",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CompanySymbol",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IndustryId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "InstrumentCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "InstrumentRefPlaceholder",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "MarketId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NameEnglish",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SourceModifiedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SymbolIsin",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TseSymbol",
                table: "Companies");
        }
    }
}
