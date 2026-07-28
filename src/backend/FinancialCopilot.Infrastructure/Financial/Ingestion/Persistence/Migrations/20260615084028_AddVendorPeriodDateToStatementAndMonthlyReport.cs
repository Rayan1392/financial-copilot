using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPeriodDateToStatementAndMonthlyReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "VendorPeriodDate",
                table: "FinancialStatements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VendorPeriodDate",
                table: "MonthlyReports",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorPeriodDate",
                table: "FinancialStatements");

            migrationBuilder.DropColumn(
                name: "VendorPeriodDate",
                table: "MonthlyReports");
        }
    }
}
