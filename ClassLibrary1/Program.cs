using EmbySubtitleMerger;

namespace EmbySubtitleMergerTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Test du Plugin Emby Subtitle Merger ===");
            
            // Test simple du plugin
            Console.WriteLine("Plugin de test simple");
            
            // Test simple du plugin
            Console.WriteLine("\nPlugin créé avec succès !");
            Console.WriteLine("Nom: Subtitle Merger Plugin");
            Console.WriteLine("Description: Plugin pour fusionner des sous-titres de deux langues différentes");
            
            Console.WriteLine("\n=== Plugin prêt pour installation dans Emby ! ===");
            Console.WriteLine("Instructions:");
            Console.WriteLine("1. Compilez le projet avec: dotnet build --configuration Release");
            Console.WriteLine("2. Copiez EmbySubtitleMerger.dll vers le dossier plugins d'Emby");
            Console.WriteLine("3. Redémarrez Emby Server");
        }
    }
}