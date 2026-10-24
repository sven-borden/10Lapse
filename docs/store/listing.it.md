# 10Lapse 3.1 Store listing (Italian)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #118): Apps and games, 10Lapse, the 3.1 submission,
Store listings, Italian (Italiano). One `it` listing serves Italy and Switzerland, as the package's `it` resources do.
It is `docs/store/listing.md` translated, with the same fields, limits and order; the notes around the boxes stay in
English for Sven, the boxes are what the Store shows.

Each field below says its limit and this draft's length, counted as Partner Center does (a line break as one). The
limits are the ones `listing.md` checked on Microsoft Learn on 2026-10-20; a limit marked **verify** is not on those
pages. Copy each field from inside its box: the boxes are plain text, without Markdown.

The Italian follows the app: informal address ("tu"), as Windows 11 writes Italian; the Store names of the extensions
exactly as the Italian Store shows them (`Estensioni immagini HEIF`, `Estensioni video HEVC`, `Estensione immagini
Raw`), with the invariant price labels "(gratis)" and "(a pagamento)"; the app's own labels in “…” where the text
names one. As in English, the text says only what 3.1 does, and no field has the Windows 10 sentence. The 3.1 features
use the app's Italian names: Antisfarfallio, Fusione di fotogrammi, Ritaglio animato (Morbido, Lineare), Fermo immagine
all'inizio / alla fine, Boomerang, Riproduzioni, GIF animata (Piccole, Medie, Grandi: they agree with "Dimensioni"),
Musica, Progetti recenti, Prima / Dopo. "In tre misure" stands for the three sizes, because "in tre dimensioni" reads as
3D.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,678.

```text
Trasforma una cartella di foto in un video timelapse fluido, fino a 4K, o in una GIF animata.

Hai fotografato un tramonto, un temporale in arrivo o un cantiere dal treppiede, una foto a intervalli di pochi secondi, e ora hai una cartella con centinaia o migliaia di foto. 10Lapse le riproduce come un video: trascina la cartella sulla finestra, controlla l'anteprima ed esporta un MP4 o una GIF.

Come funziona
1. Scegli una cartella, seleziona le foto o trascinale sulla finestra. 10Lapse legge JPEG, PNG, TIFF, HEIC e i file RAW della maggior parte delle fotocamere, e mette le foto nell'ordine in cui sono state scattate.
2. Imposta la velocità in uno di tre modi: fotogrammi al secondo, la durata che deve avere il video oppure secondi per foto, per una presentazione. I numeri sono sempre sullo schermo, ad esempio “742 foto → 24,7 s a 30 fps”.
3. Scegli le proporzioni: come le tue foto, 16:9 per YouTube e la TV, 9:16 per storie e reel sul telefono, 1:1, 4:5 o 4:3. “Adatta” mostra la foto intera; “Riempi” ritaglia i bordi per non lasciare bande.
4. Esporta un MP4 a 720p, 1080p, 1440p o 4K, in H.264 oppure in HEVC se il PC può codificarlo e riprodurlo. Oppure esporta una GIF animata che si ripete all'infinito, in dimensioni “Piccole”, “Medie” o “Grandi” (320, 480 o 640 pixel sul lato lungo), fino a 10, 20 o 25 fotogrammi al secondo. L'anteprima disegna ogni fotogramma con lo stesso codice dell'esportazione, quindi il video corrisponde all'anteprima.

L'aspetto giusto
- “Antisfarfallio” attenua i salti di esposizione tra le foto. Se durante un tramonto hai cambiato il tempo di scatto, il diaframma o gli ISO, legge queste impostazioni dalle foto e trasforma ogni gradino in una rampa graduale, mentre la scena si scurisce come è successo davvero.
- “Fusione di fotogrammi” fa di ogni fotogramma la media delle foto vicine, da 2 a 16, come una lunga esposizione: le luci in movimento lasciano scie e le nuvole si muovono fluide.
- “Ritaglio animato” passa da un'inquadratura iniziale a una finale, con uno zoom fino a 4×, “Morbido” o “Lineare”. Trascina l'inquadratura sull'anteprima per impostarla.
- “Prima” e “Dopo”, nell'anteprima, mostrano le tue foto con e senza antisfarfallio e fusione di fotogrammi.
- Un fermo immagine all'inizio e alla fine del video, fino a 10 secondi ciascuno, fino a cinque riproduzioni delle foto, oppure un “Boomerang”, avanti e indietro.
- Aggiungi musica sotto un MP4 (MP3, M4A, WAV, WMA o FLAC): scegli in che punto della canzone inizia il video, con dissolvenza in entrata e in uscita. La canzone finisce dove finisce il video. Una GIF non ha audio.

Modifica ed esportazione
- Scorri la sequenza, avanza un fotogramma alla volta, rimuovi una foto mossa o trascinala al suo posto. Ctrl+Z la rimette nel progetto. Rimuovere una foto la toglie dal video, mai dal disco.
- Nella pellicola, dei segni indicano dove sembrano mancare delle foto.
- Frequenze fotogrammi esatte, 29,97 compreso, da 0,1 a 120 fotogrammi al secondo.
- Il pannello Output stima le dimensioni del file prima dell'esportazione.
- L'esportazione in MP4 usa il codificatore video della scheda grafica, se il PC ne ha uno. L'esportazione tiene il PC attivo fino alla fine, continua quando riduci la finestra a icona e si ferma entro un secondo quando la annulli.
- Quando l'esportazione è finita, riproduci subito il risultato nell'app o apri la sua cartella.
- Se qualcosa va storto, 10Lapse ti dice cosa è successo e cosa fare.

Progetti
Salva un progetto come file .10lapse, che conserva l'elenco delle foto, il loro ordine e tutte le impostazioni, e riaprilo più tardi o con un doppio clic. La schermata iniziale mostra i tuoi progetti recenti. Se 10Lapse si chiude prima che tu abbia salvato, all'avvio successivo propone di ripristinare le modifiche. Le foto che un progetto non trova restano nel suo file, quindi tornano appena la loro unità è di nuovo collegata.

Privacy fin dalla progettazione
10Lapse funziona offline. Nessun account, nessuna raccolta di dati di utilizzo e nessun accesso alla rete: le tue foto, i tuoi video e i tuoi progetti non lasciano mai il PC.

Pensato per Windows 11
10Lapse richiede Windows 11 su un PC x64 o ARM64. I temi chiaro, scuro e a contrasto seguono Windows, come le animazioni. Tutto funziona da tastiera e le utilità per la lettura dello schermo annunciano ogni controllo e l'avanzamento dell'esportazione.

Le estensioni facoltative di Microsoft Store aggiungono altri formati di foto: le foto HEIC richiedono Estensioni immagini HEIF (gratis) ed Estensioni video HEVC (a pagamento), i file RAW delle fotocamere Estensione immagini Raw (gratis). 10Lapse ti dice quale manca e apre la sua pagina nello Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 251.

```text
Crea un video timelapse dalle centinaia di foto scattate dal treppiede: imposta velocità e proporzioni, attenua lo sfarfallio ed esporta un MP4 fino a 4K o una GIF animata. 10Lapse funziona offline, senza account, e le tue foto non lasciano mai il PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,352. As in English, it is what 3.0 users get with the update, and it leaves
out the rating question.

