using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

using MsgHatch.Data.EFCore.Migrations;

#nullable disable

namespace MsgHatch.Data.EFCore.PostgreSql.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : EntityFrameworkStoreMigration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: Schema);

            migrationBuilder.CreateTable(
                name: "ChannelEntry",
                schema: Schema,
                columns: table => new
                {
                    Channel = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: TextCollation),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, collation: TextCollation),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelEntry", x => x.Channel);
                });

            migrationBuilder.CreateTable(
                name: "ChannelState",
                schema: Schema,
                columns: table => new
                {
                    Channel = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    SinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousKind = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true, collation: TextCollation),
                    PreviousDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true, collation: TextCollation),
                    NextCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorName = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: TextCollation)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelState", x => new { x.Channel, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: Schema,
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Id = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: true, collation: TextCollation),
                    Status = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxTryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    ClaimToken = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: true, collation: TextCollation),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    ContentVersion = table.Column<int>(type: "integer", nullable: false),
                    Flags = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TraceParent = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    Content = table.Column<string>(type: "text", nullable: false, collation: TextCollation)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "MessageTypeEntry",
                schema: Schema,
                columns: table => new
                {
                    Type = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: TextCollation),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, collation: TextCollation),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisabledChannels = table.Column<string>(type: "text", nullable: false, collation: TextCollation),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTypeEntry", x => x.Type);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: Schema,
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceMessageId = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: false, collation: TextCollation),
                    MessageType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Channel = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    TargetKind = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TargetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    TraceType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    Trace = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    Tries = table.Column<string>(type: "text", nullable: false, collation: TextCollation),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Id = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: true, collation: TextCollation),
                    Status = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxTryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextTryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    ClaimToken = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: true, collation: TextCollation),
                    ClaimedDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    ContentVersion = table.Column<int>(type: "integer", nullable: false),
                    Flags = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    TraceParent = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    Content = table.Column<string>(type: "text", nullable: false, collation: TextCollation)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Created",
                schema: Schema,
                table: "Inbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Due",
                schema: Schema,
                table: "Inbox",
                columns: new[] { "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Flags",
                schema: Schema,
                table: "Inbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "UX_Inbox_Key",
                schema: Schema,
                table: "Inbox",
                columns: new[] { "TenantId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Created",
                schema: Schema,
                table: "Outbox",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Due",
                schema: Schema,
                table: "Outbox",
                columns: new[] { "Channel", "Status", "NextTryAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Flags",
                schema: Schema,
                table: "Outbox",
                column: "Flags");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Source",
                schema: Schema,
                table: "Outbox",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Target",
                schema: Schema,
                table: "Outbox",
                columns: new[] { "TenantId", "TargetKind", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Outbox_Id",
                schema: Schema,
                table: "Outbox",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelEntry",
                schema: Schema);

            migrationBuilder.DropTable(
                name: "ChannelState",
                schema: Schema);

            migrationBuilder.DropTable(
                name: "Inbox",
                schema: Schema);

            migrationBuilder.DropTable(
                name: "MessageTypeEntry",
                schema: Schema);

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: Schema);
        }
    }
}
