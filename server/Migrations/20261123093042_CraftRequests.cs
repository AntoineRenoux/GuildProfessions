using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class CraftRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CrafterCharacter",
                table: "CraftOrders",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresUtc",
                table: "CraftOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipeId",
                table: "CraftOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<int>(type: "INTEGER", nullable: false),
                    SpellId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Profession = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogRecipes_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Crafter = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Withdrawn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftOffers_CraftOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "CraftOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRecipes_GuildId_SpellId",
                table: "CatalogRecipes",
                columns: new[] { "GuildId", "SpellId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftOffers_OrderId_Crafter",
                table: "CraftOffers",
                columns: new[] { "OrderId", "Crafter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogRecipes");

            migrationBuilder.DropTable(
                name: "CraftOffers");

            migrationBuilder.DropColumn(
                name: "ExpiresUtc",
                table: "CraftOrders");

            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "CraftOrders");

            migrationBuilder.AlterColumn<string>(
                name: "CrafterCharacter",
                table: "CraftOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
