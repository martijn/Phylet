using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Phylet.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeviceConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceConfigurations",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigurations", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceConfigurations");
        }
    }
}
