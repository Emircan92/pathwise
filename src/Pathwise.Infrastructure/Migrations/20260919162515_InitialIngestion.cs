using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pathwise.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerAccounts",
                columns: table => new
                {
                    Puuid = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GameName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TagLine = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ConfiguredGameName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfiguredTagLine = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Regional = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAccounts", x => x.Puuid);
                });

            migrationBuilder.CreateTable(
                name: "StoredMatches",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PlayerPuuid = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Regional = table.Column<string>(type: "TEXT", nullable: false),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ChampionName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Won = table.Column<bool>(type: "INTEGER", nullable: true),
                    TeamPosition = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    MetadataErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataErrorAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredMatches", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_StoredMatches_PlayerAccounts_PlayerPuuid",
                        column: x => x.PlayerPuuid,
                        principalTable: "PlayerAccounts",
                        principalColumn: "Puuid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchPayloads",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndpointVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true),
                    FailureHttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    RetryAfterUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPayloads", x => new { x.MatchId, x.Kind });
                    table.ForeignKey(
                        name: "FK_MatchPayloads_StoredMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "StoredMatches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredMatches_PlayerPuuid_QueueId_PlayedAtUtc",
                table: "StoredMatches",
                columns: new[] { "PlayerPuuid", "QueueId", "PlayedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPayloads");

            migrationBuilder.DropTable(
                name: "StoredMatches");

            migrationBuilder.DropTable(
                name: "PlayerAccounts");
        }
    }
}
