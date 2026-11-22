using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class GuildLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "Guilds",
                type: "TEXT",
                nullable: false,
                defaultValue: "fr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Locale",
                table: "Guilds");
        }
    }
}
