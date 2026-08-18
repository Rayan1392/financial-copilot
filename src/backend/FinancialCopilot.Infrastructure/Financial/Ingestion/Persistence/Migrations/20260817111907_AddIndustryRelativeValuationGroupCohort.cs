using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndustryRelativeValuationGroupCohort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_TransitionDate",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchStates_IndustryId",
                table: "IndustryWatchStates");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationDate_IsEffec~",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Indus~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~1",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~2",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculatio~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "IndustryWatchTransitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "IndustryWatchStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "IndustryWatchEvaluations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "IndustryRelativeValuationOutbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupExternalId",
                table: "IndustryRelativeValuationCalculations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "IndustryRelativeValuationCalculations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupTitleSnapshot",
                table: "IndustryRelativeValuationCalculations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_GroupId_CalculationId_EvaluationKi~",
                table: "IndustryWatchTransitions",
                columns: new[] { "GroupId", "CalculationId", "EvaluationKind" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_GroupId_TransitionDate",
                table: "IndustryWatchTransitions",
                columns: new[] { "GroupId", "TransitionDate" },
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true,
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_TransitionDate",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "TransitionDate" },
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchStates_GroupId",
                table: "IndustryWatchStates",
                column: "GroupId",
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchStates_IndustryId",
                table: "IndustryWatchStates",
                column: "IndustryId",
                unique: true,
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_GroupId_CalculationDate_IsEffective",
                table: "IndustryWatchEvaluations",
                columns: new[] { "GroupId", "CalculationDate", "IsEffective" },
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_GroupId_CalculationId_EvaluationKi~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "GroupId", "CalculationId", "EvaluationKind" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationDate_IsEffec~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationDate", "IsEffective" },
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true,
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationOutbox_GroupId",
                table: "IndustryRelativeValuationOutbox",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Group~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "CalculationDate", "GroupId", "CalculationVersion" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Indus~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "CalculationDate", "IndustryId", "CalculationVersion" },
                unique: true,
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationD~1",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "GroupId", "CalculationDate", "IsSelectedCurrent" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL AND \"IsSelectedCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationD~2",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "GroupId", "CalculationDate", "Status" },
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationDa~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "GroupId", "CalculationDate", "IsLatestEvaluation" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL AND \"IsLatestEvaluation\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~1",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsSelectedCurrent" },
                unique: true,
                filter: "\"GroupId\" IS NULL AND \"IsSelectedCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~2",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "Status" },
                filter: "\"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculatio~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsLatestEvaluation" },
                unique: true,
                filter: "\"GroupId\" IS NULL AND \"IsLatestEvaluation\" = TRUE");

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryRelativeValuationCalculations_IndustryGroups_GroupId",
                table: "IndustryRelativeValuationCalculations",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryRelativeValuationOutbox_IndustryGroups_GroupId",
                table: "IndustryRelativeValuationOutbox",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryWatchEvaluations_IndustryGroups_GroupId",
                table: "IndustryWatchEvaluations",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryWatchStates_IndustryGroups_GroupId",
                table: "IndustryWatchStates",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryWatchTransitions_IndustryGroups_GroupId",
                table: "IndustryWatchTransitions",
                column: "GroupId",
                principalTable: "IndustryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IndustryRelativeValuationCalculations_IndustryGroups_GroupId",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropForeignKey(
                name: "FK_IndustryRelativeValuationOutbox_IndustryGroups_GroupId",
                table: "IndustryRelativeValuationOutbox");

            migrationBuilder.DropForeignKey(
                name: "FK_IndustryWatchEvaluations_IndustryGroups_GroupId",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropForeignKey(
                name: "FK_IndustryWatchStates_IndustryGroups_GroupId",
                table: "IndustryWatchStates");

            migrationBuilder.DropForeignKey(
                name: "FK_IndustryWatchTransitions_IndustryGroups_GroupId",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_GroupId_CalculationId_EvaluationKi~",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_GroupId_TransitionDate",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_TransitionDate",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchStates_GroupId",
                table: "IndustryWatchStates");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchStates_IndustryId",
                table: "IndustryWatchStates");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_GroupId_CalculationDate_IsEffective",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_GroupId_CalculationId_EvaluationKi~",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationDate_IsEffec~",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationOutbox_GroupId",
                table: "IndustryRelativeValuationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Group~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Indus~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationD~1",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationD~2",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_GroupId_CalculationDa~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~1",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~2",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculatio~",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "IndustryWatchTransitions");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "IndustryWatchStates");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "IndustryWatchEvaluations");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "IndustryRelativeValuationOutbox");

            migrationBuilder.DropColumn(
                name: "GroupExternalId",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.DropColumn(
                name: "GroupTitleSnapshot",
                table: "IndustryRelativeValuationCalculations");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchTransitions_IndustryId_TransitionDate",
                table: "IndustryWatchTransitions",
                columns: new[] { "IndustryId", "TransitionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchStates_IndustryId",
                table: "IndustryWatchStates",
                column: "IndustryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationDate_IsEffec~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationDate", "IsEffective" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryWatchEvaluations_IndustryId_CalculationId_Evaluatio~",
                table: "IndustryWatchEvaluations",
                columns: new[] { "IndustryId", "CalculationId", "EvaluationKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_CalculationDate_Indus~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "CalculationDate", "IndustryId", "CalculationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~1",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsSelectedCurrent" },
                unique: true,
                filter: "\"IsSelectedCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculati~2",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IndustryRelativeValuationCalculations_IndustryId_Calculatio~",
                table: "IndustryRelativeValuationCalculations",
                columns: new[] { "IndustryId", "CalculationDate", "IsLatestEvaluation" },
                unique: true,
                filter: "\"IsLatestEvaluation\" = TRUE");
        }
    }
}
