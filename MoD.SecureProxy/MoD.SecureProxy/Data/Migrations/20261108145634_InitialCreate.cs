using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MoD.SecureProxy.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RequestTimeoutMilliseconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EncryptionProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    KeySourceReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncryptionProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DestinationId = table.Column<int>(type: "int", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    ForwardingOccurred = table.Column<bool>(type: "bit", nullable: false),
                    UpstreamStatusCode = table.Column<int>(type: "int", nullable: true),
                    ProxyStatusCode = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestLogs_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DestinationId = table.Column<int>(type: "int", nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MaxRequestSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RequiresEncryption = table.Column<bool>(type: "bit", nullable: false),
                    EncryptionProfileId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Operations_EncryptionProfiles_EncryptionProfileId",
                        column: x => x.EncryptionProfileId,
                        principalTable: "EncryptionProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HeaderRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    HeaderName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeaderRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeaderRules_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParameterRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MaximumLength = table.Column<int>(type: "int", nullable: true),
                    MinimumValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaximumValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParameterRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParameterRules_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Name",
                table: "Destinations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncryptionProfiles_Name",
                table: "EncryptionProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeaderRules_OperationId_HeaderName",
                table: "HeaderRules",
                columns: new[] { "OperationId", "HeaderName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operations_DestinationId",
                table: "Operations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_EncryptionProfileId",
                table: "Operations",
                column: "EncryptionProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_OperationKey",
                table: "Operations",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParameterRules_OperationId_ParameterName",
                table: "ParameterRules",
                columns: new[] { "OperationId", "ParameterName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_CorrelationId",
                table: "RequestLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_DestinationId",
                table: "RequestLogs",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_StartedAtUtc",
                table: "RequestLogs",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeaderRules");

            migrationBuilder.DropTable(
                name: "ParameterRules");

            migrationBuilder.DropTable(
                name: "RequestLogs");

            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "Destinations");

            migrationBuilder.DropTable(
                name: "EncryptionProfiles");
        }
    }
}
