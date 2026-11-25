using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

using MsgHatch.Data.EFCore.Migrations;

#nullable disable

namespace MsgHatch.Data.EFCore.MySql.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : EntityFrameworkStoreMigration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MsgHatchChannelEntry",
                columns: table => new
                {
                    Channel = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: TextCollation),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: TextCollation),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchChannelEntry", x => x.Channel);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MsgHatchChannelState",
                columns: table => new
                {
                    Channel = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    SinceUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PreviousKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true, collation: TextCollation),
                    PreviousDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true, collation: TextCollation),
                    NextCheckAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorCode = table.Column<int>(type: "int", nullable: true),
                    ErrorName = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: TextCollation)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchChannelState", x => new { x.Channel, x.Version });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MsgHatchInbox",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Id = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true, collation: TextCollation),
                    Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TryCount = table.Column<int>(type: "int", nullable: false),
                    MaxTryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "longtext", nullable: true, collation: TextCollation),
                    ClaimToken = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true, collation: TextCollation),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    ContentVersion = table.Column<int>(type: "int", nullable: false),
                    Flags = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TraceParent = table.Column<string>(type: "longtext", nullable: true, collation: TextCollation),
                    Content = table.Column<string>(type: JsonColumnType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchInbox", x => x.Sequence);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MsgHatchMessageTypeEntry",
                columns: table => new
                {
                    Type = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: TextCollation),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true, collation: TextCollation),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DisabledChannels = table.Column<string>(type: JsonColumnType, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchMessageTypeEntry", x => x.Type);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MsgHatchOutbox",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceMessageId = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false, collation: TextCollation),
                    MessageType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Channel = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    TargetKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TargetId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    TraceType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    Trace = table.Column<string>(type: JsonColumnType, nullable: true),
                    Tries = table.Column<string>(type: JsonColumnType, nullable: false),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Id = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true, collation: TextCollation),
                    Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TryCount = table.Column<int>(type: "int", nullable: false),
                    MaxTryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "longtext", nullable: true, collation: TextCollation),
                    ClaimToken = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true, collation: TextCollation),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    ContentVersion = table.Column<int>(type: "int", nullable: false),
                    Flags = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TraceParent = table.Column<string>(type: "longtext", nullable: true, collation: TextCollation),
                    Content = table.Column<string>(type: JsonColumnType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchOutbox", x => x.Sequence);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Created",
                table: "MsgHatchInbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Due",
                table: "MsgHatchInbox",
                columns: new[] { "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Flags",
                table: "MsgHatchInbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "UX_Inbox_Key",
                table: "MsgHatchInbox",
                columns: new[] { "TenantId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Created",
                table: "MsgHatchOutbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Due",
                table: "MsgHatchOutbox",
                columns: new[] { "Channel", "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Flags",
                table: "MsgHatchOutbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Source",
                table: "MsgHatchOutbox",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Target",
                table: "MsgHatchOutbox",
                columns: new[] { "TenantId", "TargetKind", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Outbox_Id",
                table: "MsgHatchOutbox",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MsgHatchChannelEntry");

            migrationBuilder.DropTable(
                name: "MsgHatchChannelState");

            migrationBuilder.DropTable(
                name: "MsgHatchInbox");

            migrationBuilder.DropTable(
                name: "MsgHatchMessageTypeEntry");

            migrationBuilder.DropTable(
                name: "MsgHatchOutbox");
        }
    }
}
