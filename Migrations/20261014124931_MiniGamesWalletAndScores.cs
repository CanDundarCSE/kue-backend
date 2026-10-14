using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class MiniGamesWalletAndScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameBalance",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GameTotalEarned",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MiniGameScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Game = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    RoundsWon = table.Column<int>(type: "integer", nullable: false),
                    RoundsTotal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniGameScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniGameScores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniGameScores_Game_Score",
                table: "MiniGameScores",
                columns: new[] { "Game", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniGameScores_UserId_CreatedAt",
                table: "MiniGameScores",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniGameScores");

            migrationBuilder.DropColumn(
                name: "GameBalance",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GameTotalEarned",
                table: "Users");
        }
    }
}
