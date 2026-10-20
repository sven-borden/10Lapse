# 10Lapse 3.0 Store listing (Portuguese, Brazil)

The text Sven pastes into Partner Center for the 3.0 update (#42, #119): Apps and games, 10Lapse, the 3.0 submission,
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

As in English, the text says only what 3.0 does.

## Description

Limit 10,000 characters, plain text (no HTML, no URLs). This draft: 2,902.

```text
Transforme uma pasta de fotos em um vídeo timelapse suave, em até 4K.

Você fotografou um pôr do sol, uma tempestade chegando ou uma obra com a câmera no tripé, uma foto a cada poucos segundos, e agora tem uma pasta com centenas ou milhares de fotos. O 10Lapse transforma essas fotos em vídeo: solte a pasta na janela, confira a visualização e exporte um MP4.

Como funciona
1. Escolha uma pasta, selecione fotos ou arraste-as para a janela. O 10Lapse lê JPEG, PNG, TIFF, HEIC e os arquivos RAW da maioria das câmeras, e coloca as fotos na ordem em que foram tiradas.
2. Defina a velocidade de uma destas três formas: quadros por segundo, a duração que o vídeo deve ter ou segundos por foto, para uma apresentação de slides. Os números ficam sempre na tela, por exemplo "742 fotos → 24,7 s a 30 fps".
3. Escolha a proporção: igual às suas fotos, 16:9 para o YouTube e a TV, 9:16 para stories e reels no celular, 1:1, 4:5 ou 4:3. Com Ajustar, a foto aparece inteira; com Preencher, as bordas são cortadas e não sobram faixas.
4. Exporte um MP4 em 720p, 1080p, 1440p ou 4K, em H.264, ou em HEVC quando o PC consegue codificar e reproduzir esse formato. A visualização desenha cada quadro com o mesmo código da exportação, então o vídeo fica igual ao que você viu.

Edição e exportação
- Percorra a sequência, avance quadro a quadro e remova uma foto tremida ou arraste uma foto para o lugar certo. Ctrl+Z devolve a foto ao projeto. Remover uma foto a tira do vídeo, nunca do seu disco.
- Marcadores na tira de filme mostram onde parecem faltar fotos.
- As taxas de quadros são exatas, inclusive 29,97, de 0,1 a 120 quadros por segundo.
- O painel Saída estima o tamanho do arquivo antes de você exportar.
- A exportação usa o codificador de vídeo da placa de vídeo quando o PC tem um. Ela mantém o PC ativo até o vídeo ficar pronto, continua quando você minimiza a janela e para em menos de um segundo quando você cancela.
- Quando o vídeo estiver pronto, reproduza-o ali mesmo ou abra a pasta dele.
- Se algo der errado, o 10Lapse diz o que aconteceu e o que fazer em seguida.

Privacidade em primeiro lugar
O 10Lapse funciona offline. Não há conta, nem coleta de dados de uso, nem acesso à rede: suas fotos e vídeos nunca saem do seu PC.

Feito para o Windows 11
O 10Lapse 3.0 requer o Windows 11 em um PC x64 ou ARM64; no Windows 10, a Store oferece a versão anterior, o 10Lapse 2.1.1. Os temas claro, escuro e de alto contraste seguem o Windows, assim como as animações. Tudo funciona pelo teclado, e os leitores de tela anunciam cada controle e o progresso da exportação.

Extensões opcionais da Microsoft Store adicionam mais formatos de foto: fotos HEIC precisam de "Extensão da Imagem HEIF" (grátis) e "Extensões de Vídeo HEVC" (compra necessária), e arquivos RAW de câmera precisam de "Extensão de Imagem Raw" (grátis). O 10Lapse indica qual extensão está faltando e abre a página dela na Store.
```

## Short description

Limit 1,000 characters; Microsoft recommends under 270, because some views cut it there. This draft: 226.

```text
Crie um vídeo timelapse com as centenas de fotos de uma sessão no tripé: defina a velocidade e a proporção, veja o resultado e exporte um MP4 em até 4K. O 10Lapse funciona offline, sem conta, e suas fotos nunca saem do seu PC.
```

## What's new in this version

Limit 1,500 characters. This draft: 1,389. Like the English one, it does not claim a crash rate.

```text
O 10Lapse 3.0 foi refeito do zero para o Windows 11.

- Reescrito do zero, então o código por trás dos travamentos da versão 2.1.1 não existe mais. Quando algo dá errado, o 10Lapse diz o que aconteceu e o que fazer em seguida.
- Importa fotos HEIC e .jpeg. HEIC requer "Extensão da Imagem HEIF" (grátis) e "Extensões de Vídeo HEVC" (compra necessária) da Microsoft Store; o 10Lapse indica qual está faltando.
- Exportação em até 4K, em H.264, ou em HEVC quando o PC é compatível, com progresso, tempo restante e um botão Cancelar que para a exportação em menos de um segundo.
- Continua funcionando quando você muda de janela ou minimiza: a exportação segue, com o progresso na barra de tarefas e uma notificação quando o vídeo fica pronto.
- Novo modo Por foto, para apresentações de slides, ao lado de Por segundo e Duração.
- Visualização ao vivo e tira de filme: percorra, avance, remova ou reordene fotos e desfaça.
- Proporções para TV, celular e redes sociais: 16:9, 9:16, 1:1, 4:5, 4:3 ou a proporção das suas fotos.
- O 10Lapse lembra a última velocidade, proporção e saída para o próximo projeto.
- Temas claro e escuro, atalhos de teclado, suporte a leitores de tela e alto contraste.
- Privado: sem conta e, agora, sem nenhum acesso à rede; suas fotos nunca saem do seu PC.

A versão 3.0 requer o Windows 11 em um PC x64 ou ARM64. No Windows 10, a Store mantém o 10Lapse 2.1.1.
```

## Product features

Up to 20 features, 200 characters each; the Store adds the bullets. Paste one line per feature box. These 19 are
45 to 105 characters each.

```text
Transforma uma pasta de fotos em um vídeo timelapse, em até 4K
Importe uma pasta, fotos escolhidas ou arraste as fotos para a janela
Lê JPEG, PNG e TIFF; HEIC e RAW de câmera com extensões da Microsoft Store
Coloca as fotos na ordem em que foram tiradas; arraste uma miniatura para movê-la
Velocidade em quadros por segundo, pela duração do vídeo ou em segundos por foto
Taxas de quadros exatas de 0,1 a 120 fps, inclusive 29,97
Número de fotos, velocidade e duração do vídeo sempre na tela
Proporções: igual às fotos, 16:9, 9:16, 1:1, 4:5 ou 4:3, com Ajustar ou Preencher
Visualização ao vivo igual ao vídeo exportado
Tira de filme com miniaturas, marcadores de fotos faltando, remoção e opção de desfazer
MP4 em H.264 (ou HEVC quando o PC é compatível) em 720p, 1080p, 1440p ou 4K, com três níveis de qualidade
Estimativa do tamanho do arquivo antes de exportar
Exporta com o codificador de vídeo da placa de vídeo quando o PC tem um
Continua exportando com a janela minimizada e mantém o PC ativo até terminar
Reproduza o vídeo pronto no aplicativo ou abra a pasta dele
Mensagens claras que dizem o que deu errado e o que fazer em seguida
Funciona offline: sem conta, sem coleta de dados, e as fotos nunca saem do seu PC
Temas claro e escuro, alto contraste, atalhos de teclado e suporte a leitores de tela
Lembra a última velocidade, proporção e saída que você usou
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
