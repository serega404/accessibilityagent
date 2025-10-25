using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AccessibilityAgent.Cli;
using Xunit;

// Отключаем параллелизацию в рамках этого набора тестов, так как используется перенаправление Console
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AccessibilityAgent.Tests.Integration;

public sealed class CommandDispatcherIntegrationTests
{
    // Запускает диспетчер команд с заданными аргументами и перехватывает stdout/stderr и код возврата.
    private static async Task<(int exitCode, string stdout, string stderr)> RunAsyncAndCapture(params string[] args)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var outWriter = new StringWriter(stdout);
        using var errWriter = new StringWriter(stderr);

        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var code = await CommandDispatcher.RunAsync(args);
            outWriter.Flush();
            errWriter.Flush();
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    // Проверяет, что запуск без аргументов печатает общую справку (usage) и возвращает код 1.
    [Fact(DisplayName = "No args prints general usage and returns 1")]
    public async Task NoArgs_PrintsUsage_Returns1()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture(Array.Empty<string>());

        Assert.Equal(1, code);
        Assert.Contains("AccessibilityAgent - host reachability checker", stdout);
        Assert.Contains("Usage:", stdout);
        Assert.Empty(stderr);
    }

    // Проверяет, что глобальная справка (команда help) печатает usage и возвращает 0.
    [Fact(DisplayName = "Global help prints usage and returns 0")]
    public async Task Help_PrintsUsage_Returns0()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture("help");

        Assert.Equal(0, code);
        Assert.Contains("AccessibilityAgent - host reachability checker", stdout);
        Assert.Contains("Commands:", stdout);
        Assert.Empty(stderr);
    }

    // Проверяет, что неизвестная команда пишет ошибку в stderr, печатает usage и возвращает 1.
    [Fact(DisplayName = "Unknown command prints error and returns 1")]
    public async Task UnknownCommand_PrintsError_Returns1()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture("unknowncmd");

        Assert.Equal(1, code);
        Assert.Contains("Unknown command", stderr);
        Assert.Contains("Usage:", stdout);
    }

    // Проверяет, что неверный URL в команде http приводит к ошибке и коду возврата 1.
    [Fact(DisplayName = "HTTP invalid URL returns 1 and prints error")]
    public async Task Http_InvalidUrl_Returns1_PrintsError()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture("http", "--url", "not_a_url");

        Assert.Equal(1, code);
        Assert.Contains("Error:", stderr);
        Assert.Contains("Invalid URL", stderr);
        Assert.Equal(string.Empty, stdout);
    }

    // Проверяет, что неверный TCP-порт (нижняя граница) приводит к ошибке и коду возврата 1.
    [Fact(DisplayName = "TCP invalid port returns 1 and prints error")]
    public async Task Tcp_InvalidPort_Returns1_PrintsError()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture("tcp", "localhost", "--port", "0");

        Assert.Equal(1, code);
        Assert.Contains("Error:", stderr);
        Assert.Contains("must be >= 1", stderr);
        Assert.Equal(string.Empty, stdout);
    }

    // Проверяет, что составная проверка без выбранных чеков в формате JSON печатает пустой массив и возвращает 0.
    [Fact(DisplayName = "Composite check with no checks returns 0 and prints empty JSON when --format json")]
    public async Task Check_NoChecksSelected_Returns0_PrintsEmptyJson()
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture(
            "check", "localhost", "--skip-ping", "--skip-dns", "--format", "json");

        Assert.Equal(0, code);
        Assert.Equal("[]", stdout.Trim());
        Assert.Equal(string.Empty, stderr);
    }

    // Проверяет, что командная справка (--help) для каждой поддерживаемой команды печатает usage и возвращает 0.
    [Theory(DisplayName = "Command-specific help prints usage and returns 0")]
    [InlineData("ping")]
    [InlineData("dns")]
    [InlineData("tcp")]
    [InlineData("udp")]
    [InlineData("http")]
    [InlineData("check")]
    public async Task CommandHelp_PrintsUsage_Returns0(string command)
    {
        var (code, stdout, stderr) = await RunAsyncAndCapture(command, "--help");

        Assert.Equal(0, code);
        Assert.Contains("Usage:", stdout);
        Assert.Equal(string.Empty, stderr);
    }
}
