# 10Lapse 3.0 Store listing (French)

The text Sven pastes into Partner Center for the 3.0 update (#42, #115): Apps and games, 10Lapse, the 3.0 submission,
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

As in English, the text says only what 3.0 does.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 3,055.

```text
Transformez un dossier de photos en vidéo timelapse fluide, jusqu’en 4K.

Vous avez photographié un coucher de soleil, l’arrivée d’un orage ou un chantier depuis un trépied, à intervalles de quelques secondes, et vous voilà avec un dossier de centaines ou de milliers de photos. 10Lapse les enchaîne en vidéo : déposez le dossier sur la fenêtre, vérifiez l’aperçu et exportez un MP4.

Comment ça marche
1. Choisissez un dossier, sélectionnez des photos ou faites-les glisser sur la fenêtre. 10Lapse lit les formats JPEG, PNG, TIFF et HEIC ainsi que les fichiers RAW de la plupart des appareils photo, et range les photos dans l’ordre de la prise de vue.
2. Réglez la vitesse de trois façons : en images par seconde, par la durée voulue pour la vidéo ou en secondes par photo pour un diaporama. Les chiffres restent à l’écran, par exemple « 742 photos → 24,7 s à 30 i/s ».
3. Choisissez le cadre : comme vos photos, 16:9 pour YouTube et la télévision, 9:16 pour les stories et les reels sur téléphone, 1:1, 4:5 ou 4:3. « Ajuster » garde toute la photo ; « Remplir » rogne les bords pour éviter les bandes.
4. Exportez un MP4 en 720p, 1080p, 1440p ou 4K, en H.264, ou en HEVC si le PC sait l’encoder et le lire. L’aperçu dessine chaque image avec le même code que l’exportation : la vidéo ressemble à l’aperçu.

Montage et exportation
- Parcourez la séquence, avancez image par image, retirez une photo floue ou faites-en glisser une à sa place. Ctrl+Z la remet dans le projet. Retirer une photo la sort de la vidéo, jamais de votre disque.
- Des repères dans la pellicule signalent les endroits où des photos semblent manquer.
- Les cadences sont exactes, de 0,1 à 120 images par seconde, y compris 29,97.
- Le panneau Sortie estime la taille du fichier avant l’exportation.
- L’exportation utilise l’encodeur vidéo de la carte graphique quand le PC en a un. Elle empêche la mise en veille du PC jusqu’à la fin, continue quand vous réduisez la fenêtre et s’arrête en moins d’une seconde si vous l’annulez.
- Une fois la vidéo prête, lisez-la sur place ou ouvrez son dossier.
- En cas de problème, 10Lapse vous dit ce qui s’est passé et ce qu’il faut faire.

Vos photos restent chez vous
10Lapse fonctionne hors connexion. Pas de compte, pas de statistiques d’utilisation, aucun accès réseau : vos photos et vos vidéos ne quittent jamais votre PC.

Conçu pour Windows 11
10Lapse 3.0 nécessite Windows 11 sur un PC x64 ou ARM64 ; sous Windows 10, le Store propose à la place l’ancienne version 10Lapse 2.1.1. Les thèmes clair, sombre et à contraste élevé suivent Windows, tout comme les animations. Tout se fait au clavier, et les lecteurs d’écran annoncent chaque contrôle et la progression de l’exportation.

Des extensions facultatives du Microsoft Store ajoutent d’autres formats de photo : les photos HEIC nécessitent « Extension d’image HEIF » (sans frais) et « Extensions vidéo HEVC » (achat requis), et les fichiers RAW des appareils photo « Extension d'image Raw » (sans frais). 10Lapse indique l’extension qui manque et ouvre sa page dans le Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. This draft: 262.

```text
Créez une vidéo timelapse à partir des centaines de photos d’une prise de vue au trépied : réglez la vitesse et le cadre, regardez l’aperçu et exportez un MP4 jusqu’en 4K. 10Lapse fonctionne hors connexion, sans compte, et vos photos ne quittent jamais votre PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,493. Like the English one, it does not claim a crash rate.

```text
10Lapse 3.0 a été entièrement reconstruit pour Windows 11.

- Réécrit de zéro : le code à l’origine des plantages de la 2.1.1 a disparu. En cas de problème, 10Lapse vous dit ce qui s’est passé et ce qu’il faut faire.
- Importe les photos HEIC et .jpeg. Les HEIC nécessitent « Extension d’image HEIF » (sans frais) et « Extensions vidéo HEVC » (achat requis) du Microsoft Store ; 10Lapse indique celle qui manque.
- Exportation jusqu’en 4K, en H.264, ou en HEVC si le PC le permet, avec progression, temps restant et un bouton Annuler qui l’arrête en moins d’une seconde.
- L’exportation continue quand vous changez de fenêtre ou la réduisez, avec sa progression dans la barre des tâches et une notification quand la vidéo est prête.
- Nouveau mode Par photo pour les diaporamas, en plus des images par seconde et de la durée.
- Un aperçu en direct et une pellicule : parcourez, avancez image par image, retirez ou déplacez des photos, et annulez.
- Des formats d’image pour la télévision, les téléphones et les réseaux sociaux : 16:9, 9:16, 1:1, 4:5, 4:3 ou les proportions de vos photos.
- Vos derniers réglages de vitesse, de cadre et de sortie sont mémorisés pour le projet suivant.
- Thèmes clair et sombre, contraste élevé, raccourcis clavier, lecteurs d’écran pris en charge.
- Confidentialité : pas de compte, et désormais aucun accès réseau ; vos photos ne quittent jamais votre PC.

La 3.0 nécessite Windows 11 sur un PC x64 ou ARM64. Sous Windows 10, le Store conserve 10Lapse 2.1.1.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 19 are
43 to 100 characters each.

```text
Transforme un dossier de photos en vidéo timelapse, jusqu’en 4K
Importez un dossier, des photos choisies, ou faites-les glisser sur la fenêtre
Lit JPEG, PNG et TIFF ; HEIC et RAW avec des extensions du Microsoft Store
Range les photos dans l’ordre de la prise de vue ; faites glisser une miniature pour la déplacer
Vitesse en images par seconde, par durée de la vidéo ou en secondes par photo
Cadences exactes de 0,1 à 120 i/s, y compris 29,97
Nombre de photos, vitesse et durée de la vidéo toujours à l’écran
Formats d’image : comme les photos, 16:9, 9:16, 1:1, 4:5 ou 4:3, en mode Ajuster ou Remplir
Aperçu en direct fidèle à la vidéo exportée
Pellicule avec miniatures, repères de photos manquantes, retrait et annulation
MP4 en H.264 (ou HEVC si le PC le permet) en 720p, 1080p, 1440p ou 4K, avec trois niveaux de qualité
Estimation de la taille du fichier avant l’exportation
Exporte avec l’encodeur vidéo de la carte graphique quand le PC en a un
Continue d’exporter fenêtre réduite et empêche la mise en veille jusqu’à la fin
Lisez la vidéo terminée dans l’application ou ouvrez son dossier
Des messages clairs qui disent ce qui ne va pas et ce qu’il faut faire
Fonctionne hors connexion : pas de compte, pas de statistiques, vos photos restent sur votre PC
Thèmes clair et sombre, contraste élevé, raccourcis clavier, lecteurs d’écran pris en charge
Mémorise vos derniers réglages de vitesse, de cadre et de sortie
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

## Until 3.0 is public (#7)

Only if the 2.1.1 listing has a French description: the same listing-only edit as in `listing.md`, its last line
replaced with the line below. This draft: 68 characters.

```text
10Lapse 3.0 est en cours de reconstruction complète pour Windows 11.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 41.

```text
© 2026 Sven Borden. Tous droits réservés.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, empty short and voice titles, the sort title Sven chooses (the same
"Ten Lapse" helps French searches too), no additional license terms; trailers and logos belong to #43 and #37. The
privacy policy and support URLs are set once, on the Properties page, for every language.
