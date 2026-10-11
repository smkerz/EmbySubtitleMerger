using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// État d'une tâche de fond
    /// </summary>
    public enum JobStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Tâche de fond suivie en mémoire.
    /// Une traduction, la normalisation d'une médiathèque ou l'extraction des pistes d'un gros film
    /// prend plusieurs minutes : cela ne tient pas dans une requête HTTP, d'où ce suivi par polling.
    /// </summary>
    public abstract class BackgroundJob
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public JobStatus Status { get; set; } = JobStatus.Pending;

        public int Processed { get; set; }
        public int Total { get; set; }

        public string? Message { get; set; }
        public string? Error { get; set; }

        public DateTime? FinishedAt { get; set; }

        internal CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        public int PercentComplete => Total <= 0 ? 0 : (int)Math.Round(100.0 * Processed / Total);

        public bool IsActive => Status == JobStatus.Pending || Status == JobStatus.Running;
    }

    /// <summary>
    /// Stockage en mémoire des tâches en cours et récentes, par type de tâche
    /// </summary>
    public static class JobStore<TJob> where TJob : BackgroundJob
    {
        private static readonly ConcurrentDictionary<string, TJob> Jobs =
            new ConcurrentDictionary<string, TJob>();

        private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(1);

        public static TJob Add(TJob job)
        {
            PurgeExpired();
            Jobs[job.Id] = job;
            return job;
        }

        public static TJob? Get(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Jobs.TryGetValue(id!, out var job) ? job : null;
        }

        /// <summary>Tâche encore en cours, s'il y en a une</summary>
        public static TJob? Active()
        {
            return Jobs.Values.FirstOrDefault(j => j.IsActive);
        }

        public static bool Cancel(string? id)
        {
            var job = Get(id);
            if (job == null || !job.IsActive) return false;

            job.Cancellation.Cancel();
            return true;
        }

        /// <summary>Retire les tâches terminées depuis plus d'une heure</summary>
        private static void PurgeExpired()
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            foreach (var kvp in Jobs)
            {
                var finished = kvp.Value.FinishedAt;
                if (finished.HasValue && finished.Value < cutoff)
                    Jobs.TryRemove(kvp.Key, out _);
            }
        }
    }
}
