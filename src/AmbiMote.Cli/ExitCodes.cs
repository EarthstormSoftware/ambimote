namespace AmbiMote.Cli;

/// <summary>Exit codes. Scripts depend on these, so never renumber them.</summary>
internal static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Unexpected failure, such as a serial port that could not be opened.</summary>
    public const int Error = 1;

    /// <summary>Invalid arguments.</summary>
    public const int Usage = 2;

    /// <summary>No Mote host is plugged in, or none matches <c>--port</c>.</summary>
    public const int NoDevice = 3;

    /// <summary>The config file is missing or not valid.</summary>
    public const int Config = 4;

    /// <summary>The AmbiMote app is not running, or did not answer, so 'ambimote app' has nothing to control.</summary>
    public const int AppNotRunning = 5;
}
