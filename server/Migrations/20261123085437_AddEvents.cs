using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOfficer",
                table: "Characters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    StartsAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Organizer = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTank = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetHealer = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetMelee = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetRanged = table.Column<int>(type: "INTEGER", nullable: false),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReservesLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Duty = table.Column<string>(type: "TEXT", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: true),
                    SetBy = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventDuties_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventHardReserves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: true),
                    Holder = table.Column<string>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    SetBy = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventHardReserves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventHardReserves_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventReserves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: true),
                    SetBy = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventReserves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventReserves_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventSignups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    ClassFile = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Spec = table.Column<string>(type: "TEXT", nullable: true),
                    Response = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Placement = table.Column<string>(type: "TEXT", nullable: false),
                    PlacementAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSignups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSignups_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventDuties_EventId_Duty",
                table: "EventDuties",
                columns: new[] { "EventId", "Duty" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventHardReserves_EventId_ItemId",
                table: "EventHardReserves",
                columns: new[] { "EventId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventReserves_EventId_Character_Priority",
                table: "EventReserves",
                columns: new[] { "EventId", "Character", "Priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_GuildId_ClientId",
                table: "Events",
                columns: new[] { "GuildId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_GuildId_StartsAt",
                table: "Events",
                columns: new[] { "GuildId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventSignups_EventId_Character",
                table: "EventSignups",
                columns: new[] { "EventId", "Character" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventDuties");

            migrationBuilder.DropTable(
                name: "EventHardReserves");

            migrationBuilder.DropTable(
                name: "EventReserves");

            migrationBuilder.DropTable(
                name: "EventSignups");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropColumn(
                name: "IsOfficer",
                table: "Characters");
        }
    }
}
