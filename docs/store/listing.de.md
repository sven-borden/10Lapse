# 10Lapse 3.0 Store listing (German)

The text Sven pastes into Partner Center for the 3.0 update (#116): Apps and games, 10Lapse, the 3.0 submission,
Store listings, German (Deutsch). One `de` listing serves Germany, Austria and Switzerland, as the package's `de`
resources do. It is `docs/store/listing.md` translated, with the same fields, limits and order; the notes around the
boxes stay in English for Sven, the boxes are what the Store shows.

Each field below says its limit and this draft's length, counted as Partner Center does (a line break as one). The
limits are the ones `listing.md` checked on Microsoft Learn on 2026-10-20; a limit marked **verify** is not on those
pages. Copy each field from inside its box: the boxes are plain text, without Markdown.

The German follows the app: formal address ("Sie"), as Windows 11 writes German; the Store names of the extensions
exactly as the German Store shows them (`HEIF-Bilderweiterung`, `HEVC-Videoerweiterungen`, `Raw-Bilderweiterung`);
the app's own labels in „…“ where the text names one. As in English, the text says only what 3.0 does.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 3,110.

```text
Machen Sie aus einem Ordner voller Fotos ein flüssiges Zeitraffervideo, bis 4K.

Sie haben einen Sonnenuntergang, ein aufziehendes Gewitter oder eine Baustelle vom Stativ aus fotografiert, alle paar Sekunden ein Foto, und haben jetzt einen Ordner mit Hunderten oder Tausenden Fotos. 10Lapse spielt sie als Video ab: Ziehen Sie den Ordner auf das Fenster, prüfen Sie die Vorschau, und exportieren Sie ein MP4-Video.

So funktioniert es
1. Wählen Sie einen Ordner oder einzelne Fotos aus, oder ziehen Sie sie auf das Fenster. 10Lapse liest JPEG, PNG, TIFF, HEIC und die RAW-Dateien der meisten Kameras und ordnet sie nach Aufnahmezeit.
2. Legen Sie das Tempo auf eine von drei Arten fest: Bilder pro Sekunde, die gewünschte Videodauer oder Sekunden pro Foto für eine Diashow. Die Zahlen sind immer zu sehen, zum Beispiel „742 Fotos → 24,7 s bei 30 fps“.
3. Wählen Sie das Bildformat: wie Ihre Fotos, 16:9 für YouTube und Fernseher, 9:16 für Storys und Reels auf dem Smartphone, 1:1, 4:5 oder 4:3. „Anpassen“ zeigt das ganze Foto; „Ausfüllen“ schneidet die Ränder ab, sodass keine Balken entstehen.
4. Exportieren Sie ein MP4-Video in 720p, 1080p, 1440p oder 4K, mit H.264 oder mit HEVC, wenn der PC es codieren und wiedergeben kann. Die Vorschau zeichnet jedes Bild mit demselben Code wie der Export, deshalb sieht das Video genauso aus wie die Vorschau.

Bearbeiten und exportieren
- Spulen Sie durch die Sequenz, gehen Sie Bild für Bild vor, und entfernen Sie ein unscharfes Foto oder ziehen Sie eines an seinen Platz. Strg+Z holt es zurück. Ein entferntes Foto verschwindet aus dem Video, nie von Ihrem Datenträger.
- Lückenmarkierungen im Filmstreifen zeigen, wo anscheinend Fotos fehlen.
- Die Bildraten sind exakt, auch 29,97, von 0,1 bis 120 Bildern pro Sekunde.
- Der Bereich „Ausgabe“ schätzt die Dateigröße schon vor dem Export.
- Der Export nutzt den Videoencoder der Grafikkarte, wenn der PC einen hat. Er hält den PC wach, bis das Video fertig ist, läuft weiter, wenn Sie das Fenster minimieren, und stoppt innerhalb einer Sekunde, wenn Sie abbrechen.
- Ist das Video fertig, spielen Sie es direkt in der App ab oder öffnen seinen Ordner.
- Wenn etwas schiefgeht, sagt 10Lapse, was passiert ist und was Sie als Nächstes tun können.

Datenschutz von Grund auf
10Lapse funktioniert offline. Es gibt kein Konto, keine Analysedaten und keinen Netzwerkzugriff: Ihre Fotos und Videos verlassen nie Ihren PC.

Für Windows 11 gemacht
10Lapse 3.0 benötigt Windows 11 auf einem x64- oder ARM64-PC; unter Windows 10 bietet der Store stattdessen das ältere 10Lapse 2.1.1 an. Helles und dunkles Design sowie Kontrastdesigns folgen Windows, ebenso die Animationen. Alles lässt sich mit der Tastatur bedienen, und Screenreader sagen jedes Steuerelement und den Fortschritt des Exports an.

Optionale Erweiterungen aus dem Microsoft Store fügen weitere Fotoformate hinzu: HEIC-Fotos benötigen die HEIF-Bilderweiterung (kostenlos) und die HEVC-Videoerweiterungen (kostenpflichtig), RAW-Dateien von Kameras die Raw-Bilderweiterung (kostenlos). 10Lapse nennt die fehlende Erweiterung und verlinkt auf ihre Store-Seite.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 236.

```text
Machen Sie aus Hunderten Fotos einer Serie vom Stativ ein Zeitraffervideo: Tempo und Bildformat festlegen, Vorschau ansehen und als MP4 bis 4K exportieren. 10Lapse funktioniert offline, ohne Konto, und Ihre Fotos verlassen nie Ihren PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,489. It names what the reviews of 2.1.1 reported (#44) and says only what 3.0
does; it does not claim a crash rate.

```text
10Lapse 3.0 wurde für Windows 11 von Grund auf neu entwickelt.

- Komplett neu geschrieben: Der Code hinter den Abstürzen von 2.1.1 ist weg. Wenn etwas schiefgeht, sagt 10Lapse, was passiert ist und was Sie als Nächstes tun können.
- Importiert HEIC- und .jpeg-Fotos. HEIC benötigt die HEIF-Bilderweiterung (kostenlos) und die HEVC-Videoerweiterungen (kostenpflichtig) aus dem Microsoft Store; 10Lapse sagt Ihnen, was fehlt.
- Export bis 4K, mit H.264 oder HEVC, wo der PC es unterstützt, mit Fortschritt, Restzeit und der Schaltfläche „Export abbrechen“, die den Export innerhalb einer Sekunde stoppt.
- Arbeitet weiter, wenn Sie die App wechseln oder minimieren: Der Export läuft weiter, mit Fortschritt in der Taskleiste und einer Benachrichtigung, wenn das Video fertig ist.
- Neues Tempo „Pro Foto“ für Diashows, neben Bildern pro Sekunde und Videodauer.
- Live-Vorschau und Filmstreifen: durchspulen, Bild für Bild vorgehen, Fotos entfernen oder neu anordnen und rückgängig machen.
- Bildformate für Fernseher, Smartphones und soziale Netzwerke: 16:9, 9:16, 1:1, 4:5, 4:3 oder das Format Ihrer Fotos.
- Ihr letztes Tempo, Bildformat und Ihre letzte Ausgabe gelten auch im nächsten Projekt.
- Helles und dunkles Design, Tastenkombinationen, Screenreader-Unterstützung und Kontrastdesigns.
- Privat: kein Konto und jetzt gar kein Netzwerkzugriff; Ihre Fotos verlassen nie Ihren PC.

3.0 benötigt Windows 11 auf einem x64- oder ARM64-PC. Unter Windows 10 behält der Store 10Lapse 2.1.1.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 19 are 39
to 106 characters each.

```text
Macht aus einem Fotoordner ein Zeitraffervideo, bis 4K
Import eines Ordners, ausgewählter Fotos oder per Ziehen auf das Fenster
Liest JPEG, PNG und TIFF; HEIC und Kamera-RAW mit Erweiterungen aus dem Microsoft Store
Ordnet Fotos nach Aufnahmezeit; eine Miniatur lässt sich per Ziehen verschieben
Tempo nach Bildern pro Sekunde, nach Videodauer oder nach Sekunden pro Foto
Exakte Bildraten von 0,1 bis 120 fps, auch 29,97
Fotoanzahl, Tempo und Videodauer immer im Blick
Bildformate: wie die Fotos, 16:9, 9:16, 1:1, 4:5 oder 4:3, mit „Anpassen“ oder „Ausfüllen“
Live-Vorschau, die dem exportierten Video entspricht
Filmstreifen mit Miniaturen, Lückenmarkierungen für fehlende Fotos, Entfernen und Rückgängig
MP4 mit H.264 (oder HEVC, wo der PC es unterstützt) in 720p, 1080p, 1440p oder 4K, in drei Qualitätsstufen
Schätzung der Dateigröße vor dem Export
Exportiert mit dem Videoencoder der Grafikkarte, wenn der PC einen hat
Exportiert auch minimiert weiter und hält den PC wach, bis das Video fertig ist
Fertiges Video in der App abspielen oder seinen Ordner öffnen
Klare Meldungen, die sagen, was schiefgelaufen ist und was als Nächstes zu tun ist
Funktioniert offline: kein Konto, keine Analysedaten, Fotos verlassen nie Ihren PC
Helles und dunkles Design, Kontrastdesigns, Tastenkombinationen, Screenreader-Unterstützung
Merkt sich Ihr letztes Tempo, Bildformat und Ihre letzte Ausgabe
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are 7 to
20 characters, 13 words in all. "timelapse" stays: German users search the English word too.

```text
zeitraffer erstellen
zeitraffer video
timelapse
fotos zu video
stop motion
diashow
zeitraffer app
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. These
items are 39 to 128 characters.

Minimum hardware:

```text
Windows 11 auf einem x64- oder ARM64-PC
Optional, für HEIC-Fotos: HEIF-Bilderweiterung (kostenlos) und HEVC-Videoerweiterungen (kostenpflichtig) aus dem Microsoft Store
Optional, für Kamera-RAW-Fotos: Raw-Bilderweiterung (kostenlos) aus dem Microsoft Store
Optional, für HEVC-Videos: HEVC-Videoerweiterungen (kostenpflichtig) und eine Grafikkarte, die HEVC codiert
```

Recommended hardware:

```text
Eine Grafikkarte mit Hardware-Videoencoder für einen schnelleren Export
8 GB Arbeitsspeicher oder mehr für den Export in 4K
```

The notes on what each extension adds, for Sven's answers to reviews (#44), are in `listing.md`; the German Store names
are `HEIF-Bilderweiterung` (9PMMSR1CGPWG), `HEVC-Videoerweiterungen` (9NMZLZ57R3T7) and `Raw-Bilderweiterung`
(9NCTDW2W1BH8).

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`pwsh -File scripts/store-screenshots.ps1 -Language de` makes the German set into `docs/store/screenshots/de/`
(in Git LFS), from the same generated sample photos as the English set.

Upload six, in this order; captions are 71 to 109 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Sehen Sie die ganze Sequenz ablaufen, mit Fotoanzahl, Tempo und Videodauer immer im Blick.` |
| `03-vertical.png` | 1920 x 1080 | `Wählen Sie 9:16 für Storys und Reels auf dem Smartphone oder 16:9, 1:1, 4:5 oder 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Exportieren Sie ein MP4-Video bis 4K, und verfolgen Sie Bild für Bild und die Restzeit.` |
| `05-finished.png` | 1920 x 1080 | `Spielen Sie das fertige Video sofort ab, oder öffnen Sie seinen Ordner.` |
| `06-per-photo.png` | 1920 x 1080 | `Erstellen Sie eine Diashow mit Sekunden pro Foto, oder legen Sie Bilder pro Sekunde oder die Videodauer fest.` |
| `01-first-launch.png` | 1920 x 1080 | `Ziehen Sie einen Fotoordner hierher, um zu beginnen. Ihre Fotos verlassen nie Ihren PC.` |

Do not upload 07-settings and 08-narrow, as in English.

## Until 3.0 is public (#7)

Only if the 2.1.1 listing has a German page: the same listing-only edit as in English, replacing the "BETA REALEASE!"
line with the line below. This draft: 61 characters.

```text
10Lapse 3.0 wird für Windows 11 von Grund auf neu entwickelt.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 44.

```text
© 2026 Sven Borden. Alle Rechte vorbehalten.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, the empty short and voice titles, the sort title Ten Lapse, the empty
additional license terms, the trailer (#43) and the Store logos (#37). The privacy policy and support URLs are on the
Properties page and are the same for every language.
