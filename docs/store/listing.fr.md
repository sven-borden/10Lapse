# 10Lapse 3.1 Store listing (French)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #42, #115): Apps and games, 10Lapse, the 3.1 submission,
Store listings, French (fr). The package declares French from `src/TenLapse.App/Strings/fr/Resources.resw`, so
Partner Center offers a French listing; one neutral `fr` listing serves France, Canada, Belgium and Switzerland.

This file has the structure of `docs/store/listing.md`, with the same fields, limits and rules; read that file for
where each limit comes from. Only the text inside the boxes is French; the notes stay in English for Sven. Lengths
count characters as Partner Center does, a line break as one. The French text uses Windows typography: a narrow
no-break space (U+202F) before : ; ? ! and inside « », and a no-break space (U+00A0) between a number and its unit.
Copy each field from inside its box so they come along.

The extension names are the Microsoft Store's own French titles (fr-FR catalog, checked 2026-10-20), quoted so that
no article has to agree with them, and they match the app's InfoBars. In the Canadian Store the HEVC one is titled
"Extensions de vidéo HEVC"; the fr-FR name is kept, as in the app, and a search for it finds the product there too.

As in English, the text says only what 3.1 does, and no field has the Windows 10 sentence. The 3.1 features use the
app's own French names (`LookText`, `CropText`, `TimingText`, `OutputText`, `MusicText`, `ProjectFileText`):
Anti-scintillement, Fusion d’images, Recadrage animé (Doux, Linéaire), Arrêt sur image au début / à la fin,
Boomerang, GIF animé (Petite, Moyenne, Grande), Musique, Projets récents, Avant / Après.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,964.

```text
Transformez un dossier de photos en vidéo timelapse fluide, jusqu’en 4K, ou en GIF animé.

Vous avez photographié un coucher de soleil, l’arrivée d’un orage ou un chantier depuis un trépied, à intervalles de quelques secondes, et vous voilà avec un dossier de centaines ou de milliers de photos. 10Lapse les enchaîne en vidéo : déposez le dossier sur la fenêtre, vérifiez l’aperçu et exportez un MP4 ou un GIF.

Comment ça marche
1. Choisissez un dossier, sélectionnez des photos ou faites-les glisser sur la fenêtre. 10Lapse lit les formats JPEG, PNG, TIFF et HEIC ainsi que les fichiers RAW de la plupart des appareils photo, et range les photos dans l’ordre de la prise de vue.
2. Réglez la vitesse de trois façons : en images par seconde, par la durée voulue pour la vidéo ou en secondes par photo pour un diaporama. Les chiffres restent à l’écran, par exemple « 742 photos → 24,7 s à 30 i/s ».
3. Choisissez le cadre : comme vos photos, 16:9 pour YouTube et la télévision, 9:16 pour les stories et les reels sur téléphone, 1:1, 4:5 ou 4:3. « Ajuster » garde toute la photo ; « Remplir » rogne les bords pour éviter les bandes.
4. Exportez un MP4 en 720p, 1080p, 1440p ou 4K, en H.264, ou en HEVC si le PC sait l’encoder et le lire. Ou exportez un GIF animé qui tourne en boucle, en taille Petite, Moyenne ou Grande (320, 480 ou 640 pixels sur le grand côté), jusqu’à 10, 20 ou 25 images par seconde. L’aperçu dessine chaque image avec le même code que l’exportation : la vidéo ressemble à l’aperçu.

Soignez le rendu
- « Anti-scintillement » atténue les sauts d’exposition entre les photos. Si vous avez changé la vitesse d’obturation, l’ouverture ou la sensibilité ISO pendant un coucher de soleil, il lit ces réglages dans les photos et change chaque palier en une rampe douce, tandis que la scène s’assombrit comme elle l’a fait.
- « Fusion d’images » fait de chaque image la moyenne de 2 à 16 photos voisines, comme une pose longue : les lumières en mouvement laissent des filés et les nuages avancent en douceur.
- « Recadrage animé » passe d’un cadrage de début à un cadrage de fin, avec un zoom jusqu’à 4 ×, en mouvement « Doux » ou « Linéaire ». Faites glisser le cadrage sur l’aperçu pour le régler.
- « Avant » et « Après », dans l’aperçu, montrent vos photos avec et sans anti-scintillement et fusion d’images.
- Ajoutez un arrêt sur image au début et à la fin de la vidéo, jusqu’à 10 s chacun, lisez les photos jusqu’à cinq fois, ou en « Boomerang », à l’aller puis au retour.
- Ajoutez une musique sous un MP4 (MP3, M4A, WAV, WMA ou FLAC) : choisissez à quel moment de la chanson la vidéo commence, avec un fondu d’entrée et un fondu de sortie. La chanson est coupée à la fin de la vidéo ; si elle est plus courte, la suite reste silencieuse. Un GIF n’a pas de son.

Montage et exportation
- Parcourez la séquence, avancez image par image, retirez une photo floue ou faites-en glisser une à sa place. Ctrl+Z la remet dans le projet. Retirer une photo la sort de la vidéo, jamais de votre disque.
- Des repères dans la pellicule signalent les endroits où des photos semblent manquer.
- Les cadences sont exactes, de 0,1 à 120 images par seconde, y compris 29,97.
- Le panneau Sortie estime la taille du fichier avant l’exportation.
- L’exportation en MP4 utilise l’encodeur vidéo de la carte graphique quand le PC en a un. L’exportation empêche la mise en veille du PC jusqu’à la fin, continue quand vous réduisez la fenêtre et s’arrête en moins d’une seconde si vous l’annulez.
- Une fois l’exportation terminée, lisez le résultat sur place ou ouvrez son dossier.
- En cas de problème, 10Lapse vous dit ce qui s’est passé et ce qu’il faut faire.

Projets
Enregistrez un projet dans un fichier .10lapse, qui garde la liste des photos, leur ordre et tous les réglages, puis rouvrez-le plus tard ou d’un double-clic. L’écran d’accueil affiche vos projets récents. Si 10Lapse se ferme de façon inattendue avant que vous ayez enregistré, il propose de restaurer vos modifications au démarrage suivant. Les photos introuvables restent dans le fichier du projet : elles reviennent dès que leur disque est de nouveau branché.

Vos photos restent chez vous
10Lapse fonctionne hors connexion. Pas de compte, pas de statistiques d’utilisation, aucun accès réseau : vos photos, vos vidéos et vos projets ne quittent jamais votre PC.

Conçu pour Windows 11
10Lapse nécessite Windows 11 sur un PC x64 ou ARM64. Les thèmes clair, sombre et à contraste élevé suivent Windows, tout comme les animations. Tout se fait au clavier, et les lecteurs d’écran annoncent chaque contrôle et la progression de l’exportation.

Des extensions facultatives du Microsoft Store ajoutent d’autres formats de photo : les photos HEIC nécessitent « Extension d’image HEIF » (sans frais) et « Extensions vidéo HEVC » (achat requis), et les fichiers RAW des appareils photo « Extension d'image Raw » (sans frais). 10Lapse indique l’extension qui manque et ouvre sa page dans le Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. This draft: 266.

```text
Créez un timelapse avec les centaines de photos d’une prise de vue au trépied : réglez la vitesse et le cadre, atténuez le scintillement, exportez un MP4 jusqu’en 4K ou un GIF animé. 10Lapse fonctionne hors connexion, sans compte, et vos photos restent sur votre PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,493. Like the English one, it is what 3.0 users get, and it leaves out the rating question.

