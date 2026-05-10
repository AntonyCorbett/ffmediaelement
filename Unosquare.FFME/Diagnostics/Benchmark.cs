using System.Threading;

namespace Unosquare.FFME.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// A simple benchmarking class.
    /// </summary>
    internal static class Benchmark
    {
        private static readonly Lock SyncLock = new();
        private static readonly Dictionary<string, List<TimeSpan>> Measures = [];

        /// <summary>
        /// Gets the identifiers.
        /// </summary>
        public static IReadOnlyList<string> Identifiers
        {
            get
            {
                lock (SyncLock)
                {
                    return Measures.Keys.ToArray();
                }
            }
        }

        /// <summary>
        /// Starts measuring with the given identifier.
        /// Usage: using (Benchmark.Start(operationName) { your code here }.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>A disposable object that when disposed, adds a benchmark result.</returns>
        public static IDisposable Start(string identifier)
        {
            return new BenchmarkUnit(identifier);
        }

        /// <summary>
        /// Outputs the benchmark statistics.
        /// </summary>
        /// <returns>A string containing human-readable statistics.</returns>
        public static string Dump()
        {
            lock (SyncLock)
            {
                var builder = new StringBuilder();
                foreach (var kvp in Measures)
                    builder.AppendLine(new BenchmarkResult(kvp.Key, kvp.Value).ToString());

                return builder.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Outputs the benchmark statistics for the given identifier.
        /// </summary>
        /// <param name="identifier">The benchmark identifier to dump.</param>
        /// <returns>A string containing human-readable statistics.</returns>
        public static string Dump(string identifier)
        {
            lock (SyncLock)
            {
                if (!Measures.TryGetValue(identifier, out List<TimeSpan> measure)) return string.Empty;
                return new BenchmarkResult(identifier, measure).ToString();
            }
        }

        /// <summary>
        /// Retrieves the results for all benchmark identifiers.
        /// </summary>
        /// <returns>The benchmark result collection.</returns>
        public static IEnumerable<BenchmarkResult> Results()
        {
            BenchmarkResult[] snapshot;

            lock (SyncLock)
            {
                snapshot = Measures
                    .Select(kvp => new BenchmarkResult(kvp.Key, kvp.Value))
                    .ToArray();
            }

            foreach (var result in snapshot)
                yield return result;
        }

        /// <summary>
        /// Retrieves the results for the given benchmark identifier.
        /// </summary>
        /// <param name="identifier">The benchmark identifier.</param>
        /// <returns>The benchmark result.</returns>
        public static BenchmarkResult Results(string identifier)
        {
            lock (SyncLock)
            {
                if (!Measures.TryGetValue(identifier, out List<TimeSpan> measure)) return null;
                return new BenchmarkResult(identifier, measure);
            }
        }

        /// <summary>
        /// Returns the number of measures available for the given identifier.
        /// Returns 0 if the identifier does not exist.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>The number of measures.</returns>
        public static int Count(string identifier)
        {
            lock (SyncLock)
            {
                if (Measures.ContainsKey(identifier) == false) return 0;
                return Measures[identifier].Count;
            }
        }

        /// <summary>
        /// Clears the measures for the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        public static void Clear(string identifier)
        {
            lock (SyncLock)
            {
                if (Measures.ContainsKey(identifier) == false) return;
                Measures[identifier].Clear();
            }
        }

        /// <summary>
        /// Clears the measures for all identifiers.
        /// </summary>
        public static void Clear()
        {
            lock (SyncLock)
            {
                foreach (var kvp in Measures)
                    kvp.Value.Clear();
            }
        }

        /// <summary>
        /// Adds the specified result to the given identifier.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="elapsed">The elapsed.</param>
        private static void Add(string identifier, TimeSpan elapsed)
        {
            lock (SyncLock)
            {
                if (Measures.ContainsKey(identifier) == false)
                {
#pragma warning disable IDE0028 // don't simplify init since we want to set the initial capacity.
                    Measures[identifier] = new List<TimeSpan>(1024 * 1024);
#pragma warning restore IDE0028
                }

                Measures[identifier].Add(elapsed);
            }
        }

        /// <summary>
        /// Represents a disposable benchmark unit.
        /// </summary>
        /// <seealso cref="IDisposable" />
        private sealed class BenchmarkUnit : IDisposable
        {
            private readonly string Identifier;
            private volatile bool IsDisposed;
            private readonly Stopwatch Stopwatch = new();

            /// <summary>
            /// Initializes a new instance of the <see cref="BenchmarkUnit" /> class.
            /// </summary>
            /// <param name="identifier">The identifier.</param>
            public BenchmarkUnit(string identifier)
            {
                Identifier = identifier;
                Stopwatch.Start();
            }

            /// <inheritdoc />
            public void Dispose() => Dispose(true);

            /// <summary>
            /// Releases unmanaged and - optionally - managed resources.
            /// </summary>
            /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
            private void Dispose(bool alsoManaged)
            {
                if (IsDisposed) return;
                IsDisposed = true;
                if (!alsoManaged) return;

                Add(Identifier, Stopwatch.Elapsed);
                Stopwatch.Stop();
            }
        }
    }
}
