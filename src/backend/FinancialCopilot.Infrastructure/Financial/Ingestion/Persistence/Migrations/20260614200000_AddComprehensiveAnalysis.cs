using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComprehensiveAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprehensiveAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PersianCreatedAt = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprehensiveAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComprehensiveAnalysisTags",
                columns: table => new
                {
                    AnalysisId = table.Column<long>(type: "bigint", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    TagName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TagSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TagTypeId = table.Column<int>(type: "integer", nullable: false),
                    IsAnalytic = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprehensiveAnalysisTags", x => new { x.AnalysisId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ComprehensiveAnalysisTags_ComprehensiveAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalTable: "ComprehensiveAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComprehensiveAnalysisCategories",
                columns: table => new
                {
                    AnalysisId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprehensiveAnalysisCategories", x => new { x.AnalysisId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_ComprehensiveAnalysisCategories_ComprehensiveAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalTable: "ComprehensiveAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComprehensiveAnalysisSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PagesTotal = table.Column<int>(type: "integer", nullable: false),
                    ItemsSynced = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprehensiveAnalysisSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensiveAnalyses_CreatedAt",
                table: "ComprehensiveAnalyses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensiveAnalysisTags_TagName_TagTypeId_AnalysisId",
                table: "ComprehensiveAnalysisTags",
                columns: new[] { "TagName", "TagTypeId", "AnalysisId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensiveAnalysisTags_TagName_IsAnalytic_AnalysisId",
                table: "ComprehensiveAnalysisTags",
                columns: new[] { "TagName", "IsAnalytic", "AnalysisId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensiveAnalysisTags_TagTypeId_AnalysisId",
                table: "ComprehensiveAnalysisTags",
                columns: new[] { "TagTypeId", "AnalysisId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprehensiveAnalysisSyncLogs_StartedAt",
                table: "ComprehensiveAnalysisSyncLogs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ComprehensiveAnalysisCategories");
            migrationBuilder.DropTable(name: "ComprehensiveAnalysisTags");
            migrationBuilder.DropTable(name: "ComprehensiveAnalyses");
            migrationBuilder.DropTable(name: "ComprehensiveAnalysisSyncLogs");
        }
    }
}
