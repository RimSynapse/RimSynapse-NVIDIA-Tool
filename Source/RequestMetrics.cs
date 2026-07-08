using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Tracks LLM request metrics by reading Core's public API.
    /// Maintains a rolling window of recent request stats for
    /// throughput and capacity estimation.
    /// </summary>
    internal static class RequestMetrics
    {
        // Rolling window of request durations
        private static readonly List<RequestSnapshot> _recentRequests = new List<RequestSnapshot>();
        private static readonly object _lock = new object();
        private const int MaxSnapshots = 50;

        // ── Session counters ──
        internal static int TotalRequests { get; private set; }
        internal static int FailedRequests { get; private set; }
        internal static long TotalPromptTokens { get; private set; }
        internal static long TotalCompletionTokens { get; private set; }

        /// <summary>
        /// Record a completed request for metrics tracking.
        /// Called from the DevToolsMod's request interceptor.
        /// </summary>
        internal static void RecordRequest(ChatResult result, string modId)
        {
            TotalRequests++;

            if (!result.success)
            {
                FailedRequests++;
                return;
            }

            TotalPromptTokens += result.promptTokens;
            TotalCompletionTokens += result.completionTokens;

            lock (_lock)
            {
                _recentRequests.Add(new RequestSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    PromptTokens = result.promptTokens,
                    CompletionTokens = result.completionTokens,
                    DurationMs = result.durationMs,
                    ModId = modId,
                    WasThrottled = result.wasThrottled,
                });

                // Trim to max window
                while (_recentRequests.Count > MaxSnapshots)
                    _recentRequests.RemoveAt(0);
            }
        }

        // ── Computed metrics ──

        /// <summary>Average prompt tokens across recent requests.</summary>
        internal static float AvgPromptTokens
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count == 0) return 0f;
                    return (float)_recentRequests.Average(r => r.PromptTokens);
                }
            }
        }

        /// <summary>Average completion tokens across recent requests.</summary>
        internal static float AvgCompletionTokens
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count == 0) return 0f;
                    return (float)_recentRequests.Average(r => r.CompletionTokens);
                }
            }
        }

        /// <summary>Average response duration in ms across recent requests.</summary>
        internal static float AvgDurationMs
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count == 0) return 0f;
                    return (float)_recentRequests.Average(r => r.DurationMs);
                }
            }
        }

        /// <summary>Requests per minute based on the last 60 seconds of data.</summary>
        internal static float RequestsPerMinute
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count < 2) return 0f;

                    var cutoff = DateTime.UtcNow.AddSeconds(-60);
                    int count = _recentRequests.Count(r => r.Timestamp > cutoff);
                    return count; // Already per-minute since window is 60s
                }
            }
        }

        /// <summary>Percentage of recent requests that were throttled.</summary>
        internal static float ThrottledPercent
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count == 0) return 0f;
                    return _recentRequests.Count(r => r.WasThrottled) /
                           (float)_recentRequests.Count * 100f;
                }
            }
        }

        /// <summary>
        /// Estimate max concurrent pawns based on avg tokens and context window.
        /// Formula from DESIGN_DEVTOOLS.md:
        ///   tokensPerPawn = max(100, avgPromptTokens / activePawnCount)
        ///   maxPawnsByContext = contextTarget / tokensPerPawn
        /// </summary>
        internal static int EstimateMaxPawns(int contextWindow, int activePawnCount)
        {
            if (contextWindow <= 0) return 0;

            float avgTokens = AvgPromptTokens;
            if (avgTokens < 1f) avgTokens = 200f; // Default estimate

            float tokensPerPawn = Math.Max(100f,
                activePawnCount > 0 ? avgTokens / activePawnCount : avgTokens);

            // Reserve 25% for completion
            float contextTarget = contextWindow * 0.75f;

            return Math.Max(1, (int)(contextTarget / tokensPerPawn));
        }

        /// <summary>
        /// Get tokens per second throughput based on recent requests.
        /// </summary>
        internal static float TokensPerSecond
        {
            get
            {
                lock (_lock)
                {
                    if (_recentRequests.Count == 0) return 0f;

                    float totalTokens = _recentRequests.Sum(r => r.CompletionTokens);
                    float totalSeconds = _recentRequests.Sum(r => r.DurationMs) / 1000f;

                    return totalSeconds > 0 ? totalTokens / totalSeconds : 0f;
                }
            }
        }

        internal static void Reset()
        {
            lock (_lock)
            {
                _recentRequests.Clear();
            }
            TotalRequests = 0;
            FailedRequests = 0;
            TotalPromptTokens = 0;
            TotalCompletionTokens = 0;
        }
    }

    internal class RequestSnapshot
    {
        public DateTime Timestamp;
        public int PromptTokens;
        public int CompletionTokens;
        public long DurationMs;
        public string ModId;
        public bool WasThrottled;
    }
}
