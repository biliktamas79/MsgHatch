using System;
using Microsoft.EntityFrameworkCore.Migrations;

using MsgHatch.Data.EFCore.Migrations;

#nullable disable

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : EntityFrameworkStoreMigration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MsgHatchSubscription",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false, collation: TextCollation),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    Level = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    CompanyId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    UserId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: TextCollation),
                    MessageType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    Channel = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: TextCollation),
                    SettingsType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true, collation: TextCollation),
                    SettingsVersion = table.Column<int>(type: "int", nullable: true),
                    Settings = table.Column<string>(type: JsonColumnType, nullable: true),
                    Recipients = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: TextCollation),
                    OwnerKey = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    NamedUsers = table.Column<string>(type: JsonColumnType, nullable: true),
                    Mandatory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MsgHatchSubscription", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_Type",
                table: "MsgHatchSubscription",
                columns: new[] { "TenantId", "MessageType" });

            migrationBuilder.CreateIndex(
                name: "UX_Subscription_Owner",
                table: "MsgHatchSubscription",
                column: "OwnerKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MsgHatchSubscription");
        }
    }
}
