using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;

namespace EmbySubtitleMerger
{
    /// <summary>
    /// Entry point pour injecter le script dans l'UI Emby
    /// </summary>
    public class SubtitleMergerEntryPoint : IServerEntryPoint
    {
        public void Dispose()
        {
        }

        public void Run()
        {
            // Le plugin est chargé - on peut logger ici
            Console.WriteLine("[SubtitleMerger] Entry point started");
        }
    }
}
