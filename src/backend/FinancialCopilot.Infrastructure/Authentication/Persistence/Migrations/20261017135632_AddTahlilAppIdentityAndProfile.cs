using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTahlilAppIdentityAndProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthenticationMethod",
                table: "auth_refresh_tokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_external_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LocalUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLaunchAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_external_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_external_identities_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_external_identities_auth_users_LocalUserId",
                        column: x => x.LocalUserId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_tahlilapp_launch_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_tahlilapp_launch_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "auth_user_profiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NationalIdEncrypted = table.Column<string>(type: "text", nullable: true),
                    NationalIdLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    MobileEncrypted = table.Column<string>(type: "text", nullable: true),
                    MobileLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProfileSyncedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_user_profiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_auth_user_profiles_auth_users_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_LocalUserId",
                table: "auth_external_identities",
                column: "LocalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_Provider_ExternalUserId",
                table: "auth_external_identities",
                columns: new[] { "Provider", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_Provider_LocalUserId",
                table: "auth_external_identities",
                columns: new[] { "Provider", "LocalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_external_identities_TenantId",
                table: "auth_external_identities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_tahlilapp_launch_attempts_CodeHash",
                table: "auth_tahlilapp_launch_attempts",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_tahlilapp_launch_attempts_CreatedAtUtc",
                table: "auth_tahlilapp_launch_attempts",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_external_identities");

            migrationBuilder.DropTable(
                name: "auth_tahlilapp_launch_attempts");

            migrationBuilder.DropTable(
                name: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "AuthenticationMethod",
                table: "auth_refresh_tokens");
        }
    }
}
