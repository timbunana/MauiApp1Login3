# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Visão geral

App .NET MAUI para registro de **APR (Análise Preliminar de Risco)** em campo — checklist de EPIs/condições de segurança, captura de foto como evidência e geração de um PDF do registro. Escopo atual coberto: Telecomunicações (trabalho em altura e no mesmo nível).

UI, identificadores e comentários são em **pt-BR** — mantenha essa convenção ao adicionar código.

## Build e execução

Não há projeto de testes, lint ou CI neste repositório. Toda a verificação hoje é build + execução manual.

**O SDK está instalado em escopo de usuário** (sem admin) em `%LOCALAPPDATA%\Microsoft\dotnet` e **não está no PATH**. Só a workload `maui-windows` está instalada — builds de Android/iOS ainda exigem `dotnet workload install maui` (+ Android SDK/JDK).

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"

# Build. O -p:TargetFrameworks é obrigatório: sem ele o restore avalia TODOS os TFMs
# do .csproj e falha por falta das workloads de Android/iOS.
dotnet build MauiApp1Login3\MauiApp1Login3.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0

# Executar. DOTNET_ROOT é obrigatório: o apphost procura o runtime no caminho padrão
# da máquina e sai com 0x80008003 ("You must install .NET") sem essa variável.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
.\MauiApp1Login3\bin\Debug\net10.0-windows10.0.19041.0\win-x64\MauiApp1Login3.exe
```

TFMs declarados: `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`.

**Verificando a UI sem testes automatizados:** o alvo Windows pode ser dirigido por UI Automation via PowerShell (`UIAutomationClient`), navegando com `InvokePattern`/`ValuePattern`/`ExpandCollapsePattern` e capturando a janela com `PrintWindow` (flag `2`) — `CopyFromScreen` não serve, porque pega o que está na frente e falha se a estação estiver bloqueada. Dois cuidados: o PowerShell 5.1 lê `.ps1` como ANSI, então **não use literais acentuados** no script (case nomes de elementos por padrão ASCII com curinga, ex.: `"Telecomunica*"`); e dê ~25s de espera para o app subir.

## Arquitetura

**Bootstrap:** `MauiProgram.CreateMauiApp()` registra as fontes e `UseMauiCommunityToolkit()` — nenhuma página ou serviço no DI. **Não remova a chamada do toolkit**: o analisador do pacote emite `error MCT001` e quebra o build se `CommunityToolkit.Maui` estiver referenciado sem ser inicializado. `App.CreateWindow()` devolve `MyWindow(new AppShell())`, uma `Window` com tamanho **fixo 400x600**.

**Navegação:** tudo passa pelo **Shell**. `AppShell.xaml` tem um único `ShellContent` (`MainPage`, com `FlyoutBehavior="Disabled"`); as demais páginas são rotas globais registradas em `AppShell.xaml.cs` via `Routing.RegisterRoute(nameof(X), typeof(X))`. A navegação usa `Shell.Current.GoToAsync(nameof(Pagina))` para avançar e `GoToAsync("..")` para voltar. `MainPage` esconde a barra com `Shell.NavBarIsVisible="False"`.

Pilha resultante: `MainPage → Login → Entrada → AprTelecom1`. Voltar de `Entrada` cai no `Login`, que funciona como logout.

**Fluxo de telas:**
1. `MainPage` — abertura da marca, botões "Iniciar uma APR" e "Como funciona".
2. `Login` — valida contra uma **lista hardcoded** em `Login.xaml.cs`; grava `usuario_logado` no `SecureStorage` (valor nunca é lido depois).
3. `Entrada` — um card por área de atuação; só **Telecomunicações** navega, os outros dois avisam "em breve".
4. `AprTelecom1` — tela principal e única com lógica real.
5. `Protegida.xaml` — página órfã do template, não referenciada.

**`AprTelecom1` (coração do app):** o tipo de trabalho (*Mesmo Nível* × *Altura*) é escolhido por **dois botões dentro de um card na própria tela** (`OnMesmoNivelClicked` / `OnAlturaClicked`); o botão escolhido troca para `PrimaryButton`. Antes isso era um `DisplayActionSheet` no `OnAppearing` — não volte a usar diálogo de sistema para isso. O nome do colaborador também é um campo da tela (`txtNome`), não um `DisplayPromptAsync`.

Os 10 itens do checklist vivem **apenas** em `CatalogoEpis` (array estático no code-behind), com uma flag `AplicaMesmoNivel` por item. `MontarChecklist()` filtra, numera e publica a lista via `BindableLayout.SetItemsSource`, renderizada pelo `DataTemplate` de `ItemEpi`. Tela e PDF consomem a mesma coleção — **ao adicionar ou alterar um item de EPI, mexa só em `CatalogoEpis`**, nunca no XAML.

Ao salvar: monta o PDF com **QuestPDF** em memória, grava em `FileSystem.Current.AppDataDirectory` e entrega via `Share.Default.RequestAsync`. **Não volte a gravar direto em pastas públicas** (ex.: `Downloads`): o scoped storage do Android bloqueia isso desde a API 29. A licença Community do QuestPDF é configurada no **construtor da página**, não no bootstrap. `using QColors = QuestPDF.Helpers.Colors;` é necessário para evitar colisão com `Microsoft.Maui.Graphics.Colors`.

**Diálogos:** `Dialogo.xaml` é um `ContentView` (véu + card) que substitui `DisplayAlert` e `DisplayActionSheet`, que ignoram o design system. Cada página o declara como **última camada de um `Grid` raiz** (`<local:Dialogo x:Name="Dialogo" />`) e chama `AvisarAsync` ou `PerguntarAsync`. Não use os diálogos nativos em código novo.

**Sem MVVM:** toda a lógica está em `*.xaml.cs` (event handlers). Não há ViewModels, serviços nem modelos além de `DadosUsuario` e `ItemEpi`. O `CommunityToolkit.Maui` está inicializado, mas nenhum recurso dele é usado ainda.

**Estilos:** o design system vive em `Resources/Styles/Colors.xaml` + `Styles.xaml` e está documentado em **`DESIGN.md`** — leia antes de mexer em qualquer tela. É **tema escuro único** (`UserAppTheme = AppTheme.Dark` em `App`), com cores diretas (sem `AppThemeBinding`). **Nenhum hex dentro de página**: só `StaticResource`. Ícones são SVGs em `Resources/Images/ic_*.svg`, referenciados no XAML com extensão **`.png`** (o Resizetizer converte no build) e com a cor embutida no arquivo. `EstiloNativo.RemoverBordaDosCampos()`, chamado no `MauiProgram`, tira a borda nativa do `Entry` por plataforma. XAML é compilado com `MauiXamlInflator=SourceGen`.

## Limitações conhecidas

Em aberto:
- O fluxo **nunca foi validado no Android** — tudo que foi verificado rodou no alvo Windows. Câmera, permissões e `Share` precisam de teste em aparelho real.
- `.csproj` tem entradas `MauiImage` para `logo1.png` e `dotnet_bot.png`, que não existem mais.
- `logo2.png` e `logo_entrada.png` são arquivos idênticos e **não são mais usados** por nenhuma tela.
- `Protegida.xaml` e `MImage.cs` continuam órfãos.
- No Windows a **barra de título fica clara**: o WinUI não acompanha o tema escuro do app. Não afeta Android.

Já corrigido (não reintroduzir): rótulos genéricos `"Item N"` no PDF; mapeamento trocado dos EPIs no modo "Mesmo Nível"; `Stream` descartado na pré-visualização da foto; opções de área sem destino e sem aviso; asset inexistente `analise_poste1.png`; build quebrado por `MCT001`; gravação em pasta pública no Android; ausência de permissão de câmera; navegação por `Application.Current.MainPage`; diálogos nativos fora do design system.

Débitos estruturais: autenticação mockada com senhas em texto plano no código; sessão gravada no `SecureStorage` mas nunca lida; nenhuma persistência/histórico de APRs; é possível salvar uma APR sem nenhum item marcado; campos `string` não-anuláveis sem inicialização sob `Nullable=enable`; `catch` genéricos exibindo `ex.Message` cru ao usuário.

Uma armadilha de rotina: **o app aberto trava os arquivos de `bin/`** e o build falha com `MSB3027`. Feche o processo `MauiApp1Login3` antes de compilar.

## Git

Repositório: `https://github.com/timbunana/MauiApp1Login3.git`, branch `master`. Histórico autorado por Jonas Andrade — confirme com o usuário antes de qualquer push.
