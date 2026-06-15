using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTickerEnTickerAndCompanyIdFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "MonthlyReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "FinancialStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnTicker",
                table: "Companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ticker",
                table: "Companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_CompanyId",
                table: "MonthlyReports",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialStatements_CompanyId",
                table: "FinancialStatements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_EnTicker",
                table: "Companies",
                column: "EnTicker",
                unique: true,
                filter: "\"EnTicker\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Ticker",
                table: "Companies",
                column: "Ticker",
                unique: true,
                filter: "\"Ticker\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialStatements_Companies_CompanyId",
                table: "FinancialStatements",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyReports_Companies_CompanyId",
                table: "MonthlyReports",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialStatements_Companies_CompanyId",
                table: "FinancialStatements");

            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyReports_Companies_CompanyId",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyReports_CompanyId",
                table: "MonthlyReports");

            migrationBuilder.DropIndex(
                name: "IX_FinancialStatements_CompanyId",
                table: "FinancialStatements");

            migrationBuilder.DropIndex(
                name: "IX_Companies_EnTicker",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Ticker",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "EnTicker",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Ticker",
                table: "Companies");
        }
    }
}
