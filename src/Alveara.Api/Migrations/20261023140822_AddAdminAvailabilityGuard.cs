using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAvailabilityGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ALV-N017: at least one enabled administrator (Role = 6 is Admin) must remain after any UPDATE or DELETE of an account, whoever runs it (application, raw SQL, bulk update).
            // The statement is only examined when it removes an available administrator; it then takes the same application lock as the application guard and counts the latest committed state.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_UserAccounts_LastAdmin] ON [UserAccounts] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Role] = 6 AND d.[IsDisabled] = 0
                   AND NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[Id] = d.[Id] AND i.[Role] = 6 AND i.[IsDisabled] = 0))
        RETURN;
    DECLARE @r int;
    EXEC @r = sp_getapplock @Resource = N'alveara.admin-availability', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
    IF @r < 0
        THROW 51208, 'The administrator guard is busy. Try again.', 1;
    IF NOT EXISTS (SELECT 1 FROM [UserAccounts] WITH (READCOMMITTEDLOCK) WHERE [Role] = 6 AND [IsDisabled] = 0)
        THROW 51207, 'At least one enabled administrator must remain.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_UserAccounts_LastAdmin]");
        }
    }
}
