using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Phylet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryScanStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastScanUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryScanStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AlbumPathKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CoverRelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlbumId = table.Column<int>(type: "INTEGER", nullable: true),
                    FolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TrackArtistName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tracks_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tracks_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtistId_NormalizedTitle_AlbumPathKey",
                table: "Albums",
                columns: new[] { "ArtistId", "NormalizedTitle", "AlbumPathKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_NormalizedName",
                table: "Artists",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentFolderId",
                table: "Folders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_RelativePath",
                table: "Folders",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId",
                table: "Tracks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_FolderId",
                table: "Tracks",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_RelativePath",
                table: "Tracks",
                column: "RelativePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryScanStates");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropTable(
                name: "Artists");
        }
    }
}
