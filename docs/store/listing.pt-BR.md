# 10Lapse 3.1 Store listing (Portuguese, Brazil)

The text Sven pastes into Partner Center for the 3.1 update (#141; 3.0 was #42, #119): Apps and games, 10Lapse, the 3.1 submission,
Store listings, Portuguese (Brazil). The package declares Brazilian Portuguese from
`src/TenLapse.App/Strings/pt-BR/Resources.resw`, so Partner Center offers a pt-BR listing. Portugal: Sven decides in
Partner Center whether to add a Portuguese (Portugal) listing that reuses this text; without one, customers in Portugal
may see the default English listing. The app itself shows its pt-BR text to pt-PT users (#113).

This file has the structure of `docs/store/listing.md`, with the same fields, limits and rules; read that file for
where each limit comes from. Only the text inside the boxes is Portuguese; the notes stay in English for Sven. Lengths
count characters as Partner Center does, a line break as one. Copy each field from inside its box.

The extension names are the Microsoft Store's own Brazilian titles (catalog, market BR, pt-BR, checked 2026-10-20),
quoted so that no article or preposition has to agree with them, with the app's price labels "(grátis)" and
"(compra necessária)", which agree with no name either. Form of address: você, with the imperative, as Windows 11
writes it.

As in English, the text says only what 3.1 does, and no field has the Windows 10 sentence. The 3.1 features use the
app's Brazilian names: Anticintilação, Fusão de quadros, Corte e movimento (Suave, Linear), Imagem congelada no início /
no fim, Bumerangue, Reproduções, GIF animado (Pequeno, Médio, Grande), Música (Fade de entrada / de saída), Projetos
recentes, Antes / Depois.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 4,562.

```text
Transforme uma pasta de fotos em um vídeo timelapse suave, em até 4K, ou em um GIF animado.

Você fotografou um pôr do sol, uma tempestade chegando ou uma obra com a câmera no tripé, uma foto a cada poucos segundos, e agora tem uma pasta com centenas ou milhares de fotos. O 10Lapse transforma essas fotos em vídeo: solte a pasta na janela, confira a visualização e exporte um MP4 ou um GIF.

Como funciona
1. Escolha uma pasta, selecione fotos ou arraste-as para a janela. O 10Lapse lê JPEG, PNG, TIFF, HEIC e os arquivos RAW da maioria das câmeras, e coloca as fotos na ordem em que foram tiradas.
2. Defina a velocidade de uma destas três formas: quadros por segundo, a duração que o vídeo deve ter ou segundos por foto, para uma apresentação de slides. Os números ficam sempre na tela, por exemplo "742 fotos → 24,7 s a 30 fps".
3. Escolha a proporção: igual às suas fotos, 16:9 para o YouTube e a TV, 9:16 para stories e reels no celular, 1:1, 4:5 ou 4:3. Com Ajustar, a foto aparece inteira; com Preencher, as bordas são cortadas e não sobram faixas.
4. Exporte um MP4 em 720p, 1080p, 1440p ou 4K, em H.264, ou em HEVC quando o PC consegue codificar e reproduzir esse formato. Ou exporte um GIF animado que se repete sem parar, no tamanho Pequeno, Médio ou Grande (320, 480 ou 640 pixels no lado maior), com até 10, 20 ou 25 quadros por segundo. A visualização desenha cada quadro com o mesmo código da exportação, então o vídeo fica igual ao que você viu.

O visual certo
- Anticintilação suaviza os saltos de exposição entre as fotos. Se você mudou a velocidade do obturador, a abertura ou o ISO durante um pôr do sol, ela lê essas configurações nas fotos e transforma cada degrau em uma rampa suave, enquanto a cena escurece como de fato escureceu.
- Fusão de quadros faz de cada quadro a média de 2 a 16 fotos próximas, como uma longa exposição: as luzes em movimento deixam rastros e as nuvens se movem com suavidade.
- Corte e movimento vai de um enquadramento inicial a um final, com zoom de até 4×, Suave ou Linear. Arraste o enquadramento na visualização para posicioná-lo.
- Antes e Depois, na visualização, mostram suas fotos com e sem anticintilação e fusão de quadros.
- Uma imagem congelada no início e no fim do vídeo, de até 10 segundos cada, até cinco reproduções das fotos ou um Bumerangue, de ida e volta.
- Adicione música sob um MP4 (MP3, M4A, WAV, WMA ou FLAC): escolha em que ponto da música o vídeo começa, com fade de entrada e de saída. A música é cortada onde o vídeo termina; se for mais curta, o resto fica em silêncio. Um GIF não tem som.

Edição e exportação
- Percorra a sequência, avance quadro a quadro e remova uma foto tremida ou arraste uma foto para o lugar certo. Ctrl+Z devolve a foto ao projeto. Remover uma foto a tira do vídeo, nunca do seu disco.
- Marcadores na tira de filme mostram onde parecem faltar fotos.
- As taxas de quadros são exatas, inclusive 29,97, de 0,1 a 120 quadros por segundo.
- O painel Saída estima o tamanho do arquivo antes de você exportar.
- A exportação em MP4 usa o codificador de vídeo da placa de vídeo quando o PC tem um. A exportação mantém o PC ativo até terminar, continua quando você minimiza a janela e para em menos de um segundo quando você cancela.
- Quando a exportação terminar, reproduza o resultado ali mesmo ou abra a pasta dele.
- Se algo der errado, o 10Lapse diz o que aconteceu e o que fazer em seguida.

Projetos
Salve um projeto como arquivo .10lapse, que guarda a lista de fotos, a ordem delas e todas as configurações, e abra-o de novo depois ou com um clique duplo. A tela inicial mostra seus projetos recentes. Se o 10Lapse fechar inesperadamente antes de você salvar, ele oferece restaurar suas alterações na próxima vez que for aberto. As fotos que um projeto não encontra continuam no arquivo dele e voltam assim que a unidade delas for conectada de novo.

Privacidade em primeiro lugar
O 10Lapse funciona offline. Não há conta, nem coleta de dados de uso, nem acesso à rede: suas fotos, vídeos e projetos nunca saem do seu PC.

Feito para o Windows 11
O 10Lapse requer o Windows 11 em um PC x64 ou ARM64. Os temas claro, escuro e de alto contraste seguem o Windows, assim como as animações. Tudo funciona pelo teclado, e os leitores de tela anunciam cada controle e o progresso da exportação.

Extensões opcionais da Microsoft Store adicionam mais formatos de foto: fotos HEIC precisam de "Extensão da Imagem HEIF" (grátis) e "Extensões de Vídeo HEVC" (compra necessária), e arquivos RAW de câmera precisam de "Extensão de Imagem Raw" (grátis). O 10Lapse indica qual extensão está faltando e abre a página dela na Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. This draft: 248.

```text
Crie um vídeo timelapse com as centenas de fotos de uma sessão no tripé: defina a velocidade e a proporção, suavize a cintilação e exporte um MP4 em até 4K ou um GIF animado. O 10Lapse funciona offline, sem conta, e suas fotos nunca saem do seu PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,310. Like the English one, it is what 3.0 users get, and it leaves out the rating question.

```text
O 10Lapse 3.1 traz o que um timelapse precisa depois da sessão de fotos.

- Anticintilação: suaviza os saltos de exposição entre as fotos. Se você mudou a velocidade do obturador, a abertura ou o ISO durante um pôr do sol, ela lê essas configurações nas fotos e transforma cada degrau em uma rampa suave.
- Fusão de quadros: cada quadro é a média de 2 a 16 fotos próximas, como uma longa exposição, para rastros de luz e nuvens suaves.
- Corte e movimento: vá de um enquadramento inicial a um final, com zoom de até 4×, suave ou linear.
- GIF animado: exporte um GIF que se repete sem parar, em três tamanhos, com até 25 quadros por segundo.
- Música sob um MP4: escolha onde a música começa, com fade de entrada e de saída. Ela é cortada onde o vídeo termina.
- Imagem congelada no início e no fim, de até 10 segundos, até cinco reproduções das fotos ou o modo Bumerangue.
- Salve seu trabalho: um arquivo de projeto .10lapse abre de novo depois, a tela inicial mostra os projetos recentes e, se o 10Lapse fechar inesperadamente antes de você salvar, ele oferece restaurar suas alterações.
- Ctrl+O agora abre um projeto; Escolher pasta passou para Ctrl+I e Escolher fotos, para Ctrl+Shift+I.
- Antes e Depois, na visualização, comparam suas fotos com e sem anticintilação e fusão de quadros.

Como antes, o 10Lapse funciona offline e suas fotos nunca saem do seu PC. Ele requer o Windows 11 em um PC x64 ou ARM64.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 20 are
57 to 130 characters each.

```text
Transforma uma pasta de fotos em um vídeo timelapse em até 4K ou em um GIF animado
Importe uma pasta ou fotos escolhidas, ou arraste as fotos para a janela; elas entram na ordem em que foram tiradas
Lê JPEG, PNG e TIFF; HEIC e RAW de câmera com extensões da Microsoft Store
Velocidade em quadros por segundo, pela duração do vídeo ou em segundos por foto, com o número de fotos e a duração sempre na tela
Taxas de quadros exatas de 0,1 a 120 fps, inclusive 29,97
Proporções: igual às fotos, 16:9, 9:16, 1:1, 4:5 ou 4:3, com Ajustar ou Preencher
Visualização ao vivo igual ao vídeo exportado e tira de filme com marcadores de fotos faltando: arraste, remova e desfaça
Anticintilação: suaviza os saltos de exposição e, com as configurações de exposição das fotos, transforma os degraus de um pôr do sol em uma rampa suave
Fusão de quadros: a média de 2 a 16 fotos próximas, para rastros de luz e nuvens suaves
Corte e movimento de um enquadramento inicial a um final, zoom de até 4×, Suave ou Linear
Imagem congelada no início e no fim, até cinco reproduções das fotos ou modo Bumerangue
Música sob um MP4: escolha onde a música começa, com fade de entrada e de saída; ela é cortada onde o vídeo termina
Salve projetos como arquivos .10lapse, abra os recentes na tela inicial e restaure as alterações depois de um travamento
MP4 em H.264 (ou HEVC quando o PC é compatível) em 720p, 1080p, 1440p ou 4K, com três níveis de qualidade e estimativa do tamanho antes de exportar
GIF animado que se repete sem parar, em três tamanhos, com até 10, 20 ou 25 quadros por segundo
Exporta MP4 com o codificador da placa de vídeo quando há um; continua com a janela minimizada e mantém o PC ativo
Reproduza o vídeo ou o GIF pronto no aplicativo ou abra a pasta dele
Mensagens claras que dizem o que deu errado e o que fazer em seguida
Funciona offline: sem conta, sem coleta de dados, e as fotos nunca saem do seu PC
Temas claro e escuro, alto contraste, atalhos de teclado e suporte a leitores de tela
```

## Search terms

At most 7 terms (Store Policies: no more than seven unique terms or phrases, relevant to the app, no other product's
name). **Verify** in the form: it has allowed 30 characters per term and 21 words across all terms. These 7 are
9 to 22 characters, 16 words in all. Brazilian users search the English words too ("timelapse",
"stop motion", "slideshow"); "apresentação de slides" is the Portuguese name Windows and Office use for a slideshow.

```text
timelapse
time lapse
fotos para vídeo
stop motion
slideshow
apresentação de slides
criar vídeo com fotos
```

## Additional system requirements

Up to 11 items each for minimum and recommended hardware, 200 characters each; the Store adds the bullets. These
items are 32 to 128 characters.

Minimum hardware:

```text
Windows 11 em um PC x64 ou ARM64
Opcional, para fotos HEIC: "Extensão da Imagem HEIF" (grátis) e "Extensões de Vídeo HEVC" (compra necessária) da Microsoft Store
Opcional, para fotos RAW de câmera: "Extensão de Imagem Raw" (grátis) da Microsoft Store
Opcional, para vídeo HEVC: "Extensões de Vídeo HEVC" (compra necessária) e uma placa de vídeo que codifique HEVC
```

Recommended hardware:

```text
Uma placa de vídeo com codificador de vídeo por hardware, para exportar mais rápido
8 GB de memória ou mais para exportar em 4K
```

What each extension adds, for Sven's answers to Brazilian reviews (#44), not for a field:

- "Extensão da Imagem HEIF" (grátis) e "Extensões de Vídeo HEVC" (compra necessária): as fotos HEIC e HEIF de celulares.
  Sem elas, essas fotos são ignoradas e uma mensagem cita a extensão que falta, com um botão para a página dela na Store.
- "Extensão de Imagem Raw" (grátis): os arquivos RAW da maioria das câmeras (CR2, CR3, NEF, ARW, RAF, ORF, RW2 e
  outros). Arquivos DNG abrem sem ela. Um RAW com um JPEG gêmeo de mesmo nome é importado uma vez só, como JPEG.
- "Extensões de Vídeo HEVC" (compra necessária): a exportação em HEVC (H.265). O 10Lapse só oferece HEVC quando o PC
  consegue codificar e reproduzir esse formato; caso contrário, a lista Formato tem apenas H.264.

## Screenshots

Desktop: PNG, 1366 x 768 or larger, up to 10, captions 200 characters each; the rules are those of `listing.md`.
`pwsh -File scripts/store-screenshots.ps1 -Language pt-BR` makes the Brazilian set into
`docs/store/screenshots/pt-BR/` (in Git LFS) from the same generated sample photos as the English set, so the app
shows its Portuguese text.

Upload six, in the English order; captions are 54 to 112 characters.

| File | Size | Caption |
|---|---|---|
| `02-editing.png` | 1920 x 1080 | `Veja a sequência inteira em reprodução, com o número de fotos, a velocidade e a duração do vídeo sempre na tela.` |
| `03-vertical.png` | 1920 x 1080 | `Enquadre em 9:16 para stories e reels no celular, ou em 16:9, 1:1, 4:5 ou 4:3.` |
| `04-exporting.png` | 1920 x 1080 | `Exporte um MP4 em até 4K e acompanhe a renderização de cada quadro, com o tempo restante.` |
| `05-finished.png` | 1920 x 1080 | `Reproduza o vídeo pronto na hora ou abra a pasta dele.` |
| `06-per-photo.png` | 1920 x 1080 | `Crie uma apresentação de slides com segundos por foto, ou defina quadros por segundo ou a duração do vídeo.` |
| `01-first-launch.png` | 1920 x 1080 | `Solte uma pasta de fotos para começar. Suas fotos nunca saem do seu PC.` |

As in English, do not upload 07-settings and 08-narrow.

## Until 3.0 is public (#7)

Only if the 2.1.1 listing has a Portuguese description: the same listing-only edit as in `listing.md`, its last line
replaced with the line below. This draft: 59 characters.

```text
O 10Lapse 3.0 está sendo refeito do zero para o Windows 11.
```

## Copyright and trademark info

**Verify** the limit (it has been 200 characters). This draft: 49.

```text
© 2026 Sven Borden. Todos os direitos reservados.
```

## Fields to leave as they are

As in `listing.md`: the product name 10Lapse, empty short and voice titles, the sort title Sven chooses, no additional
license terms; trailers and logos belong to #43 and #37. The privacy policy and support URLs are set once, on the
Properties page, for every language.
