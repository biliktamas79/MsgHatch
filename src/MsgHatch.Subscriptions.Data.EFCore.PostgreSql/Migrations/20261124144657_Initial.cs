using System;
using Microsoft.EntityFrameworkCore.Migrations;

using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.Migrations;

#nullable disable

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Migrations
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
                name: "MsgHatchSubscription",
                schema: Schema,
                columns: table => new
                {
                    Id = table.Column<string>(type: "character(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: false, collation: TextCollation),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Level = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    CompanyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    MessageType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Kind = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    Channel = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    SettingsType = table.Column<string>(type: "character varying(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    SettingsVersion = table.Column<int>(type: "integer", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    Recipients = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    NamedUsers = table.Column<string>(type: "text", nullable: true, collation: TextCollation),
                    Mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchSubscription", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_MsgHatchSubscription_Owner",
                schema: Schema,
                table: "MsgHatchSubscription",
                columns: new[] { "TenantId", "MessageType", "Level", "CompanyId", "UserId", "Channel", "Recipients" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MsgHatchSubscription",
                schema: Schema);
        }
    }
}
