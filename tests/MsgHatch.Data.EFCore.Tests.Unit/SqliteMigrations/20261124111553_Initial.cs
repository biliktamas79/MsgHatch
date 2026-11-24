using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsgHatch.Data.EFCore.Tests.Unit.SqliteMigrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "msghatch");

            migrationBuilder.CreateTable(
                name: "MsgHatchChannelEntry",
                schema: "msghatch",
                columns: table => new
                {
                    Channel = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchChannelEntry", x => x.Channel);
                });

            migrationBuilder.CreateTable(
                name: "MsgHatchChannelState",
                schema: "msghatch",
                columns: table => new
                {
                    Channel = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    SinceUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousKind = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: true),
                    PreviousDurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: true),
                    NextCheckAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorName = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchChannelState", x => new { x.Channel, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "MsgHatchInbox",
                schema: "msghatch",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Id = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 36, nullable: true),
                    Status = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    TryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxTryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 36, nullable: true),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    ContentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Flags = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    TraceParent = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchInbox", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "MsgHatchMessageTypeEntry",
                schema: "msghatch",
                columns: table => new
                {
                    Type = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisabledChannels = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchMessageTypeEntry", x => x.Type);
                });

            migrationBuilder.CreateTable(
                name: "MsgHatchOutbox",
                schema: "msghatch",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceMessageId = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 36, nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TraceType = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: true),
                    Trace = table.Column<string>(type: "TEXT", nullable: true),
                    Tries = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Id = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 36, nullable: true),
                    Status = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    TryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxTryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 36, nullable: true),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", unicode: false, maxLength: 256, nullable: false),
                    ContentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Flags = table.Column<string>(type: "TEXT", unicode: false, maxLength: 32, nullable: false),
                    TraceParent = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchOutbox", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchInbox_Created",
                schema: "msghatch",
                table: "MsgHatchInbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchInbox_Due",
                schema: "msghatch",
                table: "MsgHatchInbox",
                columns: new[] { "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchInbox_Flags",
                schema: "msghatch",
                table: "MsgHatchInbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "UX_MsgHatchInbox_Key",
                schema: "msghatch",
                table: "MsgHatchInbox",
                columns: new[] { "TenantId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchOutbox_Created",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchOutbox_Due",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                columns: new[] { "Channel", "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchOutbox_Flags",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchOutbox_Source",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MsgHatchOutbox_Target",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                columns: new[] { "TenantId", "TargetKind", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_MsgHatchOutbox_Id",
                schema: "msghatch",
                table: "MsgHatchOutbox",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MsgHatchChannelEntry",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "MsgHatchChannelState",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "MsgHatchInbox",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "MsgHatchMessageTypeEntry",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "MsgHatchOutbox",
                schema: "msghatch");
        }
    }
}
