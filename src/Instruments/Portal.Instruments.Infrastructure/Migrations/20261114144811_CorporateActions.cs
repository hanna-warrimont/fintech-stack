using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Instruments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorporateActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporate_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    new_isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    split_new = table.Column<int>(type: "integer", nullable: true),
                    split_old = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corporate_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_corporate_actions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_corporate_actions_instrument_id",
                table: "corporate_actions",
                column: "instrument_id");

            // Höchstens eine OFFENE Maßnahme gleicher Art pro Instrument und Stichtag (UTC-Tag). EF kann keine
            // Ausdrucks-Indizes; die Datenbank entscheidet so auch den Wettlauf zweier gleichzeitiger Erfassungen.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_corporate_actions_open_per_day ON corporate_actions
                  (instrument_id, type, ((effective_at AT TIME ZONE 'UTC')::date))
                  WHERE status IN ('Draft','Submitted','Approved');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_corporate_actions_open_per_day;");
            migrationBuilder.DropTable(
                name: "corporate_actions");
        }
    }
}
