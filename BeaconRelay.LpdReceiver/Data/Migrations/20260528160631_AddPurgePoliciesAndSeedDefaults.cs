using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeaconRelay.LpdReceiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurgePoliciesAndSeedDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO RetryPolicies (Name, MaxAttempts, InitialDelaySeconds, BackoffMode, MaxDelaySeconds, JitterPercent, IsEnabled, CreatedUtc, UpdatedUtc)
SELECT 'DefaultDeliveryRetry', NULL, 30, 'Exponential', 3600, 10, 1, '2026-05-28T00:00:00Z', '2026-05-28T00:00:00Z'
WHERE NOT EXISTS (SELECT 1 FROM RetryPolicies WHERE Name = 'DefaultDeliveryRetry');
");

            migrationBuilder.CreateTable(
                name: "PurgePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApplyTo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    TerminalStatusesCsv = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgePolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurgePolicies_IsEnabled",
                table: "PurgePolicies",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_PurgePolicies_Name",
                table: "PurgePolicies",
                column: "Name",
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO PurgePolicies (Name, IsEnabled, ApplyTo, RetentionDays, TerminalStatusesCsv, IntervalMinutes, CreatedUtc, UpdatedUtc)
SELECT 'DefaultDeliveryAttemptsPurge', 1, 'DeliveryAttempts', 30, NULL, 60, '2026-05-28T00:00:00Z', '2026-05-28T00:00:00Z'
WHERE NOT EXISTS (SELECT 1 FROM PurgePolicies WHERE Name = 'DefaultDeliveryAttemptsPurge');
");

            migrationBuilder.Sql(@"
INSERT INTO PurgePolicies (Name, IsEnabled, ApplyTo, RetentionDays, TerminalStatusesCsv, IntervalMinutes, CreatedUtc, UpdatedUtc)
SELECT 'DefaultTerminalWorkItemPurge', 1, 'DeliveryWorkItemsTerminal', 14, 'Succeeded,Failed,Canceled', 60, '2026-05-28T00:00:00Z', '2026-05-28T00:00:00Z'
WHERE NOT EXISTS (SELECT 1 FROM PurgePolicies WHERE Name = 'DefaultTerminalWorkItemPurge');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM RetryPolicies WHERE Name = 'DefaultDeliveryRetry';");
            migrationBuilder.DropTable(
                name: "PurgePolicies");
        }
    }
}
