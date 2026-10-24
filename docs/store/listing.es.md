# 10Lapse 3.1 Store listing (Spanish)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #117): Apps and games, 10Lapse, the 3.1 submission,
Store listings, Spanish (español). One `es` listing serves Spain and Latin America, as the package's `es` resources
do. It is `docs/store/listing.md` translated, with the same fields, limits and order; the notes around the boxes stay
in English for Sven, the boxes are what the Store shows.

Each field below says its limit and this draft's length, counted as Partner Center does (a line break as one). The
limits are the ones `listing.md` checked on Microsoft Learn on 2026-10-20; a limit marked **verify** is not on those
pages. Copy each field from inside its box: the boxes are plain text, without Markdown.

The Spanish follows the app: informal address ("tú"), as Windows 11 writes Spanish; words Spain and Latin America
share ("equipo" for the PC, "teléfono", "en tiempo real"), and "video" without the accent, because the one `es`
listing also serves Mexico, the US and the rest of Latin America (about 90 % of Spanish speakers) and the RAE accepts
"video" in Spain too; the Store names of the extensions exactly as the Mexican Store (market MX) shows them, which
match that spelling (`Extensión de imágenes HEIF`, `Extensiones de video HEVC`, `Extensión de imagen RAW`); the app's
own labels in “…” where the text names one. As in English, the text says only what 3.1 does, and no field has the
Windows 10 sentence. The 3.1 features use the app's Spanish names: Antiparpadeo, Fusión de fotogramas, Recorte y paneo
(Suave, Lineal), Imagen fija al inicio / al final, Bumerán, Reproducciones, GIF animado (Pequeño, Mediano, Grande),
Música, Proyectos recientes, Antes / Después.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,666.

```text
Convierte una carpeta de fotos en un video timelapse fluido, hasta 4K, o en un GIF animado.

Fotografiaste una puesta de sol, una tormenta que se acerca o una obra desde un trípode, una foto cada pocos segundos, y ahora tienes una carpeta con cientos o miles de fotos. 10Lapse las reproduce como un video: suelta la carpeta en la ventana, revisa la vista previa y exporta un MP4 o un GIF.

Cómo funciona
1. Elige una carpeta, selecciona fotos o arrástralas a la ventana. 10Lapse lee JPEG, PNG, TIFF, HEIC y los archivos RAW de la mayoría de las cámaras, y los ordena según el momento en que se tomaron.
2. Fija la velocidad de una de estas tres formas: fotogramas por segundo, la duración que debe tener el video o segundos por foto para una presentación. Las cifras siempre están a la vista, por ejemplo “742 fotos → 24,7 s a 30 fps”.
3. Elige la proporción: la de tus fotos, 16:9 para YouTube y la televisión, 9:16 para historias y reels en el teléfono, 1:1, 4:5 o 4:3. “Ajustar” muestra la foto entera; “Rellenar” recorta los bordes para que no haya franjas.
4. Exporta un MP4 en 720p, 1080p, 1440p o 4K, en H.264, o en HEVC si el equipo puede codificarlo y reproducirlo. O exporta un GIF animado que se repite, en tamaño “Pequeño”, “Mediano” o “Grande” (320, 480 o 640 píxeles en el lado largo), con hasta 10, 20 o 25 fotogramas por segundo. La vista previa dibuja cada fotograma con el mismo código que la exportación, así que el video se ve igual que la vista previa.

El aspecto justo
- “Antiparpadeo” suaviza los saltos de exposición entre fotos. Si cambiaste la velocidad de obturación, la apertura o el ISO durante una puesta de sol, lee esos ajustes en las fotos y convierte cada escalón en una rampa suave, mientras la escena se oscurece como lo hizo.
- “Fusión de fotogramas” hace de cada fotograma el promedio de 2 a 16 fotos cercanas, como una exposición larga: las luces en movimiento dejan estelas y las nubes se mueven con suavidad.
- “Recorte y paneo” pasa de un encuadre inicial a uno final, con un zoom de hasta 4×, “Suave” o “Lineal”. Arrastra el encuadre en la vista previa para colocarlo.
- “Antes” y “Después”, en la vista previa, muestran tus fotos con y sin antiparpadeo y fusión de fotogramas.
- Una imagen fija al inicio y al final del video, de hasta 10 segundos cada una, hasta cinco reproducciones de las fotos, o un “Bumerán”, de ida y vuelta.
- Agrega música bajo un MP4 (MP3, M4A, WAV, WMA o FLAC): elige en qué punto de la canción empieza el video, con fundido de entrada y de salida. La canción se corta donde termina el video; si es más corta, el resto queda en silencio. Un GIF no tiene sonido.

Edición y exportación
- Recorre la secuencia, avanza fotograma a fotograma, y quita una foto borrosa (Ctrl+Z la devuelve) o arrastra otra a su sitio. Quitar una foto la saca del video, nunca de tu disco.
- Las marcas de la tira de fotogramas muestran dónde parece que faltan fotos.
- Las velocidades de fotogramas son exactas, 29,97 incluida, de 0,1 a 120 fotogramas por segundo.
- El panel Salida calcula el tamaño del archivo antes de exportar.
- La exportación a MP4 usa el codificador de video de la tarjeta gráfica si el equipo tiene uno. La exportación mantiene el equipo activo hasta terminar, sigue adelante si minimizas la ventana y se detiene en menos de un segundo si la cancelas.
- Cuando la exportación termina, puedes reproducir el resultado ahí mismo o abrir su carpeta.
- Si algo falla, 10Lapse te dice qué pasó y qué hacer a continuación.

Proyectos
Guarda un proyecto como archivo .10lapse, que conserva la lista de fotos, su orden y todos los ajustes, y vuelve a abrirlo más tarde o con un doble clic. La pantalla de inicio muestra tus proyectos recientes. Si 10Lapse se cierra antes de que guardes, te ofrece restaurar tus cambios la próxima vez que se inicia. Las fotos que un proyecto no encuentra siguen en su archivo, así que vuelven en cuanto su unidad se conecta de nuevo.

Privacidad desde el diseño
10Lapse funciona sin conexión. No hay cuenta, ni análisis, ni acceso a la red: tus fotos, videos y proyectos nunca salen de tu equipo.

Hecho para Windows 11
10Lapse necesita Windows 11 en un equipo x64 o ARM64. Los temas claro, oscuro y de contraste siguen la configuración de Windows, igual que las animaciones. Todo funciona con el teclado, y los lectores de pantalla anuncian cada control y el progreso de la exportación.

Las extensiones opcionales de Microsoft Store agregan más formatos de foto. Las fotos HEIC necesitan dos: Extensión de imágenes HEIF (gratis) y Extensiones de video HEVC (de pago). Los archivos RAW de cámara necesitan una: Extensión de imagen RAW (gratis). 10Lapse indica cuál falta y enlaza a su página de Microsoft Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 259.

```text
Crea un video timelapse con los cientos de fotos de una sesión con trípode: fija la velocidad y la proporción, suaviza el parpadeo y exporta un MP4 de hasta 4K o un GIF animado. 10Lapse funciona sin conexión y sin cuenta, y tus fotos nunca salen de tu equipo.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,348 / 1,500. As in English, it is what 3.0 users get with the update, and it leaves
out the rating question.

