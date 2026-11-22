using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class MultiGuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Metas");

            migrationBuilder.DropIndex(
                name: "IX_Members_DiscordId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_CraftOrders_ClientId",
                table: "CraftOrders");

            migrationBuilder.DropIndex(
                name: "IX_Characters_Name",
                table: "Characters");

            migrationBuilder.AddColumn<int>(
                name: "GuildId",
                table: "Members",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GuildId",
                table: "CraftOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GuildId",
                table: "Characters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    DiscordServerId = table.Column<string>(type: "TEXT", nullable: true),
                    ApiToken = table.Column<string>(type: "TEXT", nullable: false),
                    InviteCode = table.Column<string>(type: "TEXT", nullable: false),
                    SyncNicknames = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrdersChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    DataVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByDiscordId = table.Column<string>(type: "TEXT", nullable: true),
                    BotRemovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                });

            // Données d'avant le multi-guildes : rattachées à une « guilde
            // historique » (id 1), que le démarrage complète depuis la config
            // (nom, token, serveur Discord). Rien n'est créé sur une base vide.
            migrationBuilder.Sql(@"
INSERT INTO Guilds (Id, Name, Slug, ApiToken, InviteCode, SyncNicknames, DataVersion, CreatedUtc)
SELECT 1, 'Guilde historique', 'guilde-historique', 'legacy-' || lower(hex(randomblob(16))), lower(hex(randomblob(8))), 1, 0, datetime('now')
WHERE EXISTS (SELECT 1 FROM Characters) OR EXISTS (SELECT 1 FROM Members) OR EXISTS (SELECT 1 FROM CraftOrders);
UPDATE Characters SET GuildId = 1 WHERE GuildId = 0;
UPDATE Members SET GuildId = 1 WHERE GuildId = 0;
UPDATE CraftOrders SET GuildId = 1 WHERE GuildId = 0;
");

            migrationBuilder.CreateIndex(
                name: "IX_Members_GuildId_DiscordId",
                table: "Members",
                columns: new[] { "GuildId", "DiscordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_UploadToken",
                table: "Members",
                column: "UploadToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftOrders_GuildId_ClientId",
                table: "CraftOrders",
                columns: new[] { "GuildId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_GuildId_Name",
                table: "Characters",
                columns: new[] { "GuildId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_ApiToken",
                table: "Guilds",
                column: "ApiToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_DiscordServerId",
                table: "Guilds",
                column: "DiscordServerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_InviteCode",
                table: "Guilds",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_Slug",
                table: "Guilds",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_Guilds_GuildId",
                table: "Characters",
                column: "GuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CraftOrders_Guilds_GuildId",
                table: "CraftOrders",
                column: "GuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Guilds_GuildId",
                table: "Members",
                column: "GuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_Guilds_GuildId",
                table: "Characters");

            migrationBuilder.DropForeignKey(
                name: "FK_CraftOrders_Guilds_GuildId",
                table: "CraftOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_Guilds_GuildId",
                table: "Members");

            migrationBuilder.DropTable(
                name: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Members_GuildId_DiscordId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_UploadToken",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_CraftOrders_GuildId_ClientId",
                table: "CraftOrders");

            migrationBuilder.DropIndex(
                name: "IX_Characters_GuildId_Name",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "CraftOrders");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "Characters");

            migrationBuilder.CreateTable(
                name: "Metas",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Metas", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_DiscordId",
                table: "Members",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftOrders_ClientId",
                table: "CraftOrders",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name",
                table: "Characters",
                column: "Name",
                unique: true);
        }
    }
}
