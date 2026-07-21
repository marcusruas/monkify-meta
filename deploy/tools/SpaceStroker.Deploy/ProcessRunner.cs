using System.Diagnostics;

namespace SpaceStroker.Deploy;

/// <summary>Runs external CLIs (gcloud, docker, terraform, gsutil, yarn, npm) and streams output.</summary>
public static class ProcessRunner
{
    public static int Run(string file, string args, string? workingDir = null)
    {
        Console.WriteLine($"  $ {file} {args}");
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start '{file}'.");
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new DeployException(
                $"'{file}' was not found on PATH. Install it (see deploy/README.md → Prerequisites) and re-run.");
        }
    }

    /// <summary>Runs and throws <see cref="DeployException"/> on a non-zero exit.</summary>
    public static void RunOrThrow(string file, string args, string? workingDir = null)
    {
        var code = Run(file, args, workingDir);
        if (code != 0)
            throw new DeployException($"'{file} {args}' exited with code {code}.");
    }
}

public sealed class DeployException(string message) : Exception(message);
