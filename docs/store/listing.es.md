# 10Lapse 3.0 Store listing (Spanish)

The text Sven pastes into Partner Center for the 3.0 update (#117): Apps and games, 10Lapse, the 3.0 submission,
Store listings, Spanish (español). One `es` listing serves Spain and Latin America, as the package's `es` resources
do. It is `docs/store/listing.md` translated, with the same fields, limits and order; the notes around the boxes stay
in English for Sven, the boxes are what the Store shows.

Each field below says its limit and this draft's length, counted as Partner Center does (a line break as one). The
limits are the ones `listing.md` checked on Microsoft Learn on 2026-10-20; a limit marked **verify** is not on those
pages. Copy each field from inside its box: the boxes are plain text, without Markdown.

The Spanish follows the app: informal address ("tú"), as Windows 11 writes Spanish; words Spain and Latin America
share ("equipo" for the PC, "teléfono", "en tiempo real"), and "vídeo" as Windows and the Store write it in Spanish;
the Store names of the extensions exactly as the Spanish Store (market ES) shows them (`Extensión de imágenes HEIF`,
`Extensiones de vídeo HEVC`, `Extensión de imagen de formato raw`); the app's own labels in “…” where the text names
one. As in English, the text says only what 3.0 does.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 2,947.

```text
Convierte una carpeta de fotos en un vídeo timelapse fluido, hasta 4K.

Fotografiaste una puesta de sol, una tormenta que se acerca o una obra desde un trípode, una foto cada pocos segundos, y ahora tienes una carpeta con cientos o miles de fotos. 10Lapse las reproduce como un vídeo: suelta la carpeta en la ventana, revisa la vista previa y exporta un MP4.

Cómo funciona
1. Elige una carpeta, selecciona fotos o arrástralas a la ventana. 10Lapse lee JPEG, PNG, TIFF, HEIC y los archivos RAW de la mayoría de las cámaras, y los ordena según el momento en que se tomaron.
2. Fija la velocidad de una de estas tres formas: fotogramas por segundo, la duración que debe tener el vídeo o segundos por foto para una presentación. Las cifras siempre están a la vista, por ejemplo “742 fotos → 24,7 s a 30 fps”.
3. Elige la proporción: la de tus fotos, 16:9 para YouTube y la televisión, 9:16 para historias y reels en el teléfono, 1:1, 4:5 o 4:3. “Ajustar” muestra la foto entera; “Rellenar” recorta los bordes para que no haya franjas.
4. Exporta un MP4 en 720p, 1080p, 1440p o 4K, en H.264, o en HEVC si el equipo puede codificarlo y reproducirlo. La vista previa dibuja cada fotograma con el mismo código que la exportación, así que el vídeo se ve igual que la vista previa.

Edición y exportación
- Recorre la secuencia, avanza fotograma a fotograma, y quita una foto movida o arrástrala a su sitio. Ctrl+Z la restaura. Quitar una foto la saca del vídeo, nunca de tu disco.
- Las marcas de la tira de fotogramas muestran dónde parece que faltan fotos.
- Las velocidades de fotogramas son exactas, 29,97 incluida, de 0,1 a 120 fotogramas por segundo.
- El panel Salida calcula el tamaño del archivo antes de exportar.
- La exportación usa el codificador de vídeo de la tarjeta gráfica si el equipo tiene uno. Mantiene el equipo activo hasta que el vídeo está listo, sigue adelante si minimizas la ventana y se detiene en menos de un segundo si la cancelas.
- Cuando el vídeo está listo, puedes reproducirlo ahí mismo o abrir su carpeta.
- Si algo falla, 10Lapse te dice qué pasó y qué hacer a continuación.

Privado por diseño
10Lapse funciona sin conexión. No hay cuenta, ni análisis, ni acceso a la red: tus fotos y vídeos nunca salen de tu equipo.

Hecho para Windows 11
10Lapse 3.0 necesita Windows 11 en un equipo x64 o ARM64; en Windows 10, Microsoft Store ofrece en su lugar la versión anterior, 10Lapse 2.1.1. Los temas claro, oscuro y de contraste siguen a Windows, igual que las animaciones. Todo funciona con el teclado, y los lectores de pantalla anuncian cada control y el progreso de la exportación.

Las extensiones opcionales de Microsoft Store agregan más formatos de foto. Las fotos HEIC necesitan dos: Extensión de imágenes HEIF (gratis) y Extensiones de vídeo HEVC (de pago). Los archivos RAW de cámara necesitan una: Extensión de imagen de formato raw (gratis). 10Lapse indica cuál falta y enlaza a su página de Microsoft Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. It shows above the
description, so it says the same thing in other words. This draft: 248.

```text
Crea un vídeo timelapse con los cientos de fotos de una sesión con trípode: fija la velocidad y la proporción, míralo en la vista previa y exporta un MP4 de hasta 4K. 10Lapse funciona sin conexión y sin cuenta, y tus fotos nunca salen de tu equipo.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,481 / 1,500. It names what the reviews of 2.1.1 reported (#44) and says only what 3.0
does; it does not claim a crash rate.

```text
10Lapse 3.0 está rehecho desde cero para Windows 11.

- Reescrito por completo: el código de los cierres inesperados de la 2.1.1 ya no existe. Si algo falla, 10Lapse te dice qué pasó y qué hacer a continuación.
- Importa fotos HEIC y .jpeg. HEIC necesita dos extensiones de Microsoft Store, Extensión de imágenes HEIF (gratis) y Extensiones de vídeo HEVC (de pago); 10Lapse te dice cuál falta.
- Exporta hasta 4K, en H.264, o en HEVC si el equipo lo admite, con el progreso, el tiempo restante y un botón “Cancelar exportación” que la detiene en menos de un segundo.
- Sigue trabajando si cambias de ventana o la minimizas: la exportación continúa, con el progreso en la barra de tareas y una notificación cuando el vídeo está listo.
- Una nueva velocidad “Por foto” para presentaciones, junto a fotogramas por segundo y duración del vídeo.
- Vista previa en tiempo real y tira de fotogramas: recorre, avanza, quita o reordena fotos, y deshaz los cambios.
- Proporciones para televisores, teléfonos y redes sociales: 16:9, 9:16, 1:1, 4:5, 4:3 o la de tus fotos.
- Recuerda la velocidad, el encuadre y la salida que usaste por última vez para el siguiente proyecto.
- Temas claro y oscuro, métodos abreviados de teclado, compatibilidad con lectores de pantalla y temas de contraste.
- Privado: sin cuenta y ahora sin ningún acceso a la red; tus fotos nunca salen de tu equipo.

3.0 necesita Windows 11 en un equipo x64 o ARM64. En Windows 10, Microsoft Store mantiene 10Lapse 2.1.1.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 19 are
44 to 112 characters each.

```text
Convierte una carpeta de fotos en un vídeo timelapse, hasta 4K
Importa una carpeta o las fotos que elijas, o arrástralas a la ventana
Lee JPEG, PNG y TIFF; HEIC y RAW de cámara con extensiones de Microsoft Store
Ordena las fotos según el momento en que se tomaron; arrastra una miniatura para moverla
Velocidad en fotogramas por segundo, por duración del vídeo o en segundos por foto
Velocidades de fotogramas exactas de 0,1 a 120 fps, 29,97 incluida
Número de fotos, velocidad y duración del vídeo siempre a la vista
Proporciones: la de las fotos, 16:9, 9:16, 1:1, 4:5 o 4:3, con “Ajustar” o “Rellenar”
Vista previa en tiempo real igual al vídeo exportado
Tira de fotogramas con miniaturas, marcas donde faltan fotos, quitar y deshacer
MP4 en H.264 (o HEVC si el equipo lo admite) en 720p, 1080p, 1440p o 4K, con tres niveles de calidad
Tamaño de archivo estimado antes de exportar
Exporta con el codificador de vídeo de la tarjeta gráfica si el equipo tiene uno
Sigue exportando con la ventana minimizada y mantiene el equipo activo hasta terminar
Reproduce el vídeo terminado en la aplicación o abre su carpeta
Mensajes claros que dicen qué falló y qué hacer a continuación
Funciona sin conexión: sin cuenta, sin análisis, las fotos nunca salen de tu equipo
Temas claro y oscuro, temas de contraste, métodos abreviados de teclado, compatibilidad con lectores de pantalla
Recuerda la velocidad, el encuadre y la salida que usaste por última vez
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are
9 to 21 characters, 15 words in all. "timelapse" and "time lapse" stay: Spanish speakers search the English word too, and "cámara rápida" is the
Spanish name of the technique. "fotos a video" has no accent because that is how most people type it.

```text
crear timelapse
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
Opcional, para fotos HEIC: Extensión de imágenes HEIF (gratis) y Extensiones de vídeo HEVC (de pago) de Microsoft Store
Opcional, para fotos RAW de cámara: Extensión de imagen de formato raw (gratis) de Microsoft Store
Opcional, para vídeo HEVC: Extensiones de vídeo HEVC (de pago) y una tarjeta gráfica que codifique HEVC
```

Recommended hardware:

```text
Una tarjeta gráfica con codificador de vídeo por hardware, para exportar más rápido
8 GB de memoria o más para exportar en 4K
```

The notes on what each extension adds, for Sven's answers to reviews (#44), are in `listing.md`. The Spanish Store
names (market ES, es-ES, 2026-10-20) are `Extensión de imágenes HEIF` (9PMMSR1CGPWG), `Extensiones de vídeo HEVC`
(9NMZLZ57R3T7) and `Extensión de imagen de formato raw` (9NCTDW2W1BH8). The Mexican Store (market MX, es-MX) writes
two of them differently: `Extensiones de video HEVC` and `Extensión de imagen RAW`; a search for either name finds the
product in both.

## Screenshots

Desktop: PNG, 1366 x 768 or larger in landscape or portrait, up to 3840 x 2160, under 50 MB each, up to 10. Captions
200 characters each. Keep key content in the top two thirds (the Store may lay text over the bottom third).
`pwsh -File scripts/store-screenshots.ps1 -Language es` makes the Spanish set into `dist/store-screenshots/es/`
(git-ignored), from the same generated sample photos as the English set.

Upload six, in this order; captions are 59 to 122 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Mira cómo se reproduce toda la secuencia, con el número de fotos, la velocidad y la duración del vídeo siempre a la vista.` |
| `03-vertical.png` | 1920 x 1080 | `Encuádralo en 9:16 para historias y reels en el teléfono, o en 16:9, 1:1, 4:5 o 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Exporta un MP4 de hasta 4K y mira cada fotograma mientras se genera, con el tiempo restante.` |
| `05-finished.png` | 1920 x 1080 | `Reproduce el vídeo terminado al instante o abre su carpeta.` |
| `06-per-photo.png` | 1920 x 1080 | `Crea una presentación con segundos por foto, o fija los fotogramas por segundo o la duración del vídeo.` |
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
