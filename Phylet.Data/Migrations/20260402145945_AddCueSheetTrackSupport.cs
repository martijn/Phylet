using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Phylet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCueSheetTrackSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CueSegmentDurationMs",
                table: "Tracks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CueSegmentStartMs",
                table: "Tracks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CueSheetRelativePath",
                table: "Tracks",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceKind",
                table: "Tracks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceRelativePath",
                table: "Tracks",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE Tracks SET SourceRelativePath = RelativePath WHERE SourceRelativePath = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CueSegmentDurationMs",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CueSegmentStartMs",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CueSheetRelativePath",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SourceRelativePath",
                table: "Tracks");
        }
    }
}
