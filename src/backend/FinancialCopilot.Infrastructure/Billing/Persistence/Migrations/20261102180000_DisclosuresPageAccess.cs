using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Billing.Persistence.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("20261102180000_DisclosuresPageAccess")]
public partial class DisclosuresPageAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "billing_plan_capabilities",
            columns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion", "IsEnabled", "Limit" },
            columnTypes: new[] { "character varying(160)", "character varying(64)", "character varying(64)", "boolean", "numeric(18,4)" },
            values: new object[,]
            {
                { "Page.Disclosures", "Free", "v1", false, null },
                { "Page.Disclosures", "Max", "v1", true, null },
                { "Page.Disclosures", "Plus", "v1", true, null },
                { "Page.Disclosures", "Pro", "v1", true, null }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "billing_plan_capabilities",
            keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
            keyColumnTypes: new[] { "character varying(160)", "character varying(64)", "character varying(64)" },
            keyValues: new object[] { "Page.Disclosures", "Free", "v1" });
        migrationBuilder.DeleteData(
            table: "billing_plan_capabilities",
            keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
            keyColumnTypes: new[] { "character varying(160)", "character varying(64)", "character varying(64)" },
            keyValues: new object[] { "Page.Disclosures", "Max", "v1" });
        migrationBuilder.DeleteData(
            table: "billing_plan_capabilities",
            keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
            keyColumnTypes: new[] { "character varying(160)", "character varying(64)", "character varying(64)" },
            keyValues: new object[] { "Page.Disclosures", "Plus", "v1" });
        migrationBuilder.DeleteData(
            table: "billing_plan_capabilities",
            keyColumns: new[] { "CapabilityCode", "PlanCode", "PolicyVersion" },
            keyColumnTypes: new[] { "character varying(160)", "character varying(64)", "character varying(64)" },
            keyValues: new object[] { "Page.Disclosures", "Pro", "v1" });
    }
}
