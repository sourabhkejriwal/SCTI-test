using NUnit.Framework;

// Test fixtures (each Reqnroll feature is one fixture) run in parallel; the worker count lives in test.runsettings.
// UI scenarios are parallel-safe: each one gets its own browser and browser context, and all state is scenario-scoped.
[assembly: Parallelizable(ParallelScope.Fixtures)]
