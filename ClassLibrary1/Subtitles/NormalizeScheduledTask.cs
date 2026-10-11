using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Normalisation de toutes les médiathèques comme tâche planifiée d'Emby.
    /// Elle n'a aucun déclencheur par défaut : elle ne tourne que si l'utilisateur en ajoute un
    /// (Emby, Tâches planifiées), par exemple chaque nuit ou après l'analyse de la médiathèque.
    /// Seules les conversions ASS/SSA sont faites, dans les langues de la dernière normalisation complète.
    /// </summary>
    public class NormalizeScheduledTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;

        public NormalizeScheduledTask(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public string Name => "Normaliser les sous-titres (ASS/SSA vers SRT)";

        public string Key => "SubtitleMergerNormalize";

        public string Description =>
            "Convertit en .srt les sous-titres ASS/SSA des vidéos qui n'ont pas déjà un sous-titre texte dans la même langue. "
            + "Les fichiers déjà convertis ne sont pas refaits.";

        public string Category => "Subtitle Merger";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            // Une normalisation ou un nettoyage lancé depuis la page a la priorité
            if (JobStore<NormalizeJob>.Active() != null)
            {
                Plugin.Log("[Normalize] Tâche planifiée ignorée : une autre tâche est déjà en cours");
                return;
            }

            var options = new NormalizeOptions { DryRun = false };
            var languages = Plugin.Instance?.Configuration.NormalizeLanguages ?? PluginConfiguration.DefaultNormalizeLanguages;
            foreach (var code in languages.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var language = SubtitleNaming.Resolve(code);
                if (language != null) options.Languages.Add(language.Code);
            }

            var job = JobStore<NormalizeJob>.Add(new NormalizeJob { DryRun = false });
            Plugin.Log($"[Normalize] Tâche planifiée {job.Id}, langues {string.Join(",", options.Languages)}");

            using var registration = cancellationToken.Register(() => job.Cancellation.Cancel());

            var run = SubtitleNormalizer.RunAsync(job, LibraryFolders.All(_libraryManager), options, () => _libraryManager.QueueLibraryScan());

            // Emby affiche l'avancement de la tâche pendant qu'elle tourne
            while (await Task.WhenAny(run, Task.Delay(2000)) != run)
                progress.Report(job.PercentComplete);

            await run;
            progress.Report(100);

            cancellationToken.ThrowIfCancellationRequested();
            if (job.Status == JobStatus.Failed)
                throw new InvalidOperationException(job.Error ?? "Échec de la normalisation.");
        }
    }
}
