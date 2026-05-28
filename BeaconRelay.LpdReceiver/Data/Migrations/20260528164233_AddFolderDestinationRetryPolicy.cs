using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeaconRelay.LpdReceiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderDestinationRetryPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryPolicyId",
                table: "RuleFolderDestinations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleFolderDestinations_RetryPolicyId",
                table: "RuleFolderDestinations",
                column: "RetryPolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_RuleFolderDestinations_RetryPolicies_RetryPolicyId",
                table: "RuleFolderDestinations",
                column: "RetryPolicyId",
                principalTable: "RetryPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RuleFolderDestinations_RetryPolicies_RetryPolicyId",
                table: "RuleFolderDestinations");

            migrationBuilder.DropIndex(
                name: "IX_RuleFolderDestinations_RetryPolicyId",
                table: "RuleFolderDestinations");

            migrationBuilder.DropColumn(
                name: "RetryPolicyId",
                table: "RuleFolderDestinations");
        }
    }
}
