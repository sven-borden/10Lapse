# 10Lapse 3.1 Store listing (German)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #116): Apps and games, 10Lapse, the 3.1 submission,
Store listings, German (Deutsch). One `de` listing serves Germany, Austria and Switzerland, as the package's `de`
resources do. It is `docs/store/listing.md` translated, with the same fields, limits and order; the notes around the
boxes stay in English for Sven, the boxes are what the Store shows.

Each field below says its limit and this draft's length, counted as Partner Center does (a line break as one). The
limits are the ones `listing.md` checked on Microsoft Learn on 2026-10-20; a limit marked **verify** is not on those
pages. Copy each field from inside its box: the boxes are plain text, without Markdown.

The German follows the app: formal address ("Sie"), as Windows 11 writes German; the Store names of the extensions
exactly as the German Store shows them (`HEIF-Bilderweiterung`, `HEVC-Videoerweiterungen`, `Raw-Bilderweiterung`);
the app's own labels in „…“ where the text names one. As in English, the text says only what 3.1 does, and no field
has the Windows 10 sentence. The 3.1 features use the app's German names: Entflackern, Überlagern (frame blending),
Zoomen und Schwenken (Sanft, Gleichmäßig), Standbild am Anfang / am Ende, Bumerang, Durchläufe, Animiertes GIF (Klein,
Mittel, Groß), Musik, Zuletzt verwendete Projekte, Vorher / Nachher.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,987.

