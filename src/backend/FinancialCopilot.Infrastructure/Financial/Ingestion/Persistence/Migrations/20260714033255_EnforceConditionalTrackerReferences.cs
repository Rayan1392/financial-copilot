using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceConditionalTrackerReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_AlertRuleEvaluationStates_AlertRules_RuleId",
                table: "AlertRuleEvaluationStates",
                column: "RuleId",
                principalTable: "AlertRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AlertRuleTriggers_AlertRules_RuleId",
                table: "AlertRuleTriggers",
                column: "RuleId",
                principalTable: "AlertRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AlertRuleTriggers_NotificationIntents_NotificationIntentId",
                table: "AlertRuleTriggers",
                column: "NotificationIntentId",
                principalTable: "NotificationIntents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlertRuleEvaluationStates_AlertRules_RuleId",
                table: "AlertRuleEvaluationStates");

            migrationBuilder.DropForeignKey(
                name: "FK_AlertRuleTriggers_AlertRules_RuleId",
                table: "AlertRuleTriggers");

            migrationBuilder.DropForeignKey(
                name: "FK_AlertRuleTriggers_NotificationIntents_NotificationIntentId",
                table: "AlertRuleTriggers");
        }
    }
}
