using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTahlilAppEntitlementSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEntitlementSyncAtUtc",
                table: "auth_external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastExternalRevision",
                table: "auth_external_identities",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastKnownSubscriptionExpiresAtUtc",
                table: "auth_external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncFailureCategory",
                table: "auth_external_identities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncedPlanCode",
                table: "auth_external_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "auth_external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "auth_external_identities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextEntitlementSyncDueAtUtc",
                table: "auth_external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncAttemptCount",
                table: "auth_external_identities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PendingEmail",
                table: "auth_email_action_tokens",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_NextEntitlementSyncDueAtUtc",
                table: "auth_external_identities",
                column: "NextEntitlementSyncDueAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auth_external_identities_NextEntitlementSyncDueAtUtc",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LastEntitlementSyncAtUtc",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LastExternalRevision",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LastKnownSubscriptionExpiresAtUtc",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LastSyncFailureCategory",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LastSyncedPlanCode",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "NextEntitlementSyncDueAtUtc",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "SyncAttemptCount",
                table: "auth_external_identities");

            migrationBuilder.DropColumn(
                name: "PendingEmail",
                table: "auth_email_action_tokens");
        }
    }
}