```text
10Lapse 3.1 trae lo que un timelapse necesita después de la sesión.

- Antiparpadeo: suaviza los saltos de exposición entre fotos. Si cambiaste la velocidad de obturación, la apertura o el ISO durante una puesta de sol, cada escalón se vuelve una rampa suave.
- Fusión de fotogramas: cada fotograma es el promedio de 2 a 16 fotos cercanas, como una exposición larga, para estelas de luz y nubes suaves.
- Recorte y paneo: pasa de un encuadre inicial a uno final, con un zoom de hasta 4×, suave o lineal.
- GIF animado: exporta un GIF que se repite, en tres tamaños, con hasta 25 fotogramas por segundo.
- Música bajo un MP4: elige dónde empieza la canción, con fundido de entrada y de salida. Se corta donde termina el video.
- Imagen fija al inicio y al final, de hasta 10 segundos, hasta cinco reproducciones de las fotos, o el modo Bumerán.
- Guarda tu trabajo: un archivo de proyecto .10lapse se vuelve a abrir más tarde, la pantalla de inicio muestra los proyectos recientes y, si 10Lapse se cierra antes de que guardes, te ofrece restaurar tus cambios.
- Ctrl+O ahora abre un proyecto; “Elegir carpeta” pasó a Ctrl+I y “Elegir fotos”, a Ctrl+Mayús+I.
- “Antes” y “Después”, en la vista previa, comparan tus fotos con y sin antiparpadeo y fusión de fotogramas.

Como antes, 10Lapse funciona sin conexión y tus fotos nunca salen de tu equipo. Necesita Windows 11 en un equipo x64 o ARM64.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are
62 to 139 characters each.

```text
Convierte una carpeta de fotos en un video timelapse de hasta 4K o en un GIF animado
Importa una carpeta o las fotos que elijas, o arrástralas a la ventana; se ordenan según el momento en que se tomaron
Lee JPEG, PNG y TIFF; HEIC y RAW de cámara con extensiones de Microsoft Store
Velocidad en fotogramas por segundo, por duración del video o en segundos por foto, con el número de fotos y la duración siempre a la vista
Velocidades de fotogramas exactas de 0,1 a 120 fps, 29,97 incluida
Proporciones: la de las fotos, 16:9, 9:16, 1:1, 4:5 o 4:3, con “Ajustar” o “Rellenar”
Vista previa en tiempo real igual a la exportación, y tira de fotogramas con marcas donde faltan fotos: arrastrar, quitar y deshacer
Antiparpadeo: suaviza los saltos de exposición y convierte los escalones de una puesta de sol en una rampa suave
Fusión de fotogramas: promedio de 2 a 16 fotos cercanas para estelas de luz y nubes suaves
Recorte y paneo de un encuadre inicial a uno final, zoom de hasta 4×, “Suave” o “Lineal”
Imagen fija al inicio y al final, hasta cinco reproducciones de las fotos, o modo Bumerán
Música bajo un MP4: elige dónde empieza la canción, con fundido de entrada y de salida; se corta donde termina el video
Guarda proyectos como archivos .10lapse, abre los recientes desde la pantalla de inicio y restaura los cambios tras un cierre inesperado
MP4 en H.264 (o HEVC si el equipo lo admite) en 720p, 1080p, 1440p o 4K, con tres niveles de calidad y tamaño estimado antes de exportar
GIF animado que se repite, en tres tamaños, con hasta 10, 20 o 25 fotogramas por segundo
Exporta MP4 con el codificador de video de la tarjeta gráfica si hay uno; sigue con la ventana minimizada y mantiene el equipo activo
Reproduce el video o el GIF terminado en la aplicación, o abre su carpeta
Mensajes claros que dicen qué falló y qué hacer a continuación
Funciona sin conexión: sin cuenta, sin análisis, las fotos nunca salen de tu equipo
Temas claro y oscuro, temas de contraste, métodos abreviados de teclado, compatibilidad con lectores de pantalla
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are
9 to 21 characters, 16 words in all. "timelapse" and "time lapse" stay: Spanish speakers search the English word too, and "cámara rápida" is the
Spanish name of the technique; "secuencia de imágenes" is what a photographer calls the folder. "fotos a video" is spelled as the rest of the listing, and as most people type it.

