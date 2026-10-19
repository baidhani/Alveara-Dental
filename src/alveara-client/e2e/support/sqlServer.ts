/**
 * The SQL Server instance the real-backend walkthroughs reach with sqlcmd, to seed or deliberately break something in the walkthrough's OWN database (E2E_DB_NAME).
 * ALVERA_SQL_SERVER overrides the default named instance. The walkthrough's database and every file it creates live under the one data root's Test-Data folder
 * (see scripts/Invoke-RealBackendWalkthrough.ps1).
 */
export const sqlServer: string = process.env.ALVERA_SQL_SERVER ? process.env.ALVERA_SQL_SERVER : ".\SQLEXPRESS";
