using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeaconRelay.LpdReceiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSettingsAndListenerAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MonitorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitorUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MonitorIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailSmtpHost = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailSmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailUseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailUsername = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailPassword = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EmailFrom = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailTo = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ListenerDownEmailCooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertSettings");
        }
    }
}
