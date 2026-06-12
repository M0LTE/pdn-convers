using System.Globalization;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// Where the conversd-saupp oracle (<c>docker/compose.oracle.yml</c>) listens for the interop lane. The
/// HostLink dials it as a HOST peer (the oracle's <c>Access … HOST</c> allows it). Overridable for a
/// private oracle copy via environment variables (the pdn-bbs OracleFixture precedent).
/// </summary>
internal static class OracleEndpoint
{
    /// <summary>Oracle host — <c>CONVERS_ORACLE_HOST</c> or 127.0.0.1.</summary>
    public static string Host =>
        Environment.GetEnvironmentVariable("CONVERS_ORACLE_HOST") is { Length: > 0 } h ? h : "127.0.0.1";

    /// <summary>Oracle convers port — <c>CONVERS_ORACLE_PORT</c> or 3600.</summary>
    public static int Port =>
        int.TryParse(
            Environment.GetEnvironmentVariable("CONVERS_ORACLE_PORT"),
            NumberStyles.None, CultureInfo.InvariantCulture, out int p)
            ? p
            : 3600;
}
