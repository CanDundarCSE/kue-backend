using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kue.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticPlaybackTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackCandidates");

            migrationBuilder.AddColumn<string>(
                name: "SeasonEpisodeCounts",
                table: "Media",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeasonEpisodeCounts",
                table: "Media");

            migrationBuilder.CreateTable(
                name: "PlaybackCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MediaId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Episode = table.Column<int>(type: "integer", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalSource = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Season = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackCandidates_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaybackCandidates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackCandidates_MediaId",
                table: "PlaybackCandidates",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackCandidates_UserId_MediaType_ExternalSource_External~",
                table: "PlaybackCandidates",
                columns: new[] { "UserId", "MediaType", "ExternalSource", "ExternalId", "Season", "Episode", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackCandidates_UserId_Status_LastSeenAt",
                table: "PlaybackCandidates",
                columns: new[] { "UserId", "Status", "LastSeenAt" });
        }
    }
}
