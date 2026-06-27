// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Security.Cryptography;

namespace Ryx.Sidekick.AgentHost;

internal static class Program
{
    public static int Main(string[] args)
    {
        var options = DaemonOptions.Parse(args);

        // Clean, dependency-free path used as the bundled-runtime smoke test:
        // `dotnet SidekickAgentHost.dll --help` (or no args) prints version & exits 0.
        if (options.ShowHelp || options.ShowVersion || args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        // Token: read from token-file if present, else generate one (and the
        // server will write it out at Start).
        var token = ResolveToken(options);

        var server = new AgentHostServer(options, token);

        // Stop cleanly on Ctrl+C / SIGTERM.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => server.Stop();

        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SidekickAgentHost] failed to start: {ex.Message}");
            return 1;
        }

        Console.Out.WriteLine($"[SidekickAgentHost] v{DaemonInfo.Version} proto={DaemonInfo.Protocol} listening on 127.0.0.1:{server.Port}");
        Console.Out.Flush();

        // Block until the daemon stops (grace expiry / owner death / SHUTDOWN).
        server.Stopped.GetAwaiter().GetResult();
        return 0;
    }

    private static string ResolveToken(DaemonOptions options)
    {
        if (!string.IsNullOrEmpty(options.TokenFile) && System.IO.File.Exists(options.TokenFile))
        {
            try
            {
                var existing = DiscoveryPaths.ReadToken(options.TokenFile!);
                if (!string.IsNullOrEmpty(existing))
                    return existing;
            }
            catch { /* fall through to generate */ }
        }

        return GenerateToken();
    }

    private static string GenerateToken()
    {
        // 256 bits of URL-safe randomness.
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static void PrintUsage()
    {
        Console.Out.WriteLine($"SidekickAgentHost v{DaemonInfo.Version} (protocol {DaemonInfo.Protocol})");
        Console.Out.WriteLine("Generic, protocol-blind subprocess multiplexer for Ryx Sidekick.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage:");
        Console.Out.WriteLine("  SidekickAgentHost --port-file <path> --token-file <path> --pid-file <path> \\");
        Console.Out.WriteLine("                    --grace-seconds <N> --owner-pid <pid> --project-hash <hash>");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Options:");
        Console.Out.WriteLine("  --port-file <path>     Write the bound loopback port here (after bind).");
        Console.Out.WriteLine("  --token-file <path>    Auth token file (generated if absent).");
        Console.Out.WriteLine("  --pid-file <path>      Write this process's pid here.");
        Console.Out.WriteLine("  --grace-seconds <N>    Seconds to keep children alive after last client (default 30).");
        Console.Out.WriteLine("  --owner-pid <pid>      Editor pid to watch; exit if gone after grace.");
        Console.Out.WriteLine("  --project-hash <hash>  Per-project discovery dir key (derives default file paths).");
        Console.Out.WriteLine("  --help, --version      Print this and exit 0.");
    }
}