```text
Machen Sie aus einem Ordner voller Fotos ein flüssiges Zeitraffervideo, bis 4K, oder ein animiertes GIF.

Sie haben einen Sonnenuntergang, ein aufziehendes Gewitter oder eine Baustelle vom Stativ aus fotografiert, alle paar Sekunden ein Foto, und haben jetzt einen Ordner mit Hunderten oder Tausenden Fotos. 10Lapse spielt sie als Video ab: Ziehen Sie den Ordner auf das Fenster, prüfen Sie die Vorschau, und exportieren Sie ein MP4-Video oder ein GIF.

So funktioniert es
1. Wählen Sie einen Ordner oder einzelne Fotos aus, oder ziehen Sie sie auf das Fenster. 10Lapse liest JPEG, PNG, TIFF, HEIC und die RAW-Dateien der meisten Kameras und ordnet sie nach Aufnahmezeit.
2. Legen Sie das Tempo auf eine von drei Arten fest: Bilder pro Sekunde, die gewünschte Videodauer oder Sekunden pro Foto für eine Diashow. Die Zahlen sind immer zu sehen, zum Beispiel „742 Fotos → 24,7 s bei 30 fps“.
3. Wählen Sie das Bildformat: wie Ihre Fotos, 16:9 für YouTube und Fernseher, 9:16 für Storys und Reels auf dem Smartphone, 1:1, 4:5 oder 4:3. „Anpassen“ zeigt das ganze Foto; „Ausfüllen“ schneidet die Ränder ab, sodass keine Balken entstehen.
4. Exportieren Sie ein MP4-Video in 720p, 1080p, 1440p oder 4K, mit H.264 oder mit HEVC, wenn der PC es codieren und wiedergeben kann. Oder exportieren Sie ein animiertes GIF, das endlos läuft, in „Klein“, „Mittel“ oder „Groß“ (320, 480 oder 640 Pixel an der langen Seite), mit bis zu 10, 20 oder 25 Bildern pro Sekunde. Die Vorschau zeichnet jedes Bild mit demselben Code wie der Export, deshalb sieht das Video genauso aus wie die Vorschau.

Der richtige Look
- „Entflackern“ gleicht Belichtungssprünge zwischen Fotos aus. Haben Sie bei einem Sonnenuntergang Belichtungszeit, Blende oder ISO verstellt, liest es diese Einstellungen aus den Fotos und macht aus jeder Stufe einen sanften Übergang, während die Szene so dunkel wird, wie sie es wirklich wurde.
- „Überlagern“ macht jedes Bild zum Durchschnitt von 2 bis 16 benachbarten Fotos, wie eine Langzeitbelichtung: Bewegte Lichter hinterlassen Lichtspuren, und Wolken ziehen weich vorbei.
- „Zoomen und Schwenken“ wandert von einem Start- zu einem Endausschnitt, mit bis zu 4× Zoom, „Sanft“ oder „Gleichmäßig“. Ziehen Sie den Ausschnitt in der Vorschau an seinen Platz.
- „Vorher“ und „Nachher“ in der Vorschau zeigen Ihre Fotos mit und ohne Entflackern und Überlagern.
- Ein Standbild am Anfang und am Ende des Videos, je bis zu 10 Sekunden, bis zu fünf Durchläufe der Fotos oder ein „Bumerang“, vorwärts und wieder zurück.
- Musik unter einem MP4-Video (MP3, M4A, WAV, WMA oder FLAC): Wählen Sie, an welcher Stelle des Songs das Video beginnt, und blenden Sie die Musik ein und aus. Der Song wird am Ende des Videos abgeschnitten; ist er kürzer, bleibt der Rest still. Ein GIF hat keinen Ton.

Bearbeiten und exportieren
- Spulen Sie durch die Sequenz, gehen Sie Bild für Bild vor, und entfernen Sie ein unscharfes Foto oder ziehen Sie eines an seinen Platz. Strg+Z holt es zurück. Ein entferntes Foto verschwindet aus dem Video, nie von Ihrem Datenträger.
- Lückenmarkierungen im Filmstreifen zeigen, wo anscheinend Fotos fehlen.
- Die Bildraten sind exakt, auch 29,97, von 0,1 bis 120 Bildern pro Sekunde.
- Der Bereich „Ausgabe“ schätzt die Dateigröße schon vor dem Export.
- Ein MP4-Export nutzt den Videoencoder der Grafikkarte, wenn der PC einen hat. Der Export hält den PC wach, bis er fertig ist, läuft weiter, wenn Sie das Fenster minimieren, und stoppt innerhalb einer Sekunde, wenn Sie abbrechen.
- Ist der Export fertig, spielen Sie das Ergebnis direkt in der App ab oder öffnen seinen Ordner.
- Wenn etwas schiefgeht, sagt 10Lapse, was passiert ist und was Sie als Nächstes tun können.

Projekte
Speichern Sie ein Projekt als .10lapse-Datei, die die Liste der Fotos, ihre Reihenfolge und alle Einstellungen enthält, und öffnen Sie es später wieder oder per Doppelklick. Der Startbildschirm zeigt Ihre zuletzt verwendeten Projekte. Wird 10Lapse unerwartet beendet, bevor Sie gespeichert haben, bietet es beim nächsten Start an, Ihre Änderungen wiederherzustellen. Fotos, die ein Projekt nicht findet, bleiben in seiner Datei und kommen zurück, sobald ihr Laufwerk wieder verbunden ist.

Datenschutz von Grund auf
10Lapse funktioniert offline. Es gibt kein Konto, keine Analysedaten und keinen Netzwerkzugriff: Ihre Fotos, Videos und Projekte verlassen nie Ihren PC.

Für Windows 11 gemacht
10Lapse benötigt Windows 11 auf einem x64- oder ARM64-PC. Helles und dunkles Design sowie Kontrastdesigns folgen Windows, ebenso die Animationen. Alles lässt sich mit der Tastatur bedienen, und Screenreader sagen jedes Steuerelement und den Fortschritt des Exports an.

Optionale Erweiterungen aus dem Microsoft Store fügen weitere Fotoformate hinzu: HEIC-Fotos benötigen die HEIF-Bilderweiterung (kostenlos) und die HEVC-Videoerweiterungen (kostenpflichtig), RAW-Dateien von Kameras die Raw-Bilderweiterung (kostenlos). 10Lapse nennt die fehlende Erweiterung und verlinkt auf ihre Store-Seite.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 260.

```text
Machen Sie aus Hunderten Fotos einer Serie vom Stativ ein Zeitraffervideo: Tempo und Bildformat festlegen, Flackern glätten und als MP4 bis 4K oder als animiertes GIF exportieren. 10Lapse funktioniert offline, ohne Konto, und Ihre Fotos verlassen nie Ihren PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,485. As in English, it is what 3.0 users get with the update,
and it leaves out the rating question.

