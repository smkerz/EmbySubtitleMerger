using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Renomme les anciens sous-titres bilingues "Film.dual.srt" de toutes les médiathèques
    /// vers le nouveau format "Film.Français + English.fr.srt".
    /// </summary>
    [Route("/EmbySubtitleMerger/RenameLegacyDual", "POST")]
    public class RenameLegacyDualRequest : IReturn<RenameLegacyDualResponse>
    {
        /// <summary>true (défaut) = simple aperçu, aucun fichier n'est touché</summary>
        public bool DryRun { get; set; } = true;
    }

    public class RenameLegacyDualItem
    {
        public string? OldPath { get; set; }
        public string? NewName { get; set; }

        public const string Renamed = "Renamed";
        public const string WouldRename = "WouldRename";
        public const string Skipped = "Skipped";
        public const string Failed = "Error";

        /// <summary>Une des constantes ci-dessus</summary>
        public string? Status { get; set; }

        public string? Detail { get; set; }
    }

    public class RenameLegacyDualResponse
    {
        public bool Success { get; set; }
        public bool DryRun { get; set; }
        public string? Error { get; set; }
        public List<string> Folders { get; set; } = new List<string>();
        public List<RenameLegacyDualItem> Items { get; set; } = new List<RenameLegacyDualItem>();
        public int Found { get; set; }
        public int Renamed { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    public class RenameService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public RenameService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Post(RenameLegacyDualRequest request)
        {
            var response = new RenameLegacyDualResponse { DryRun = request.DryRun };

            try
            {
                response.Folders = _libraryManager.GetVirtualFolders()
                    .SelectMany(f => f.Locations ?? Array.Empty<string>())
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                };

                var legacyFiles = response.Folders
                    .SelectMany(folder => Directory.EnumerateFiles(folder, "*" + SubtitleNaming.LegacyDualSuffix, options))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                response.Found = legacyFiles.Count;

                foreach (var legacyPath in legacyFiles)
                    response.Items.Add(ProcessFile(legacyPath, request.DryRun));

                response.Renamed = response.Items.Count(i => i.Status == RenameLegacyDualItem.Renamed || i.Status == RenameLegacyDualItem.WouldRename);
                response.Skipped = response.Items.Count(i => i.Status == RenameLegacyDualItem.Skipped);
                response.Errors = response.Items.Count(i => i.Status == RenameLegacyDualItem.Failed);

                // Emby doit rescanner pour voir les nouveaux noms
                if (!request.DryRun && response.Renamed > 0)
                {
                    try { _libraryManager.QueueLibraryScan(); } catch { }
                }

                Plugin.Log($"[Rename] {(request.DryRun ? "Aperçu" : "Renommage")}: {response.Found} trouvé(s), {response.Renamed} {(request.DryRun ? "à renommer" : "renommé(s)")}, {response.Skipped} ignoré(s), {response.Errors} erreur(s)");

                response.Success = true;
            }
            catch (Exception ex)
            {
                Plugin.Log($"[Rename] ERREUR: {ex}");
                response.Success = false;
                response.Error = ex.Message;
            }

            return response;
        }

        private static RenameLegacyDualItem ProcessFile(string legacyPath, bool dryRun)
        {
            var item = new RenameLegacyDualItem { OldPath = legacyPath };

            try
            {
                var newPath = SubtitleNaming.ProposeNameForLegacyDual(legacyPath, out var detail);
                item.Detail = detail;

                if (newPath == null)
                {
                    item.Status = RenameLegacyDualItem.Skipped;
                    return item;
                }

                item.NewName = Path.GetFileName(newPath);

                // On n'écrase jamais un sous-titre existant
                if (File.Exists(newPath))
                {
                    item.Status = RenameLegacyDualItem.Skipped;
                    item.Detail = "Un fichier porte déjà le nouveau nom.";
                    return item;
                }

                if (dryRun)
                {
                    item.Status = RenameLegacyDualItem.WouldRename;
                    return item;
                }

                File.Move(legacyPath, newPath);
                item.Status = RenameLegacyDualItem.Renamed;
            }
            catch (Exception ex)
            {
                item.Status = RenameLegacyDualItem.Failed;
                item.Detail = ex.Message;
            }

            return item;
        }
    }
}
