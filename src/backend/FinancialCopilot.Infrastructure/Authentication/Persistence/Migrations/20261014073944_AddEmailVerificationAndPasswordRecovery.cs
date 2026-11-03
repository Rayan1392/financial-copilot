using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationAndPasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerificationRequired",
                table: "auth_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "auth_email_action_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_email_action_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_email_action_tokens_auth_users_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_email_action_tokens_EmailKeyHash_Purpose_CreatedAtUtc",
                table: "auth_email_action_tokens",
                columns: new[] { "EmailKeyHash", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_email_action_tokens_TokenHash",
                table: "auth_email_action_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_email_action_tokens_UserId_Purpose_CreatedAtUtc",
                table: "auth_email_action_tokens",
                columns: new[] { "UserId", "Purpose", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_email_action_tokens");

            migrationBuilder.DropColumn(
                name: "EmailVerificationRequired",
                table: "auth_users");
        }
    }
}