```text
10Lapse 3.1 aggiunge ciò che serve a un timelapse dopo lo scatto.

- Antisfarfallio: attenua i salti di esposizione tra le foto. Se durante un tramonto hai cambiato tempo di scatto, diaframma o ISO, ogni gradino diventa una rampa graduale.
- Fusione di fotogrammi: ogni fotogramma è la media delle foto vicine, da 2 a 16, come una lunga esposizione, per scie luminose e nuvole fluide.
- Ritaglio animato: passa da un'inquadratura iniziale a una finale, con uno zoom fino a 4×, morbido o lineare.
- GIF animata: esporta una GIF che si ripete all'infinito, in tre misure, fino a 25 fotogrammi al secondo.
- Musica sotto un MP4: scegli dove inizia la canzone, con dissolvenza in entrata e in uscita. Finisce con il video.
- Fermo immagine all'inizio e alla fine, fino a 10 secondi, fino a cinque riproduzioni delle foto, oppure la modalità Boomerang.
- Salva il tuo lavoro: un file di progetto .10lapse si riapre più tardi, la schermata iniziale mostra i progetti recenti e, se 10Lapse si chiude prima del salvataggio, propone di ripristinare le modifiche.
- Ctrl+O ora apre un progetto; “Scegli cartella” è passato a Ctrl+I.
- “Prima” e “Dopo”, nell'anteprima, confrontano le tue foto con e senza antisfarfallio e fusione di fotogrammi.

Come prima, 10Lapse funziona offline e le tue foto non lasciano mai il PC. Richiede Windows 11 su un PC x64 o ARM64.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are 59
to 136 characters each.

```text
Trasforma una cartella di foto in un video timelapse fino a 4K o in una GIF animata
Importa una cartella o le foto scelte, oppure trascinale sulla finestra; le foto si mettono nell'ordine di scatto
Legge JPEG, PNG e TIFF; HEIC e RAW delle fotocamere con le estensioni di Microsoft Store
Velocità in fotogrammi al secondo, in base alla durata del video o in secondi per foto, con numero di foto e durata sempre sullo schermo
Frequenze fotogrammi esatte da 0,1 a 120 fps, 29,97 compreso
Proporzioni: come le foto, 16:9, 9:16, 1:1, 4:5 o 4:3, con “Adatta” o “Riempi”
Anteprima dal vivo fedele all'esportazione, e pellicola con segni dove mancano foto: sposta, rimuovi e annulla
Antisfarfallio: attenua i salti di esposizione e trasforma i gradini di un tramonto in una rampa graduale
Fusione di fotogrammi: la media delle foto vicine, da 2 a 16, per scie luminose e nuvole fluide
Ritaglio animato da un'inquadratura iniziale a una finale, zoom fino a 4×, “Morbido” o “Lineare”
Fermo immagine all'inizio e alla fine, fino a cinque riproduzioni delle foto, oppure modalità Boomerang
Musica sotto un MP4: scegli dove inizia la canzone, con dissolvenza in entrata e in uscita; finisce con il video
Salva i progetti come file .10lapse, riapri quelli recenti dalla schermata iniziale e ripristina le modifiche dopo un arresto anomalo
MP4 in H.264 (o HEVC se il PC lo supporta) a 720p, 1080p, 1440p o 4K, con tre livelli di qualità e la stima delle dimensioni del file
GIF animata che si ripete all'infinito, in tre misure (320, 480 o 640 pixel), fino a 10, 20 o 25 fotogrammi al secondo
Esporta in MP4 con il codificatore video della scheda grafica, se c'è; continua con la finestra ridotta a icona e tiene il PC attivo
Riproduci il video o la GIF finita nell'app o apri la sua cartella
Messaggi chiari che dicono cosa è andato storto e cosa fare
Funziona offline: nessun account, nessun dato di utilizzo, le foto non lasciano mai il PC
Temi chiaro e scuro, temi a contrasto, tasti di scelta rapida, supporto per le utilità per la lettura dello schermo
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are
9 to 20 characters, 15 words in all. "timelapse" and "time lapse" stay: Italian users search the English
words too.

