using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAddonCredentialLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "AddonCredentials",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "AddonCredentials");
        }
    }
}
