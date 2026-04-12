using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using System.IO;
using System.Reflection;

namespace EmbySubtitleMerger
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Subtitle Merger Plugin";

        public override Guid Id => new Guid("12345678-1234-1234-1234-123456789012");

        public override string Description => "Plugin pour fusionner des sous-titres de deux langues différentes";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Log("Plugin initialized");
        }

        public static Plugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "subtitlemerger",
                    EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "closed_caption",
                    DisplayName = "Subtitle Merger"
                },
                new PluginPageInfo
                {
                    Name = "subtitlemerger.js",
                    EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.configPage.js"
                },
                new PluginPageInfo
                {
                    Name = "subtitlepageinjector.js",
                    EmbeddedResourcePath = "EmbySubtitleMerger.Configuration.subtitlePageInjector.js"
                }
            };
        }

        private static string LogFilePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "EmbySubtitleMerger.log");

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch { }
        }
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public string DefaultLanguage1 { get; set; } = "en";
        public string DefaultLanguage2 { get; set; } = "fr";
        public string OutputFormat { get; set; } = "srt";
    }
}
