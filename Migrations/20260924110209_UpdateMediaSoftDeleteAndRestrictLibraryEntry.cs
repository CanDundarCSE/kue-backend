using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMediaSoftDeleteAndRestrictLibraryEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryEntries_Media_MediaId",
                table: "LibraryEntries");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Media",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryEntries_Media_MediaId",
                table: "LibraryEntries",
                column: "MediaId",
                principalTable: "Media",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryEntries_Media_MediaId",
                table: "LibraryEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Media");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryEntries_Media_MediaId",
                table: "LibraryEntries",
                column: "MediaId",
                principalTable: "Media",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