```text
timelapse
time lapse
creare timelapse
video da foto
stop motion
presentazione foto
sequenza di immagini
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. These
items are 31 to 122 characters.

Minimum hardware:

```text
Windows 11 su un PC x64 o ARM64
Facoltativo, per le foto HEIC: Estensioni immagini HEIF (gratis) ed Estensioni video HEVC (a pagamento) da Microsoft Store
Facoltativo, per le foto RAW delle fotocamere: Estensione immagini Raw (gratis) da Microsoft Store
Facoltativo, per i video HEVC: Estensioni video HEVC (a pagamento) e una scheda grafica che codifica in HEVC
```

Recommended hardware:

```text
Una scheda grafica con codificatore video hardware, per un'esportazione più rapida
8 GB di memoria o più per esportare in 4K
```

The notes on what each extension adds, for Sven's answers to reviews (#44), are in `listing.md`; the Italian Store
names are `Estensioni immagini HEIF` (9PMMSR1CGPWG), `Estensioni video HEVC` (9NMZLZ57R3T7) and `Estensione immagini
Raw` (9NCTDW2W1BH8).

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`pwsh -File scripts/store-screenshots.ps1 -Language it` makes the Italian set into `docs/store/screenshots/it/`
(in Git LFS), from the same generated sample photos as the English set.

Upload six, in this order; captions are 56 to 111 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Guarda tutta la sequenza in riproduzione, con numero di foto, velocità e durata del video sempre sullo schermo.` |
| `03-vertical.png` | 1920 x 1080 | `Scegli 9:16 per storie e reel sul telefono, oppure 16:9, 1:1, 4:5 o 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Esporta un MP4 fino a 4K e guarda ogni fotogramma mentre viene elaborato, con il tempo rimanente.` |
| `05-finished.png` | 1920 x 1080 | `Riproduci subito il video finito o apri la sua cartella.` |
| `06-per-photo.png` | 1920 x 1080 | `Crea una presentazione con i secondi per foto, oppure imposta i fotogrammi al secondo o la durata del video.` |
| `01-first-launch.png` | 1920 x 1080 | `Trascina una cartella di foto per iniziare. Le tue foto non lasciano mai il PC.` |

Do not upload 07-settings and 08-narrow, as in English.

## Until 3.0 is public (#7)

Only if the 2.1.1 listing has an Italian page: the same listing-only edit as in English, replacing the "BETA
REALEASE!" line with the line below. This draft: 62 characters.

```text
10Lapse 3.0 è in fase di ricostruzione da zero per Windows 11.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 46.

```text
© 2026 Sven Borden. Tutti i diritti riservati.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, the empty short and voice titles, the sort title Ten Lapse, the empty
additional license terms, the trailer (#43) and the Store logos (#37). The privacy policy and support URLs are on the
Properties page and are the same for every language.
