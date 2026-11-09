using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoD.SecureProxy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationChanges_EntityType_EntityId_ChangedAtUtc",
                table: "ConfigurationChanges",
                columns: new[] { "EntityType", "EntityId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationChanges");
        }
    }
}
