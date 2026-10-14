# Design System — CampoSeguro

Padrão visual do app. **Toda tela nova sai daqui.** Se precisar de algo que não existe neste documento, acrescente o token antes de usar — nunca escreva um valor solto na página.

A referência é a interface do PedeFácil: tema escuro quente, um único acento laranja, cards muito arredondados com chip de ícone, rótulo em caixa alta acima de título grande, e botões em formato de pílula.

## Princípios

1. **Escuro quente, não cinza.** O fundo é um preto puxado para o marrom (`#0E0B0A`), não o preto neutro. Isso dá a sensação de calor da referência; um cinza neutro deixa tudo clínico.
2. **Um acento só.** Laranja marca ação, ícone e rótulo de seção. Verde e vermelho aparecem **apenas** para estado de segurança (conforme / não conforme), nunca para decorar.
3. **Rótulo, título, apoio.** O bloco de texto padrão tem três camadas nessa ordem: caixa alta pequena em laranja, título grande em branco, descrição em cinza quente. Esse trio se repete em toda tela.
4. **Tudo mora em card.** Conteúdo agrupado fica dentro de um `Card` arredondado. O fundo da página quase nunca encosta direto no conteúdo.

## Tema

O app é **somente escuro** — `UserAppTheme = AppTheme.Dark` é fixado em `App`. Não há variante clara, então as cores são valores diretos, sem `AppThemeBinding`. Se um dia existir tema claro, ele vira um segundo conjunto de tokens aqui, e só então as páginas passam a usar `AppThemeBinding`.

## Cores

Em `Resources/Styles/Colors.xaml`.

| Token | Valor | Uso |
|---|---|---|
| `Surface` | `#0E0B0A` | Fundo da página |
| `SurfaceCard` | `#17120F` | Card, campo de formulário |
| `SurfaceChip` | `#241A15` | Chip de ícone, realce dentro de card |
| `BorderSubtle` | `#2B211C` | Contorno de card (1px) e divisórias |
| `TextPrimary` | `#FFFFFF` | Títulos e corpo |
| `TextSecondary` | `#A89E98` | Descrição, apoio, estado indisponível |
| `Accent` | `#F7914F` | Preenchimento de botão, ícone, chevron |
| `AccentStrong` | `#E8703A` | Rótulo em caixa alta, ênfase em título |
| `OnAccent` | `#241209` | Texto sobre o laranja (escuro, nunca branco) |
| `Success` | `#3FBF6A` | Item conforme |
| `Danger` | `#E5484D` | Item não conforme, ação destrutiva |

O texto sobre o laranja é **escuro**, não branco — é assim na referência e é o que mantém o contraste legível.

## Tipografia

`OpenSansSemibold` para títulos, `OpenSansRegular` para o resto.

| Estilo | Tamanho | Uso |
|---|---|---|
| `DisplayText` | 38 | Abertura. Uma por tela. |
| `HeadlineText` | 27 | Título da tela e de card grande |
| `TitleText` | 19 | Título de card comum |
| `BodyText` | 16 | Corpo |
| `SecondaryText` | 15 | Descrição em `TextSecondary` |
| `EyebrowText` | 12 | **Caixa alta**, `CharacterSpacing="1.5"`, cor `AccentStrong` |

Títulos usam `CharacterSpacing="-0.5"` e entrelinha fechada (`LineHeight` 1.1–1.15). O `EyebrowText` é o que mais marca a identidade — use antes de todo título de seção.

## Espaçamento

Múltiplos de 4; na prática: **8** (rótulo e campo), **16** (entre cards), **24** (margem lateral da página e padding interno do card), **32** (entre blocos), **40** (antes da ação final).

## Componentes

**Card** — `Border`, fundo `SurfaceCard`, `StrokeShape="RoundRectangle 24"`, traço de 1px em `BorderSubtle`, padding 24. O raio grande é característico da referência; não reduza.

**IconChip** — quadrado de 48x48, raio 16, fundo `SurfaceChip`, com a imagem do ícone (20x20) centralizada. Abre o card, acima do título.

**Botões** (altura 54, raio 27 — pílula):
- `PrimaryButton` — fundo `Accent`, texto `OnAccent`. O rótulo termina com **" →"**, como na referência.
- `SecondaryButton` — fundo `SurfaceCard`, texto branco, borda `BorderSubtle`.
- `DangerButton` — fundo `SurfaceCard`, texto `Danger`.

**FormField** — `Border` com `SurfaceCard`, raio 16, borda sutil, padding `16,4`. O rótulo vai acima, em `EyebrowText`.

**Separator** — `BoxView` de 1px em `BorderSubtle`.

## Ícones

SVGs simples em `Resources/Images/ic_*.svg`, traço em `#F7914F`. O MAUI converte para PNG no build, então **a cor fica embutida no arquivo** — um ícone de outra cor exige outro arquivo. Mantenha-os geométricos e sem preenchimento, no estilo linear da referência.

## Aplicação por tela

- **MainPage** — cabeçalho flutuante (card arredondado com marca à esquerda e botão à direita), chip de rótulo, título grande em duas cores (uma linha branca, uma laranja), descrição e dois botões (primário + secundário).
- **Login** — título, dois `FormField`, botão primário ancorado embaixo.
- **Entrada** — um `Card` por área de atuação, cada um com `IconChip`, título, descrição e chevron laranja. Indisponíveis ficam em `TextSecondary` com a marca "Em breve" e sem chevron.
- **AprTelecom1** — cabeçalho com `EyebrowText` indicando o tipo de trabalho, checklist num card (item = texto + switch), card de evidência e ações no rodapé.

## Regras

- **Nenhum hex dentro de uma página.** Só `StaticResource`.
- Os itens do checklist vêm de `DataTemplate` ligado a `ItemEpi` — mexa no template, não em código de layout.
- Nada de imagem de marketing com texto embutido como elemento de tela. A identidade vem da tipografia e do espaçamento.
- O alvo é celular em campo: nenhum toque menor que 44x44, e tudo precisa funcionar em 400x600.
