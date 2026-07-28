using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoavaranFinancialStatementSourceCatalogAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_MetricCode",
                table: "FinancialStatementLineItems");

            migrationBuilder.AddColumn<bool>(
                name: "IsAudited",
                table: "FinancialStatements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsComposing",
                table: "FinancialStatements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRepresented",
                table: "FinancialStatements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatementTitle",
                table: "FinancialStatements",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetricCode",
                table: "FinancialStatementLineItems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceItemCatalogId",
                table: "FinancialStatementLineItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinancialStatementSourceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceItemId = table.Column<int>(type: "integer", nullable: false),
                    TitleFa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TitleEn = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Unit = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialStatementSourceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialStatementSourceItemMetricMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceItemCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialStatementSourceItemMetricMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialStatementSourceItemMetricMappings_FinancialStateme~",
                        column: x => x.SourceItemCatalogId,
                        principalTable: "FinancialStatementSourceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalCompanyId_Statemen~",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalCompanyId", "StatementType", "PeriodType", "PeriodEnd", "IsComposing", "IsAudited", "IsRepresented" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalStatementId", "StatementType", "IsAudited", "IsRepresented", "IsComposing" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_MetricCode",
                table: "FinancialStatementLineItems",
                columns: new[] { "FinancialStatementId", "MetricCode" },
                filter: "\"MetricCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_SourceItem~",
                table: "FinancialStatementLineItems",
                columns: new[] { "FinancialStatementId", "SourceItemCatalogId" },
                unique: true,
                filter: "\"SourceItemCatalogId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementLineItems_SourceItemCatalogId",
                table: "FinancialStatementLineItems",
                column: "SourceItemCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementSourceItemMetricMappings_MetricCode",
                table: "FinancialStatementSourceItemMetricMappings",
                column: "MetricCode");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementSourceItemMetricMappings_SourceItemCatalo~",
                table: "FinancialStatementSourceItemMetricMappings",
                column: "SourceItemCatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementSourceItems_ProviderName_StatementType_So~",
                table: "FinancialStatementSourceItems",
                columns: new[] { "ProviderName", "StatementType", "SourceItemId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialStatementLineItems_FinancialStatementSourceItems_S~",
                table: "FinancialStatementLineItems",
                column: "SourceItemCatalogId",
                principalTable: "FinancialStatementSourceItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialStatementLineItems_FinancialStatementSourceItems_S~",
                table: "FinancialStatementLineItems");

            migrationBuilder.DropTable(
                name: "FinancialStatementSourceItemMetricMappings");

            migrationBuilder.DropTable(
                name: "FinancialStatementSourceItems");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalCompanyId_Statemen~",
                table: "FinancialStatements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_MetricCode",
                table: "FinancialStatementLineItems");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_SourceItem~",
                table: "FinancialStatementLineItems");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatementLineItems_SourceItemCatalogId",
                table: "FinancialStatementLineItems");

            migrationBuilder.DropColumn(
                name: "IsAudited",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "IsComposing",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "IsRepresented",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "StatementTitle",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "SourceItemCatalogId",
                table: "FinancialStatementLineItems");

            migrationBuilder.AlterColumn<string>(
                name: "MetricCode",
                table: "FinancialStatementLineItems",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_ProviderName_ExternalStatementId_Statem~",
                table: "FinancialStatements",
                columns: new[] { "ProviderName", "ExternalStatementId", "StatementType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatementLineItems_FinancialStatementId_MetricCode",
                table: "FinancialStatementLineItems",
                columns: new[] { "FinancialStatementId", "MetricCode" },
                unique: true);
        }
    }
}
