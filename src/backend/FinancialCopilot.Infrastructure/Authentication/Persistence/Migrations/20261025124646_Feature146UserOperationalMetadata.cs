using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature146UserOperationalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auth_user_tenants_TenantId",
                table: "auth_user_tenants");

            migrationBuilder.DropIndex(
                name: "IX_auth_telegram_account_links_TenantId",
                table: "auth_telegram_account_links");

            migrationBuilder.DropIndex(
                name: "IX_auth_external_identities_TenantId",
                table: "auth_external_identities");

            migrationBuilder.CreateTable(
                name: "auth_user_operational_metadata",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcquisitionSourceCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastSuccessfulLoginAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginProviderCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_user_operational_metadata", x => x.UserId);
                    table.CheckConstraint("CK_auth_user_operational_metadata_acquisition_source_code", "\"AcquisitionSourceCode\" IN ('native', 'tahlilapp', 'unknown')");
                    table.ForeignKey(
                        name: "FK_auth_user_operational_metadata_auth_users_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing identity accounts have no reliable historical source or timestamp evidence.
            migrationBuilder.Sql("""
                INSERT INTO "auth_user_operational_metadata"
                    ("UserId", "RegisteredAtUtc", "AcquisitionSourceCode", "LastSuccessfulLoginAtUtc", "LastLoginProviderCode", "ConcurrencyVersion")
                SELECT "Id", NULL, 'unknown', NULL, NULL, 1
                FROM "auth_users"
                ON CONFLICT ("UserId") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_auth_users_IsEnabled_LockoutEnd_Id",
                table: "auth_users",
                columns: new[] { "IsEnabled", "LockoutEnd", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_tenants_TenantId_UserId",
                table: "auth_user_tenants",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_account_links_TenantId_ActorId_RevokedAtUtc",
                table: "auth_telegram_account_links",
                columns: new[] { "TenantId", "ActorId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_TenantId_Provider_ExternalUserId",
                table: "auth_external_identities",
                columns: new[] { "TenantId", "Provider", "ExternalUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_TenantId_Provider_LocalUserId_Stat~",
                table: "auth_external_identities",
                columns: new[] { "TenantId", "Provider", "LocalUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_operational_metadata_AcquisitionSourceCode_Regist~",
                table: "auth_user_operational_metadata",
                columns: new[] { "AcquisitionSourceCode", "RegisteredAtUtc", "UserId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_operational_metadata_LastSuccessfulLoginAtUtc_Use~",
                table: "auth_user_operational_metadata",
                columns: new[] { "LastSuccessfulLoginAtUtc", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_operational_metadata_RegisteredAtUtc_UserId",
                table: "auth_user_operational_metadata",
                columns: new[] { "RegisteredAtUtc", "UserId" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_user_operational_metadata");

            migrationBuilder.DropIndex(
                name: "IX_auth_users_IsEnabled_LockoutEnd_Id",
                table: "auth_users");

            migrationBuilder.DropIndex(
                name: "IX_auth_user_tenants_TenantId_UserId",
                table: "auth_user_tenants");

            migrationBuilder.DropIndex(
                name: "IX_auth_telegram_account_links_TenantId_ActorId_RevokedAtUtc",
                table: "auth_telegram_account_links");

            migrationBuilder.DropIndex(
                name: "IX_auth_external_identities_TenantId_Provider_ExternalUserId",
                table: "auth_external_identities");

            migrationBuilder.DropIndex(
                name: "IX_auth_external_identities_TenantId_Provider_LocalUserId_Stat~",
                table: "auth_external_identities");

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_tenants_TenantId",
                table: "auth_user_tenants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_account_links_TenantId",
                table: "auth_telegram_account_links",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_TenantId",
                table: "auth_external_identities",
                column: "TenantId");
        }
    }
}
