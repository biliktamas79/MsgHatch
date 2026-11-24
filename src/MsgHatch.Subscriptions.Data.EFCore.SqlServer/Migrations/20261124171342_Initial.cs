using System;
using Microsoft.EntityFrameworkCore.Migrations;

using MsgHatch.Data.EFCore.Migrations;

#nullable disable

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Migrations
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
                name: "Subscription",
                schema: Schema,
                columns: table => new
                {
                    Id = table.Column<string>(type: "char(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: false, collation: TextCollation),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Level = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    CompanyId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    MessageType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    Channel = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    SettingsType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    SettingsVersion = table.Column<int>(type: "int", nullable: true),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: TextCollation),
                    Recipients = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    NamedUsers = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: TextCollation),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Subscription_Owner",
                schema: Schema,
                table: "Subscription",
                columns: new[] { "TenantId", "MessageType", "Level", "CompanyId", "UserId", "Channel", "Recipients" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscription",
                schema: Schema);
        }
    }
}
