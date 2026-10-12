using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUsernameCaseInsensitive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Race-proofs the case-insensitive uniqueness check in
            // AuthService.RegisterAsync: "Alice" and "alice" can't coexist
            // even under concurrent registrations.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""IX_Users_Username_Lower"" ON ""Users"" (LOWER(""Username""));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX ""IX_Users_Username_Lower"";");
        }
    }
}
