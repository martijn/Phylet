using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Phylet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddedAlbumArtFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddedCoverMimeType",
                table: "Albums",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedCoverRelativePath",
                table: "Albums",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddedCoverMimeType",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "EmbeddedCoverRelativePath",
                table: "Albums");
        }
    }
}