```text
secuencia de imágenes
timelapse
time lapse
fotos a video
cámara rápida
stop motion
presentación de fotos
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. These
items are 35 to 119 characters.

Minimum hardware:

```text
Windows 11 en un equipo x64 o ARM64
Opcional, para fotos HEIC: Extensión de imágenes HEIF (gratis) y Extensiones de video HEVC (de pago) de Microsoft Store
Opcional, para fotos RAW de cámara: Extensión de imagen RAW (gratis) de Microsoft Store
Opcional, para video HEVC: Extensiones de video HEVC (de pago) y una tarjeta gráfica que codifique HEVC
```

Recommended hardware:

```text
Una tarjeta gráfica con codificador de video por hardware, para exportar más rápido
8 GB de memoria o más para exportar en 4K
```

The notes on what each extension adds, for Sven's answers to reviews (#44), are in `listing.md`. The Store names
used here are the Mexican Store's (market MX, es-MX, 2026-10-20): `Extensión de imágenes HEIF` (9PMMSR1CGPWG),
`Extensiones de video HEVC` (9NMZLZ57R3T7) and `Extensión de imagen RAW` (9NCTDW2W1BH8). The Spanish Store (market
ES, es-ES) writes two of them differently: `Extensiones de vídeo HEVC` and `Extensión de imagen de formato raw`; a
search for either name finds the product in both. AV1 (not named in this listing) is `Extensión de vídeo AV1` in every
market, and the app keeps that exact spelling.

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`pwsh -File scripts/store-screenshots.ps1 -Language es` makes the Spanish set into `docs/store/screenshots/es/`
(in Git LFS), from the same generated sample photos as the English set.

Upload six, in this order; captions are 59 to 122 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Mira cómo se reproduce toda la secuencia, con el número de fotos, la velocidad y la duración del video siempre a la vista.` |
| `03-vertical.png` | 1920 x 1080 | `Encuadra el video en 9:16 para historias y reels en el teléfono, o en 16:9, 1:1, 4:5 o 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Exporta un MP4 de hasta 4K y mira cada fotograma mientras se genera, con el tiempo restante.` |
| `05-finished.png` | 1920 x 1080 | `Reproduce el video terminado al instante o abre su carpeta.` |
| `06-per-photo.png` | 1920 x 1080 | `Crea una presentación con segundos por foto, o fija los fotogramas por segundo o la duración del video.` |
| `01-first-launch.png` | 1920 x 1080 | `Suelta una carpeta de fotos para empezar. Tus fotos nunca salen de tu equipo.` |

Do not upload 07-settings and 08-narrow, as in English.

## Until 3.0 is public (#7)

Only if the 2.1.1 listing has a Spanish page: the same listing-only edit as in English, replacing the "BETA REALEASE!"
line with the line below. This draft: 58 characters.

```text
10Lapse 3.0 se está rehaciendo desde cero para Windows 11.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 50.

```text
© 2026 Sven Borden. Todos los derechos reservados.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, the empty short and voice titles, the sort title Ten Lapse, the empty
additional license terms, the trailer (#43) and the Store logos (#37). The privacy policy and support URLs are on the
Properties page and are the same for every language.
