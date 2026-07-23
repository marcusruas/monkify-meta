using System.Diagnostics;

namespace SpaceStroker.Deploy;

/// <summary>Runs external CLIs (gcloud, docker, terraform, gsutil, yarn, npm) and streams output.</summary>
public static class ProcessRunner
{
    public static int Run(string file, string args, string? workingDir = null, IDictionary<string, string>? env = null)
    {
        Console.WriteLine($"  $ {file} {args}");
        using var proc = Start(file, args, workingDir, env, capture: false);
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>Runs and throws <see cref="DeployException"/> on a non-zero exit.</summary>
    public static void RunOrThrow(string file, string args, string? workingDir = null, IDictionary<string, string>? env = null)
    {
        var code = Run(file, args, workingDir, env);
        if (code != 0)
            throw new DeployException($"'{file} {args}' exited with code {code}.");
    }

    /// <summary>
    /// Runs and returns stdout. Used where the tool must READ a command's output — the state-bucket
    /// probe, `terraform output`, and the Secret Manager sweep.
    /// </summary>
    public static (int ExitCode, string Stdout) Capture(string file, string args, string? workingDir = null, IDictionary<string, string>? env = null)
    {
        Console.WriteLine($"  $ {file} {args}");
        using var proc = Start(file, args, workingDir, env, capture: true);
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout);
    }

    private static Process Start(string file, string args, string? workingDir, IDictionary<string, string>? env, bool capture)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = false,
        };

        // Per-invocation environment (TF_DATA_DIR keeps each environment's .terraform dir separate).
        if (env is not null)
        {
            foreach (var (key, value) in env)
                psi.Environment[key] = value;
        }

        try
        {
            return Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{file}'.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new DeployException(
                $"'{file}' was not found on PATH. Install it (see deploy/README.md → Prerequisites) and re-run.");
        }
    }
}

public sealed class DeployException(string message) : Exception(message);
