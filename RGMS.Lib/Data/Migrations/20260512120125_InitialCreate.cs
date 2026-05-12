using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RGMS.Lib.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneralSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SampleRateHz = table.Column<double>(type: "REAL", nullable: false),
                    SamplesPerChannelPerCallback = table.Column<int>(type: "INTEGER", nullable: false),
                    GateOnPhaseDeg = table.Column<double>(type: "REAL", nullable: false),
                    GateOffPhaseDeg = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DaqChannelSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneralSettingsId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PhysicalChannel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Terminal = table.Column<int>(type: "INTEGER", nullable: false),
                    MinVolts = table.Column<double>(type: "REAL", nullable: false),
                    MaxVolts = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaqChannelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaqChannelSettings_GeneralSettings_GeneralSettingsId",
                        column: x => x.GeneralSettingsId,
                        principalTable: "GeneralSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DaqChannelSettings_GeneralSettingsId_ChannelIndex",
                table: "DaqChannelSettings",
                columns: new[] { "GeneralSettingsId", "ChannelIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DaqChannelSettings");

            migrationBuilder.DropTable(
                name: "GeneralSettings");
        }
    }
}