```text
10Lapse 3.1 apporte ce qu’il faut à un timelapse après la prise de vue.

- Anti-scintillement : atténue les sauts d’exposition entre les photos. Si vous avez changé la vitesse d’obturation, l’ouverture ou l’ISO pendant un coucher de soleil, il lit ces réglages dans les photos et change chaque palier en rampe douce.
- Fusion d’images : chaque image est la moyenne de 2 à 16 photos voisines, comme une pose longue, pour des filés de lumière et des nuages fluides.
- Recadrage animé : passez d’un cadrage de début à un cadrage de fin, avec un zoom jusqu’à 4 ×, en mouvement doux ou linéaire.
- GIF animé : exportez un GIF qui tourne en boucle, en trois tailles, jusqu’à 25 images par seconde.
- Musique sous un MP4 : choisissez où commence la chanson, avec un fondu d’entrée et de sortie. Elle est coupée à la fin de la vidéo.
- Arrêt sur image au début et à la fin, jusqu’à 10 s, jusqu’à cinq lectures des photos, ou le mode Boomerang.
- Enregistrez votre travail : un fichier .10lapse se rouvre plus tard, l’écran d’accueil affiche les projets récents, et si 10Lapse plante avant l’enregistrement, il propose de restaurer vos modifications.
- Ctrl+O ouvre désormais un projet ; Choisir un dossier passe à Ctrl+I et Choisir des photos à Ctrl+Maj+I.
- Avant et Après, dans l’aperçu, comparent vos photos avec et sans anti-scintillement et fusion d’images.

Comme avant, 10Lapse fonctionne hors connexion et vos photos ne quittent jamais votre PC. Il nécessite Windows 11 sur un PC x64 ou ARM64.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are
50 to 145 characters each.

```text
Transforme un dossier de photos en vidéo timelapse jusqu’en 4K, ou en GIF animé
Importez un dossier ou des photos choisies, ou faites-les glisser sur la fenêtre ; elles se rangent dans l’ordre de la prise de vue
Lit JPEG, PNG et TIFF ; HEIC et RAW avec des extensions du Microsoft Store
Vitesse en images par seconde, par durée de la vidéo ou en secondes par photo, avec le nombre de photos et la durée à l’écran
Cadences exactes de 0,1 à 120 i/s, y compris 29,97
Formats d’image : comme les photos, 16:9, 9:16, 1:1, 4:5 ou 4:3, en mode Ajuster ou Remplir
Aperçu en direct fidèle à la vidéo exportée, et pellicule avec repères de photos manquantes : déplacez, retirez, annulez
Anti-scintillement : atténue les sauts d’exposition et, d’après les réglages des photos, change les paliers d’un coucher de soleil en rampe douce
Fusion d’images : la moyenne de 2 à 16 photos voisines, pour des filés de lumière et des nuages fluides
Recadrage animé d’un cadrage de début à un cadrage de fin, zoom jusqu’à 4 ×, mouvement Doux ou Linéaire
Arrêt sur image au début et à la fin, jusqu’à cinq lectures des photos, ou mode Boomerang
Musique sous un MP4 : choisissez où commence la chanson, avec fondu d’entrée et de sortie ; elle est coupée à la fin de la vidéo
Projets enregistrés en fichiers .10lapse, projets récents sur l’écran d’accueil, modifications restaurées après un plantage
MP4 en H.264 (ou HEVC si le PC le permet) en 720p, 1080p, 1440p ou 4K, trois niveaux de qualité, taille estimée avant l’exportation
GIF animé qui tourne en boucle, en trois tailles, jusqu’à 10, 20 ou 25 images par seconde
Exporte le MP4 avec l’encodeur vidéo de la carte graphique s’il y en a un ; continue fenêtre réduite, sans mise en veille
Lisez la vidéo ou le GIF terminé dans l’application, ou ouvrez son dossier
Des messages clairs qui disent ce qui ne va pas et ce qu’il faut faire
Fonctionne hors connexion : pas de compte, pas de statistiques, vos photos restent sur votre PC
Thèmes clair et sombre, contraste élevé, raccourcis clavier, lecteurs d’écran pris en charge
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are
9 to 17 characters, 12 words in all. French users search the English words too ("timelapse", "stop
motion"); "vidéo accélérée" is the French name of the effect.

```text
séquence d’images
timelapse
time-lapse
photos en vidéo
vidéo accélérée
stop motion
diaporama
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. These
items are 33 to 137 characters.

Minimum hardware:

```text
Windows 11 sur un PC x64 ou ARM64
Facultatif, pour les photos HEIC : « Extension d’image HEIF » (sans frais) et « Extensions vidéo HEVC » (achat requis) du Microsoft Store
Facultatif, pour les photos RAW : « Extension d'image Raw » (sans frais) du Microsoft Store
Facultatif, pour la vidéo HEVC : « Extensions vidéo HEVC » (achat requis) et une carte graphique qui encode le HEVC
```

Recommended hardware:

```text
Une carte graphique avec encodeur vidéo matériel, pour une exportation plus rapide
8 Go de mémoire ou plus pour exporter en 4K
```

What each extension adds, for Sven's answers to French reviews (#44), not for a field:

- « Extension d’image HEIF » (sans frais) et « Extensions vidéo HEVC » (achat requis) : les photos HEIC et HEIF des téléphones.
  Sans elles, ces photos sont ignorées et un message nomme l’extension manquante, avec un bouton vers sa page du Store.
- « Extension d'image Raw » (sans frais) : les fichiers RAW de la plupart des appareils photo (CR2, CR3, NEF, ARW, RAF, ORF,
  RW2, etc.). Les DNG s’ouvrent sans elle. Un RAW accompagné d’un JPEG du même nom est importé une seule fois, en JPEG.
- « Extensions vidéo HEVC » (achat requis) : l’exportation en HEVC (H.265). 10Lapse ne propose le HEVC que si le PC sait
  l’encoder et le lire ; sinon, la liste Format ne contient que le H.264.

## Screenshots

Desktop: PNG, 1366 x 768 or larger, up to 10, captions 200 characters each; the rules are those of `listing.md`.
`pwsh -File scripts/store-screenshots.ps1 -Language fr` makes the French set into `docs/store/screenshots/fr/`
(in Git LFS) from the same generated sample photos as the English set, so the app shows its French text.

Upload six, in the English order; captions are 61 to 116 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Regardez toute la séquence défiler, avec le nombre de photos, la vitesse et la durée de la vidéo toujours à l’écran.` |
| `03-vertical.png` | 1920 x 1080 | `Cadrez en 9:16 pour les stories et les reels sur téléphone, ou en 16:9, 1:1, 4:5 ou 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Exportez un MP4 jusqu’en 4K et suivez le rendu de chaque image, avec le temps restant.` |
| `05-finished.png` | 1920 x 1080 | `Lisez la vidéo terminée tout de suite, ou ouvrez son dossier.` |
| `06-per-photo.png` | 1920 x 1080 | `Créez un diaporama en secondes par photo, ou réglez les images par seconde ou la durée de la vidéo.` |
| `01-first-launch.png` | 1920 x 1080 | `Déposez un dossier de photos pour commencer. Vos photos ne quittent jamais votre PC.` |

As in English, do not upload 07-settings and 08-narrow.

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 41.

```text
© 2026 Sven Borden. Tous droits réservés.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, empty short and voice titles, the sort title Sven chooses (the same
"Ten Lapse" helps French searches too), no additional license terms; trailers and logos belong to #43 and #37. The
privacy policy and support URLs are set once, on the Properties page, for every language.
