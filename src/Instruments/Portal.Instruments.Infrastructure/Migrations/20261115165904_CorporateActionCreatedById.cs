using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Instruments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorporateActionCreatedById : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bewusst OHNE defaultValue (EF hätte "" eingetragen): Bestehende Maßnahmen hätten sonst eine leere Kennung,
            // und die Vier-Augen-Regel ließe dort jeden freigeben – auch die Person, die erfasst hat. So scheitert die
            // Migration auf einer Tabelle mit Daten laut ("column contains null values"), statt die Regel still abzuschwächen.
            // Bisher gibt es keine solchen Daten: lokal ist die DB nach jedem AppHost-Start leer, in Azure nie deployt.
            migrationBuilder.AddColumn<string>(
                name: "created_by_id",
                table: "corporate_actions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_id",
                table: "corporate_actions");
        }
    }
}
