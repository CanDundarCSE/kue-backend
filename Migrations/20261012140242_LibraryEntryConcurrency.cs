using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class LibraryEntryConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "LibraryEntries",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "LibraryEntries");
        }
    }
}
