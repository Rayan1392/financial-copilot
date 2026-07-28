using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNadpcoFundamentalIndexCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundamentalIndexCatchUpRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FromShamsiYear = table.Column<int>(type: "integer", nullable: false),
                    ToShamsiYear = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompaniesConsidered = table.Column<int>(type: "integer", nullable: false),
                    RequestsEnqueued = table.Column<int>(type: "integer", nullable: false),
                    FailedCompanies = table.Column<int>(type: "integer", nullable: false),
                    FailedCompanyIdsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Diagnostics = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LockOwner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LockLeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundamentalIndexCatchUpRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NadpcoFundamentalIndexObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalCompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExternalStatementId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyIndexId = table.Column<int>(type: "integer", nullable: false),
                    CompanyIndexTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CompanyIndexGroupId = table.Column<int>(type: "integer", nullable: true),
                    CompanyIndexGroupTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CompanyIndexValue = table.Column<decimal>(type: "numeric", nullable: true),
                    CompanyIndexUnit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    JalaliFiscalYearEnd = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    JalaliPeriodEnd = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    JalaliAnnouncementDate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsAudited = table.Column<bool>(type: "boolean", nullable: false),
                    IsRepresented = table.Column<bool>(type: "boolean", nullable: false),
                    IsComposing = table.Column<bool>(type: "boolean", nullable: false),
                    IsGovernedCandidate = table.Column<bool>(type: "boolean", nullable: false),
                    SourcePayloadChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NadpcoFundamentalIndexObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundamentalIndexCatchUpRuns_LockLeaseExpiresAt",
                table: "FundamentalIndexCatchUpRuns",
                column: "LockLeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_FundamentalIndexCatchUpRuns_StartedAt",
                table: "FundamentalIndexCatchUpRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FundamentalIndexCatchUpRuns_Status",
                table: "FundamentalIndexCatchUpRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoFundamentalIndexObservations_CompanyIndexId_IsGoverne~",
                table: "NadpcoFundamentalIndexObservations",
                columns: new[] { "CompanyIndexId", "IsGovernedCandidate" });

            migrationBuilder.CreateIndex(
                name: "IX_NadpcoFundamentalIndexObservations_ProviderName_ExternalCom~",
                table: "NadpcoFundamentalIndexObservations",
                columns: new[] { "ProviderName", "ExternalCompanyId", "CompanyIndexId", "PeriodType", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundamentalIndexCatchUpRuns");

            migrationBuilder.DropTable(
                name: "NadpcoFundamentalIndexObservations");
        }
    }
}
