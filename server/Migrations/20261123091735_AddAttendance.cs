using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RaidSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Instance = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<string>(type: "TEXT", nullable: false),
                    Leader = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaidSessions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RaidSessions_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    By = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_RaidSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RaidSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaidLoot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Voided = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoidNote = table.Column<string>(type: "TEXT", nullable: true),
                    VoidBy = table.Column<string>(type: "TEXT", nullable: true),
                    VoidAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidLoot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaidLoot_RaidSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RaidSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RaidParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    ClassFile = table.Column<string>(type: "TEXT", nullable: true),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaidParticipants_RaidSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RaidSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_SessionId_Character",
                table: "AttendanceCorrections",
                columns: new[] { "SessionId", "Character" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaidLoot_SessionId_Character_ItemId_Ordinal",
                table: "RaidLoot",
                columns: new[] { "SessionId", "Character", "ItemId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipants_SessionId_Character",
                table: "RaidParticipants",
                columns: new[] { "SessionId", "Character" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaidSessions_EventId",
                table: "RaidSessions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_RaidSessions_GuildId_Key",
                table: "RaidSessions",
                columns: new[] { "GuildId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaidSessions_GuildId_StartedAt",
                table: "RaidSessions",
                columns: new[] { "GuildId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "RaidLoot");

            migrationBuilder.DropTable(
                name: "RaidParticipants");

            migrationBuilder.DropTable(
                name: "RaidSessions");
        }
    }
}
