using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSeededNotificationTemplateChannels : Migration
    {
        // NotificationChannel: Email = 1, InApp = 2.
        // HardenNotificationsModule seeded the plain-text templates (ids 0001-*) under Channel = 1 and the
        // HTML templates (ids 0002-*) under Channel = 2, i.e. swapped with respect to the enum.
        private const int Email = 1;
        private const int InApp = 2;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BuildSwapSql(plainFrom: Email, plainTo: InApp, htmlFrom: InApp, htmlTo: Email));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BuildSwapSql(plainFrom: InApp, plainTo: Email, htmlFrom: Email, htmlTo: InApp));
        }

        private static string BuildSwapSql(int plainFrom, int plainTo, int htmlFrom, int htmlTo)
        {
            const string plainIds = @"""Id"" BETWEEN '00000000-0000-0000-0001-000000000001' AND '00000000-0000-0000-0001-000000000024'";
            const string htmlIds = @"""Id"" BETWEEN '00000000-0000-0000-0002-000000000001' AND '00000000-0000-0000-0002-000000000024'";

            return $@"
-- 1. Park the plain-text seeds on a temporary channel so the swap does not collide with
--    UX_NotificationTemplates_Code_Channel_Locale_Version.
UPDATE ""NotificationTemplates""
SET ""Channel"" = -1
WHERE {plainIds} AND ""Channel"" = {plainFrom};

-- 2. Move the HTML seeds. A seed stays Active only if no other template is already Active for the
--    same (Type, Channel, Locale): UX_NotificationTemplates_Active_Type_Channel_Locale.
{BuildMoveSql(htmlIds, htmlFrom, htmlTo)}

-- 3. Move the parked plain-text seeds with the same rule.
{BuildMoveSql(plainIds, -1, plainTo)}
";
        }

        private static string BuildMoveSql(string idFilter, int fromChannel, int toChannel) => $@"
UPDATE ""NotificationTemplates"" AS t
SET ""Channel"" = {toChannel},
    ""Status"" = CASE
        WHEN t.""Status"" = 2 AND EXISTS (
            SELECT 1
            FROM ""NotificationTemplates"" AS o
            WHERE o.""Id"" <> t.""Id""
              AND o.""Type"" = t.""Type""
              AND o.""Channel"" = {toChannel}
              AND o.""Locale"" = t.""Locale""
              AND o.""Status"" = 2
              AND o.""IsActive"")
        THEN 3
        ELSE t.""Status""
    END,
    ""UpdatedAtUtc"" = NOW()
WHERE t.{idFilter} AND t.""Channel"" = {fromChannel};";
    }
}
