using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenCleanupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt_IsRevoked_RevokedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "ExpiresAt", "IsRevoked", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ExpiresAt_IsRevoked_RevokedAtUtc",
                table: "RefreshTokens");
        }
    }
}
