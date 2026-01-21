# Guide d'intégration Emby Server

## 🎯 Objectif
Ce guide vous explique comment intégrer le plugin "Hello World" dans votre serveur Emby.

## 📋 Prérequis
- Emby Server installé et fonctionnel
- Le fichier `EmbyHelloWorld.dll` compilé (disponible dans `bin/Release/net8.0/`)

## 🚀 Installation

### Étape 1 : Localiser le dossier des plugins Emby

**Windows :**
```
%ProgramData%\Emby-Server\plugins\
```
ou
```
C:\ProgramData\Emby-Server\plugins\
```

**Linux :**
```
/var/lib/emby/plugins/
```

**Docker :**
```
/config/plugins/
```

### Étape 2 : Copier le plugin

1. Copiez le fichier `EmbyHelloWorld.dll` dans le dossier des plugins Emby
2. Assurez-vous que le fichier a les bonnes permissions (lecture pour Emby)

### Étape 3 : Redémarrer Emby Server

**Windows :**
- Ouvrez les Services Windows
- Trouvez "Emby Server"
- Clic droit → Redémarrer

**Linux :**
```bash
sudo systemctl restart emby-server
```

**Docker :**
```bash
docker restart emby-server
```

## 🔍 Vérification

1. Ouvrez l'interface web d'Emby
2. Allez dans **Paramètres** → **Plugins**
3. Vous devriez voir "Hello World Plugin" dans la liste
4. Cliquez sur **Configuration** pour voir la page "Hello World"

## 🐛 Dépannage

### Le plugin n'apparaît pas
- Vérifiez que le fichier DLL est dans le bon dossier
- Vérifiez les permissions du fichier
- Consultez les logs Emby : **Paramètres** → **Logs**

### Erreur de chargement
- Vérifiez que vous utilisez la bonne version de .NET
- Assurez-vous que le serveur Emby est compatible avec .NET 8.0

### Logs utiles
Les logs Emby se trouvent dans :
- **Interface web :** Paramètres → Logs
- **Fichiers système :** Voir la documentation Emby pour l'emplacement

## 🔧 Développement avancé

### Pour créer un vrai plugin Emby

1. **Ajouter les références Emby :**
   ```xml
   <PackageReference Include="Emby.Server.Core" Version="4.8.0.80" />
   <PackageReference Include="Emby.Server.Implementations" Version="4.8.0.80" />
   ```

2. **Hériter des classes Emby :**
   ```csharp
   public class HelloWorldPlugin : BasePlugin, IHasWebPages
   ```

3. **Utiliser l'injection de dépendances Emby :**
   ```csharp
   public HelloWorldPlugin(IApplicationHost applicationHost, ILogger<HelloWorldPlugin> logger)
   ```

### Sources des packages Emby
Les packages Emby ne sont pas disponibles sur nuget.org. Vous devez :
- Télécharger les DLLs depuis le serveur Emby
- Utiliser les références d'assembly directes
- Ou contacter l'équipe Emby pour l'accès aux packages

## 📚 Ressources

- [Documentation officielle Emby](https://emby.media/support/articles/Plugins.html)
- [Forum de développement Emby](https://emby.media/community/index.php?/forum/99-developers/)
- [GitHub Emby](https://github.com/MediaBrowser/Emby)

## ✅ Test réussi

Si vous voyez la page "Hello World" dans l'interface Emby, félicitations ! Votre plugin fonctionne correctement.

Vous pouvez maintenant étendre ce plugin pour ajouter vos propres fonctionnalités.
