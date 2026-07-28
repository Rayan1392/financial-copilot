using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    [DbContext(typeof(FinancialIngestionDbContext))]
    [Migration("20260727170000_AddDisclosurePublicationDates")]
    /// <inheritdoc />
    public partial class AddDisclosurePublicationDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PublishedAt",
                table: "FinancialStatements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PublishedAt",
                table: "MonthlyReports",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PublishedAt", table: "FinancialStatements");
            migrationBuilder.DropColumn(name: "PublishedAt", table: "MonthlyReports");
        }
    }
}
