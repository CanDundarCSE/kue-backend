using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomListVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new tri-state column first, then copy the old flag into
            // it before the boolean is dropped. Without the copy every list
            // would silently become public.
            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "CustomLists",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "public");

            migrationBuilder.Sql(
                "UPDATE \"CustomLists\" SET \"Visibility\" = 'private' WHERE \"IsPublic\" = FALSE;");

            migrationBuilder.DropIndex(
                name: "IX_CustomLists_IsPublic",
                table: "CustomLists");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "CustomLists");

            migrationBuilder.CreateIndex(
                name: "IX_CustomLists_Visibility",
                table: "CustomLists",
                column: "Visibility");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "CustomLists",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // "friends" has no boolean equivalent; downgrading makes those
            // lists private so nothing becomes more visible than before.
            migrationBuilder.Sql(
                "UPDATE \"CustomLists\" SET \"IsPublic\" = TRUE WHERE \"Visibility\" = 'public';");

            migrationBuilder.DropIndex(
                name: "IX_CustomLists_Visibility",
                table: "CustomLists");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "CustomLists");

            migrationBuilder.CreateIndex(
                name: "IX_CustomLists_IsPublic",
                table: "CustomLists",
                column: "IsPublic");
        }
    }
}
