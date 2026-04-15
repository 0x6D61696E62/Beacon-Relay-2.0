using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeaconRelay.LpdReceiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QueueName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RemoteHost = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: false),
                    LpdJobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StoredFilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    HostName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BannerClass = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BannerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SourceFileHints = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ControlFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RawControlText = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    DuplicateOfId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedFiles_LpdJobId",
                table: "ReceivedFiles",
                column: "LpdJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedFiles_ReceivedUtc",
                table: "ReceivedFiles",
                column: "ReceivedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedFiles_Sha256",
                table: "ReceivedFiles",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedFiles_Status",
                table: "ReceivedFiles",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivedFiles");
        }
    }
}
