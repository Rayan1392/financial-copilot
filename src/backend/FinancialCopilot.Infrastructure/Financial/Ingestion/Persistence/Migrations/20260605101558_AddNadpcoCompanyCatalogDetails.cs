using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNadpcoCompanyCatalogDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptionDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptionDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessStartDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessStartDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyCode",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanySymbolEnglish",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanySymbolPinglish",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnlistedDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnlistedDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstablishmentDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstablishmentDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundTypeId",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FundTypeTitle",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InExchange",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpoDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpoDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketBoard",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrecedencyRight",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationCity",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationDateGregorian",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationDateJalali",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationProvince",
                table: "Companies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptionDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AcceptionDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BusinessStartDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BusinessStartDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CompanyCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CompanySymbolEnglish",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CompanySymbolPinglish",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EnlistedDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EnlistedDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EstablishmentDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "EstablishmentDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FundTypeId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FundTypeTitle",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "InExchange",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IpoDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IpoDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "MarketBoard",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "PrecedencyRight",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RegistrationCity",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RegistrationDateGregorian",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RegistrationDateJalali",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "RegistrationProvince",
                table: "Companies");
        }
    }
}
