using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoD.SecureProxy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowUnknownParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowUnknownParameters",
                table: "Operations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowUnknownParameters",
                table: "Operations");
        }
    }
}
