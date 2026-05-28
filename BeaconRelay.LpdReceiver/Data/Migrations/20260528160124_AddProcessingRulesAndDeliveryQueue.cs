using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeaconRelay.LpdReceiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingRulesAndDeliveryQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetryPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: true),
                    InitialDelaySeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BackoffMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MaxDelaySeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    JitterPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VirtualPrinters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VirtualPrinterName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPrinters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatchOperator = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    QueueMatchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    QueueMatchValue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SourceIpCidr = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VirtualPrinterId = table.Column<int>(type: "INTEGER", nullable: true),
                    StopProcessingOnMatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingRules_VirtualPrinters_VirtualPrinterId",
                        column: x => x.VirtualPrinterId,
                        principalTable: "VirtualPrinters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivedFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DestinationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LockedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LockExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryWorkItems_ProcessingRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ProcessingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryWorkItems_ReceivedFiles_ReceivedFileId",
                        column: x => x.ReceivedFileId,
                        principalTable: "ReceivedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleFolderDestinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DestinationOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    RootFolder = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SubfolderPatternType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubfolderPattern = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DuplicatePolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UniqueNameMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    UniqueNameAffix = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsQueueOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleFolderDestinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleFolderDestinations_ProcessingRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ProcessingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleForwardDestinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DestinationOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    OutboundQueueName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CompressMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PayloadMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RetryPolicyId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleForwardDestinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleForwardDestinations_ProcessingRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ProcessingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RuleForwardDestinations_RetryPolicies_RetryPolicyId",
                        column: x => x.RetryPolicyId,
                        principalTable: "RetryPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BytesSentOrCopied = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RemoteHost = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: true),
                    QueueNameUsed = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ZipCreatedPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryAttempts_DeliveryWorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "DeliveryWorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_Outcome",
                table: "DeliveryAttempts",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_StartedUtc",
                table: "DeliveryAttempts",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_WorkItemId_AttemptNumber",
                table: "DeliveryAttempts",
                columns: new[] { "WorkItemId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryWorkItems_DestinationType",
                table: "DeliveryWorkItems",
                column: "DestinationType");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryWorkItems_LockExpiresUtc",
                table: "DeliveryWorkItems",
                column: "LockExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryWorkItems_ReceivedFileId",
                table: "DeliveryWorkItems",
                column: "ReceivedFileId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryWorkItems_RuleId",
                table: "DeliveryWorkItems",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryWorkItems_Status_NextAttemptUtc_Priority",
                table: "DeliveryWorkItems",
                columns: new[] { "Status", "NextAttemptUtc", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRules_IsEnabled",
                table: "ProcessingRules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRules_Name",
                table: "ProcessingRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRules_Priority",
                table: "ProcessingRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRules_VirtualPrinterId",
                table: "ProcessingRules",
                column: "VirtualPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_RetryPolicies_IsEnabled",
                table: "RetryPolicies",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_RetryPolicies_Name",
                table: "RetryPolicies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleFolderDestinations_IsEnabled",
                table: "RuleFolderDestinations",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_RuleFolderDestinations_RuleId",
                table: "RuleFolderDestinations",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleForwardDestinations_IsEnabled",
                table: "RuleForwardDestinations",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_RuleForwardDestinations_RetryPolicyId",
                table: "RuleForwardDestinations",
                column: "RetryPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleForwardDestinations_RuleId",
                table: "RuleForwardDestinations",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPrinters_ListenPort",
                table: "VirtualPrinters",
                column: "ListenPort");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPrinters_QueueName",
                table: "VirtualPrinters",
                column: "QueueName");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPrinters_VirtualPrinterName",
                table: "VirtualPrinters",
                column: "VirtualPrinterName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryAttempts");

            migrationBuilder.DropTable(
                name: "RuleFolderDestinations");

            migrationBuilder.DropTable(
                name: "RuleForwardDestinations");

            migrationBuilder.DropTable(
                name: "DeliveryWorkItems");

            migrationBuilder.DropTable(
                name: "RetryPolicies");

            migrationBuilder.DropTable(
                name: "ProcessingRules");

            migrationBuilder.DropTable(
                name: "VirtualPrinters");
        }
    }
}
