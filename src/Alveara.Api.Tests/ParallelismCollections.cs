using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// TESTSPEED-P2: test classes run in parallel (xunit.runner.json, 4 workers, conservative algorithm) EXCEPT the classes in this collection, which never overlap
/// with any other test class. Membership is the reviewed Gate 1 isolation inventory (docs/testing/TESTSPEED_P2_INVENTORY.md): the process-wide PBKDF2 verify counter
/// (AccountServiceLoginTests), the fixed port and real process (RealProcessDatabaseOutageTests), and everything that administers the LocalDB server (logins, BACKUP and
/// RESTORE, migrations, drops through master) or spawns child processes or shares file roots. A class is added here when it touches process- or server-wide state;
/// it is removed only with a recorded demonstration that concurrent execution is safe.
/// </summary>
[CollectionDefinition(ParallelismCollections.SerialServer, DisableParallelization = true)]
public sealed class SerialServerCollection
{
}

public static class ParallelismCollections
{
    public const string SerialServer = "serial-server";
}
