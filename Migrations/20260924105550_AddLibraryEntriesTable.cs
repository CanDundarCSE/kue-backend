using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryEntriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MediaId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: true),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryEntries_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_MediaId",
                table: "LibraryEntries",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_UserId_IsFavorite",
                table: "LibraryEntries",
                columns: new[] { "UserId", "IsFavorite" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_UserId_MediaId",
                table: "LibraryEntries",
                columns: new[] { "UserId", "MediaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_UserId_Status",
                table: "LibraryEntries",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryEntries");
        }
    }
}
