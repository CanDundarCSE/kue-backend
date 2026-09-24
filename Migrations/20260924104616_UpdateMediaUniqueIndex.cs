using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMediaUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Media_ExternalSource_ExternalId",
                table: "Media");

            migrationBuilder.CreateIndex(
                name: "IX_Media_MediaType_ExternalSource_ExternalId",
                table: "Media",
                columns: new[] { "MediaType", "ExternalSource", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Media_MediaType_ExternalSource_ExternalId",
                table: "Media");

            migrationBuilder.CreateIndex(
                name: "IX_Media_ExternalSource_ExternalId",
                table: "Media",
                columns: new[] { "ExternalSource", "ExternalId" },
                unique: true);
        }
    }
}
