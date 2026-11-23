using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class CharacterQuestLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Les quêtes quittent la table Characters (module Annuaire) pour une table
            // propre au module Quêtes. Ordre : nouvelle table, recopie, puis seulement
            // suppression des anciennes colonnes, pour ne perdre aucun relevé.
            migrationBuilder.CreateTable(
                name: "CharacterQuestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<int>(type: "INTEGER", nullable: false),
                    Character = table.Column<string>(type: "TEXT", nullable: false),
                    QuestsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuestLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterQuestLogs_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuestLogs_GuildId_Character",
                table: "CharacterQuestLogs",
                columns: new[] { "GuildId", "Character" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "CharacterQuestLogs" ("GuildId", "Character", "QuestsJson", "UpdatedUtc")
                SELECT "GuildId", "Name", "QuestsJson", COALESCE("QuestsUpdatedUtc", "LastSeenUtc", CURRENT_TIMESTAMP)
                FROM "Characters"
                WHERE "QuestsJson" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "QuestsJson",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "QuestsUpdatedUtc",
                table: "Characters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuestsJson",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuestsUpdatedUtc",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Characters"
                SET "QuestsJson" = (SELECT q."QuestsJson" FROM "CharacterQuestLogs" q WHERE q."GuildId" = "Characters"."GuildId" AND q."Character" = "Characters"."Name"),
                    "QuestsUpdatedUtc" = (SELECT q."UpdatedUtc" FROM "CharacterQuestLogs" q WHERE q."GuildId" = "Characters"."GuildId" AND q."Character" = "Characters"."Name");
                """);

            migrationBuilder.DropTable(
                name: "CharacterQuestLogs");
        }
    }
}
