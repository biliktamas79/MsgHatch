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
                name: "ChannelEntry",
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
                    table.PrimaryKey("PK_ChannelEntry", x => x.Channel);
                });

            migrationBuilder.CreateTable(
                name: "ChannelState",
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
                    table.PrimaryKey("PK_ChannelState", x => new { x.Channel, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "Inbox",
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
                    table.PrimaryKey("PK_Inbox", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "MessageTypeEntry",
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
                    table.PrimaryKey("PK_MessageTypeEntry", x => x.Type);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
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
                    table.PrimaryKey("PK_Outbox", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Created",
                schema: "msghatch",
                table: "Inbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Due",
                schema: "msghatch",
                table: "Inbox",
                columns: new[] { "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Flags",
                schema: "msghatch",
                table: "Inbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "UX_Inbox_Key",
                schema: "msghatch",
                table: "Inbox",
                columns: new[] { "TenantId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Created",
                schema: "msghatch",
                table: "Outbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Due",
                schema: "msghatch",
                table: "Outbox",
                columns: new[] { "Channel", "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Flags",
                schema: "msghatch",
                table: "Outbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Source",
                schema: "msghatch",
                table: "Outbox",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Target",
                schema: "msghatch",
                table: "Outbox",
                columns: new[] { "TenantId", "TargetKind", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Outbox_Id",
                schema: "msghatch",
                table: "Outbox",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelEntry",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "ChannelState",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "MessageTypeEntry",
                schema: "msghatch");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "msghatch");
        }
    }
}
