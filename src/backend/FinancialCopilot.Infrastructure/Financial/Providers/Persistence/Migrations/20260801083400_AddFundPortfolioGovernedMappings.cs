using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence.Migrations;

public partial class AddFundPortfolioGovernedMappings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "FundPortfolioGovernedMappings", columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), MappingType = table.Column<int>(type: "integer", nullable: false), RawValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false), NormalizedValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false), ResolutionJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false), IsApproved = table.Column<bool>(type: "boolean", nullable: false), ResolvedByActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false), ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false) }, constraints: table => table.PrimaryKey("PK_FundPortfolioGovernedMappings", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_FundPortfolioGovernedMappings_MappingType_RawValue", table: "FundPortfolioGovernedMappings", columns: new[] { "MappingType", "RawValue" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "FundPortfolioGovernedMappings");
}
