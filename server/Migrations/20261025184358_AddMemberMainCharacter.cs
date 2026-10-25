using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuildProfessions.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberMainCharacter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MainCharacterId",
                table: "Members",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_MainCharacterId",
                table: "Members",
                column: "MainCharacterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Characters_MainCharacterId",
                table: "Members",
                column: "MainCharacterId",
                principalTable: "Characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Membres déjà liés avant cette version : leur premier personnage lié devient le principal.
            migrationBuilder.Sql(
                "UPDATE Members SET MainCharacterId = " +
                "(SELECT MIN(c.Id) FROM Characters c WHERE c.MemberId = Members.Id) " +
                "WHERE MainCharacterId IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Members_Characters_MainCharacterId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_MainCharacterId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MainCharacterId",
                table: "Members");
        }
    }
}
