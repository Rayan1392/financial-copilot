using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProductRevenueMixTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProductRevenueMix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanySymbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReportYear = table.Column<int>(type: "integer", nullable: false),
                    ReportMonth = table.Column<byte>(type: "smallint", nullable: false),
                    FiscalEndDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProductName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ProductionQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesRate = table.Column<decimal>(type: "numeric", nullable: true),
                    SalesAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCompanySalesAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    RevenueSharePercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    ProductRank = table.Column<int>(type: "integer", nullable: false),
                    IsDominantProduct = table.Column<bool>(type: "boolean", nullable: false),
                    SourceProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProductRevenueMix", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProductRevenueMix_CompanyPeriod",
                table: "CompanyProductRevenueMix",
                columns: new[] { "ExternalCompanyId", "ReportYear", "ReportMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProductRevenueMix_CompanySymbol",
                table: "CompanyProductRevenueMix",
                column: "CompanySymbol");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProductRevenueMix_ProductRank",
                table: "CompanyProductRevenueMix",
                column: "ProductRank");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProductRevenueMix");
        }
    }
}
