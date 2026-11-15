using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialCopilot.Infrastructure.Authentication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feature160SelfServiceProfileEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "AvatarBytes",
                table: "auth_user_profiles",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarContentType",
                table: "auth_user_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactMobileEncrypted",
                table: "auth_user_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactMobileHash",
                table: "auth_user_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactMobileLast4",
                table: "auth_user_profiles",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalFirstName",
                table: "auth_user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalLastName",
                table: "auth_user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auth_mobile_otps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MobileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MobileEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_mobile_otps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_mobile_otps_auth_users_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_profiles_ContactMobileHash",
                table: "auth_user_profiles",
                column: "ContactMobileHash",
                unique: true,
                filter: "\"ContactMobileHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_auth_mobile_otps_CodeHash",
                table: "auth_mobile_otps",
                column: "CodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_auth_mobile_otps_UserId_CreatedAtUtc",
                table: "auth_mobile_otps",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_mobile_otps_UserId_MobileHash_CreatedAtUtc",
                table: "auth_mobile_otps",
                columns: new[] { "UserId", "MobileHash", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_mobile_otps");

            migrationBuilder.DropIndex(
                name: "IX_auth_user_profiles_ContactMobileHash",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "AvatarBytes",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "AvatarContentType",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "ContactMobileEncrypted",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "ContactMobileHash",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "ContactMobileLast4",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "LocalFirstName",
                table: "auth_user_profiles");

            migrationBuilder.DropColumn(
                name: "LocalLastName",
                table: "auth_user_profiles");
        }
    }
}