```text
10Lapse 3.1 bringt, was ein Zeitraffer nach der Aufnahme braucht.

- Entflackern: gleicht Belichtungssprünge zwischen Fotos aus. Haben Sie bei einem Sonnenuntergang Belichtungszeit, Blende oder ISO verstellt, liest es die Werte aus den Fotos und macht aus jeder Stufe einen sanften Übergang.
- Überlagern: Jedes Bild ist der Durchschnitt von 2 bis 16 benachbarten Fotos, wie bei einer Langzeitbelichtung, für Lichtspuren und weich ziehende Wolken.
- Zoomen und Schwenken: von einem Start- zu einem Endausschnitt, mit bis zu 4× Zoom, sanft oder gleichmäßig.
- Animiertes GIF: ein GIF, das endlos läuft, in drei Größen, mit bis zu 25 Bildern pro Sekunde.
- Musik unter einem MP4-Video: Wählen Sie, wo der Song beginnt, und blenden Sie ihn ein und aus. Er wird am Ende des Videos abgeschnitten.
- Standbild am Anfang und am Ende, bis zu 10 Sekunden, bis zu fünf Durchläufe der Fotos oder ein Bumerang.
- Projekte speichern: Eine .10lapse-Projektdatei lässt sich später wieder öffnen, der Startbildschirm zeigt die zuletzt verwendeten Projekte, und stürzt 10Lapse vor dem Speichern ab, bietet es an, Ihre Änderungen wiederherzustellen.
- Strg+O öffnet jetzt ein Projekt; „Ordner auswählen“ liegt nun auf Strg+I, „Fotos auswählen“ auf Strg+Umschalt+I.
- „Vorher“ und „Nachher“ in der Vorschau vergleichen Ihre Fotos mit und ohne Entflackern und Überlagern.

Wie bisher funktioniert 10Lapse offline, und Ihre Fotos verlassen nie Ihren PC. Es benötigt Windows 11 auf einem x64- oder ARM64-PC.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are 48
to 151 characters each.

```text
Macht aus einem Fotoordner ein Zeitraffervideo bis 4K oder ein animiertes GIF
Import eines Ordners oder ausgewählter Fotos oder per Ziehen auf das Fenster; die Fotos ordnen sich nach Aufnahmezeit
Liest JPEG, PNG und TIFF; HEIC und Kamera-RAW mit Erweiterungen aus dem Microsoft Store
Tempo nach Bildern pro Sekunde, nach Videodauer oder nach Sekunden pro Foto, mit Fotoanzahl und Videodauer immer im Blick
Exakte Bildraten von 0,1 bis 120 fps, auch 29,97
Bildformate: wie die Fotos, 16:9, 9:16, 1:1, 4:5 oder 4:3, mit „Anpassen“ oder „Ausfüllen“
Live-Vorschau, die dem exportierten Video entspricht, und Filmstreifen mit Lückenmarkierungen: verschieben, entfernen, rückgängig machen
Entflackern gleicht Belichtungssprünge aus und macht anhand der Belichtungswerte der Fotos aus den Stufen eines Sonnenuntergangs einen sanften Übergang
Überlagern: Durchschnitt von 2 bis 16 benachbarten Fotos für Lichtspuren und weich ziehende Wolken
Zoomen und Schwenken von einem Start- zu einem Endausschnitt, bis 4× Zoom, „Sanft“ oder „Gleichmäßig“
Standbild am Anfang und am Ende, bis zu fünf Durchläufe der Fotos oder ein Bumerang
Musik unter einem MP4-Video: Startpunkt im Song wählen, ein- und ausblenden; sie wird am Videoende abgeschnitten
Projekte als .10lapse-Dateien speichern, zuletzt verwendete auf dem Startbildschirm, Änderungen nach einem Absturz wiederherstellen
MP4 mit H.264 (oder HEVC, wo der PC es unterstützt) in 720p, 1080p, 1440p oder 4K, drei Qualitätsstufen, Dateigröße vorab geschätzt
Animiertes GIF, das endlos läuft, in drei Größen, mit bis zu 10, 20 oder 25 Bildern pro Sekunde
Exportiert MP4 mit dem Videoencoder der Grafikkarte, wenn es einen gibt; läuft minimiert weiter und hält den PC wach
Fertiges Video oder GIF in der App abspielen oder seinen Ordner öffnen
Klare Meldungen, die sagen, was schiefgelaufen ist und was als Nächstes zu tun ist
Funktioniert offline: kein Konto, keine Analysedaten, Fotos verlassen nie Ihren PC
Helles und dunkles Design, Kontrastdesigns, Tastenkombinationen, Screenreader-Unterstützung
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

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 44.

```text
© 2026 Sven Borden. Alle Rechte vorbehalten.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, the empty short and voice titles, the sort title Ten Lapse, the empty
additional license terms, the trailer (#43) and the Store logos (#37). The privacy policy and support URLs are on the
Properties page and are the same for every language.
