using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace VerificationTestsRunner;

public static class Program
{
    public static int Main(string[] args)
    {
        var filters = ParseFilters(args);
        var testAssembly = typeof(VerificationTests.SimulationVerificationTests).Assembly;
        var assemblyPath = testAssembly.Location;

        Console.WriteLine($"Verification test runner: {Path.GetFileName(assemblyPath)}");

        using var controller = new XunitFrontController(AppDomainSupport.Denied, assemblyPath);
        using var discoverySink = new TestDiscoverySink();

        controller.Find(includeSourceInformation: false, discoverySink, TestFrameworkOptions.ForDiscovery());
        discoverySink.Finished.WaitOne();

        var testCases = discoverySink.TestCases;
        var filteredCases = ApplyFilters(testCases, filters).ToList();

        if (filteredCases.Count == 0)
        {
            Console.WriteLine("No tests matched the specified filters.");
            return 1;
        }

        using var executionSink = new ConsoleExecutionSink();

        controller.RunTests(filteredCases, executionSink, TestFrameworkOptions.ForExecution());
        executionSink.Finished.WaitOne();

        Console.WriteLine($"Total: {executionSink.Total}, Failed: {executionSink.Failed}, Skipped: {executionSink.Skipped}");

        foreach (var failure in executionSink.Failures)
            Console.WriteLine($"FAILED: {failure}");

        return executionSink.Failed > 0 ? 1 : 0;
    }

    private static IReadOnlyCollection<string> ParseFilters(string[] args)
    {
        var filterArg = args.FirstOrDefault(arg => arg.StartsWith("--filter", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(filterArg))
            return Array.Empty<string>();

        var value = string.Empty;
        if (filterArg.Contains('='))
        {
            var parts = filterArg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            value = parts.Length > 1 ? parts[1] : string.Empty;
        }
        else if (args.Length > 1)
        {
            var index = Array.IndexOf(args, filterArg);
            if (index >= 0 && index < args.Length - 1)
                value = args[index + 1];
        }

        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<ITestCase> ApplyFilters(IEnumerable<ITestCase> testCases, IReadOnlyCollection<string> filters)
    {
        if (filters.Count == 0)
            return testCases;

        return testCases.Where(testCase =>
        {
            var method = testCase.TestMethod?.Method?.Name ?? string.Empty;
            var className = testCase.TestMethod?.TestClass?.Class?.Name ?? string.Empty;
            var fullyQualified = string.IsNullOrWhiteSpace(className) ? method : $"{className}.{method}";
            var displayName = testCase.DisplayName ?? fullyQualified;

            return filters.Any(filter =>
                displayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                fullyQualified.Contains(filter, StringComparison.OrdinalIgnoreCase));
        });
    }

    private sealed class ConsoleExecutionSink : IMessageSink, IDisposable
    {
        private readonly ManualResetEvent _finished = new(false);
        private readonly ConcurrentQueue<string> _failures = new();

        public WaitHandle Finished => _finished;
        public int Total { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }
        public IReadOnlyCollection<string> Failures => _failures.ToArray();

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed failed)
            {
                var name = failed.Test?.DisplayName ?? "UnknownTest";
                var failureMessage = failed.Messages?.FirstOrDefault() ?? "Failed";
                _failures.Enqueue($"{name}: {failureMessage}");
            }
            else if (message is ITestAssemblyFinished finished)
            {
                Total = finished.TestsRun;
                Failed = finished.TestsFailed;
                Skipped = finished.TestsSkipped;
                _finished.Set();
            }

            return true;
        }

        public void Dispose() => _finished.Dispose();
    }
}
