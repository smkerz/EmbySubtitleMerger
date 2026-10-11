using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace EmbySubtitleMerger.Subtitles
{
    /// <summary>
    /// Renomme les anciens sous-titres bilingues "Film.dual.srt" de toutes les médiathèques
    /// vers le nouveau format "Film.Français + English.fr.srt".
    /// </summary>
    [Route("/EmbySubtitleMerger/RenameLegacyDual", "POST")]
    [Authenticated(Roles = "Admin")]
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

    /// <summary>
    /// Met de côté d'anciens "Film.dual.srt" que le renommage ne sait pas traiter (une seule langue,
    /// fichier illisible…). Ils sont déplacés dans un dossier "_anciens_dual" à la racine de leur
    /// médiathèque, jamais supprimés.
    /// </summary>
    [Route("/EmbySubtitleMerger/RemoveLegacyDual", "POST")]
    [Authenticated(Roles = "Admin")]
    public class RemoveLegacyDualRequest : IReturn<RemoveLegacyDualResponse>
    {
        public List<string> Paths { get; set; } = new List<string>();
    }

    public class RemoveLegacyDualResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int Moved { get; set; }

        /// <summary>Fichiers refusés ou en échec, avec la raison</summary>
        public List<RenameLegacyDualItem> Problems { get; set; } = new List<RenameLegacyDualItem>();
    }

    public class RenameService : IService
    {
        public const string RemovedFolderName = "_anciens_dual";

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
                response.Folders = LibraryFolders.All(_libraryManager);

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

        public object Post(RemoveLegacyDualRequest request)
        {
            var response = new RemoveLegacyDualResponse();

            try
            {
                var folders = LibraryFolders.All(_libraryManager);

                foreach (var path in (request.Paths ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var problem = MoveAside(path, folders);
                    if (problem == null)
                        response.Moved++;
                    else
                        response.Problems.Add(new RenameLegacyDualItem { OldPath = path, Status = RenameLegacyDualItem.Failed, Detail = problem });
                }

                if (response.Moved > 0)
                {
                    try { _libraryManager.QueueLibraryScan(); } catch { }
                }

                Plugin.Log($"[Rename] Mise de côté : {response.Moved} fichier(s), {response.Problems.Count} refus ou échec(s)");
                response.Success = true;
            }
            catch (Exception ex)
            {
                Plugin.Log($"[Rename] ERREUR mise de côté: {ex}");
                response.Error = ex.Message;
            }

            return response;
        }

        /// <summary>
        /// Déplace un .dual.srt dans le dossier _anciens_dual de sa médiathèque.
        /// Retourne null si c'est fait, sinon la raison du refus.
        /// </summary>
        internal static string? MoveAside(string path, IReadOnlyList<string> libraryFolders)
        {
            try
            {
                // La requête ne peut désigner qu'un ancien bilingue situé dans une médiathèque
                if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(SubtitleNaming.LegacyDualSuffix, StringComparison.OrdinalIgnoreCase))
                    return "Ce n'est pas un fichier .dual.srt.";

                var full = Path.GetFullPath(path);
                var library = libraryFolders
                    .Select(Path.GetFullPath)
                    .FirstOrDefault(folder => full.StartsWith(
                        folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));

                if (library == null) return "Le fichier n'est dans aucune médiathèque.";
                if (!File.Exists(full)) return "Fichier introuvable.";

                var targetFolder = Path.Combine(library, RemovedFolderName);
                Directory.CreateDirectory(targetFolder);

                // Deux films de dossiers différents peuvent porter le même nom de fichier
                var target = Path.Combine(targetFolder, Path.GetFileName(full));
                for (var n = 2; File.Exists(target); n++)
                    target = Path.Combine(targetFolder, $"{Path.GetFileNameWithoutExtension(full)} ({n}).srt");

                File.Move(full, target);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
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
