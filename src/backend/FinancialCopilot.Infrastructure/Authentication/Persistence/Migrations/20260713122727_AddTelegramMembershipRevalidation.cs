using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramMembershipRevalidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_telegram_membership_revalidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NextDueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_membership_revalidations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_membership_revalidations_auth_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_telegram_membership_revalidations_auth_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_membership_revalidations_ActorId_TenantId_Cha~",
                table: "auth_telegram_membership_revalidations",
                columns: new[] { "ActorId", "TenantId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_membership_revalidations_NextDueAtUtc_DeadLet~",
                table: "auth_telegram_membership_revalidations",
                columns: new[] { "NextDueAtUtc", "DeadLetteredAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_membership_revalidations_TenantId",
                table: "auth_telegram_membership_revalidations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_telegram_membership_revalidations");
        }
    }
}
