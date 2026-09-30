# Tutoriel Git et GitHub pour Skrib

Ce guide sert à publier des versions sur GitHub pour que le bouton **Vérifier** des paramètres puisse les trouver.

## 1. Installer Git

1. Téléchargez Git : https://git-scm.com/download/win
2. Installez-le (options par défaut).
3. Ouvrez un terminal et vérifiez :

```powershell
git --version
```

## 2. Créer un compte et un dépôt GitHub

1. Créez un compte : https://github.com/signup
2. Nouveau dépôt : https://github.com/new
   - Nom : `Skrib` (recommandé, c’est celui lu par l’application)
   - Visibilité : Public (l’API des releases est plus simple sans jeton)
   - Ne cochez pas « Add a README » si le dossier existe déjà

Si le dépôt n’est **pas** `gaelt/Skrib`, ouvrez `Skrib/UpdateConfig.cs` et changez `GitHubOwner` et `GitHubRepo`.

## 3. Premier envoi du projet

Dans le dossier `Documents\Projet\Skrib` :

```powershell
git init
git add .
git commit -m "Premier commit Skrib"
git branch -M main
git remote add origin https://github.com/VOTRE_COMPTE/Skrib.git
git push -u origin main
```

GitHub vous demandera de vous connecter (navigateur ou jeton personnel).

## 4. Numéro de version

Avant chaque publication :

1. Ouvrez `Skrib/Package.appxmanifest`
2. Augmentez `Identity Version` (exemple : `1.0.5.0` → `1.0.6.0`)
3. Sur GitHub, le **tag** de la release doit correspondre : `v1.0.6` ou `1.0.6.0`

L’application compare cette version à celle du manifeste / du package installé.

## 5. Publier une mise à jour (release)

### Depuis Visual Studio

1. Clic droit sur le projet **Skrib** → **Publish** / **Package and Publish**
2. Générez le package (MSIX) ou un dossier publié
3. Sur GitHub : **Releases** → **Draft a new release**
4. Tag : `v1.0.6` (créez le tag)
5. Titre et notes de version (affichées dans Skrib)
6. Joignez le fichier :
   - `.msix` / `.msixbundle` (prioritaire)
   - sinon `.exe`, `.zip`
7. **Publish release**

### En ligne de commande

```powershell
git tag v1.0.6
git push origin v1.0.6
```

Puis créez la release sur GitHub et attachez le fichier d’installation.

## 6. Tester dans Skrib

1. Lancez l’application
2. Paramètres → **Mises à jour** → **Vérifier**
3. Si le tag GitHub est plus récent que la version locale, Skrib propose le téléchargement

Sans release, ou si le dépôt n’existe pas encore, le bouton l’indique et propose ce guide.

## Rappel des fichiers utiles

| Fichier | Rôle |
|---|---|
| `Skrib/UpdateConfig.cs` | Nom du dépôt GitHub |
| `Skrib/Package.appxmanifest` | Version de l’application |
| Paramètres → Vérifier | Interroge `…/releases/latest` |
