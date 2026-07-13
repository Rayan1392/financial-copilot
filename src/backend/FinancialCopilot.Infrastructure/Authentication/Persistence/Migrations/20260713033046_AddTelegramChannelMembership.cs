using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramChannelMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_telegram_channel_membership_verifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailureCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsLatest = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_telegram_channel_membership_verifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_telegram_channel_membership_verifications_auth_tenants~",
                        column: x => x.TenantId,
                        principalTable: "auth_tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_auth_telegram_channel_membership_verifications_auth_users_A~",
                        column: x => x.ActorId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_channel_membership_verifications_ActorId_Tena~",
                table: "auth_telegram_channel_membership_verifications",
                columns: new[] { "ActorId", "TenantId", "ChannelId", "IsLatest" },
                unique: true,
                filter: "\"IsLatest\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_channel_membership_verifications_ExpiresAtUtc~",
                table: "auth_telegram_channel_membership_verifications",
                columns: new[] { "ExpiresAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_channel_membership_verifications_TelegramUser~",
                table: "auth_telegram_channel_membership_verifications",
                columns: new[] { "TelegramUserId", "ChannelId", "VerifiedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_telegram_channel_membership_verifications_TenantId",
                table: "auth_telegram_channel_membership_verifications",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_telegram_channel_membership_verifications");
        }
    }
}
