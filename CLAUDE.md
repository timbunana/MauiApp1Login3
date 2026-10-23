# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Visão geral

> O produto se chama **CampoSeguro** (`br.com.camposeguro.app`). O repositório e a pasta raiz ainda usam o nome antigo `MauiApp1Login3` — renomear o remoto é decisão do dono do repositório. O projeto, o namespace e o `ApplicationId` já estão renomeados.
>
> **`ARQUITETURA.md` descreve o produto alvo**: três aplicações (plataforma, painel da empresa, app do colaborador) sobre um banco PostgreSQL compartilhado, com multi-tenancy e sincronização offline. Hoje existem o app (ainda com autenticação falsa e catálogo fixo no código), a camada de dados — `CampoSeguro.Domain`, `CampoSeguro.Data` e `CampoSeguro.Migrator`, documentados em **`BANCO.md`** — a `CampoSeguro.Platform.Api` (ver "Plataforma" abaixo) e o começo da `CampoSeguro.Manager.Api` (ver "Painel" abaixo). Os frontends web ainda não existem. Leia a arquitetura antes de evoluir o app, porque o catálogo vai passar a vir do servidor.

App .NET MAUI para registro de **APR (Análise Preliminar de Risco)** em campo — checklist de EPIs/condições de segurança, captura de foto como evidência e geração de um PDF do registro. Escopo atual coberto: Telecomunicações (trabalho em altura e no mesmo nível).

UI, identificadores e comentários são em **pt-BR** — mantenha essa convenção ao adicionar código.

## Roadmap

**`ROADMAP.md` é a fonte do estado do projeto e deve ser atualizado ao fim de toda sessão de trabalho**, no mesmo commit das mudanças, sem esperar o usuário pedir. Isso inclui o status e a data das etapas, os itens que entraram ou saíram das listas (trabalho em paralelo, decisões pendentes, débitos) e uma linha no "Histórico" com o commit. O commit só existe depois de criado; ao atualizar o roadmap no próprio commit, use `—` e preencha o hash na próxima atualização. Ao concluir uma etapa, remova do roadmap os débitos que ela resolveu.

## Build e execução

Não há lint nem CI. O app MAUI é verificado por build e execução manual; o backend tem testes automatizados.

### Testes (`CampoSeguro.Testes`)

xUnit v3 com o Microsoft.Testing.Platform (o `global.json` da raiz faz o opt-in que o `dotnet test` do .NET 10 exige). Cobre domínio, as regras do banco (CHECKs, índices, FKs compostas e os triggers, em SQL puro), o filtro de tenant do EF e as duas APIs inteiras em memória (`WebApplicationFactory`), inclusive o fluxo Plataforma → convite → Painel.

```powershell
$env:CAMPOSEGURO_TESTE_DB = "Host=localhost;Port=45432;Username=postgres"   # servidor, sem Database
dotnet test --project CampoSeguro.Testes
```

- Cada execução cria um banco `camposeguro_teste_*`, aplica as migrations reais e o apaga no fim. **Nunca aponte `CAMPOSEGURO_TESTE_DB` para produção.**
- Sem a variável, os testes de banco e de API são **pulados** (não falham) e só os de domínio rodam.
- Os testes não limpam dados entre si: cada um cria as próprias empresas com CNPJ e e-mail aleatórios (`Infra/Gerar`, `Infra/Pessoas`, `Infra/Cenario`). Mantenha assim; não conte linhas de tabela inteira.
- As fábricas usam `WebApplicationFactory<OpcoesJwt>` de cada API, não `Program`: as duas APIs têm um `Program` global e o nome seria ambíguo.

**Para ter um servidor descartável**, use os binários do PostgreSQL 18 instalado (`C:\Program Files\PostgreSQL\18\bin`): `initdb -D <scratch>\pgdata -U postgres -A trust` e `pg_ctl ... -o "-p 45432 -c listen_addresses=localhost" start`. Antes de escolher a porta, confira `netsh interface ipv4 show excludedportrange protocol=tcp`: o Windows reserva faixas que mudam a cada boot e o bind falha com "Permission denied" (a 55432 já caiu numa delas). O `pg_ctl start` não devolve o console; rode em segundo plano.

**O SDK está instalado em escopo de usuário** (sem admin) em `%LOCALAPPDATA%\Microsoft\dotnet` e **não está no PATH**. Só a workload `maui-windows` está instalada — builds de Android/iOS ainda exigem `dotnet workload install maui` (+ Android SDK/JDK).

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"

# Build. O -p:TargetFrameworks é obrigatório: sem ele o restore avalia TODOS os TFMs
# do .csproj e falha por falta das workloads de Android/iOS.
dotnet build CampoSeguro\CampoSeguro.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0

# Executar. DOTNET_ROOT é obrigatório: o apphost procura o runtime no caminho padrão
# da máquina e sai com 0x80008003 ("You must install .NET") sem essa variável.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
.\CampoSeguro\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CampoSeguro.exe
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

**`AprTelecom1` (coração do app):** o tipo de trabalho (*Mesmo Nível* × *Altura*) é escolhido por **dois botões dentro de um card na própria tela** (`OnMesmoNivelClicked` / `OnAlturaClicked`); o botão escolhido troca para `PrimaryButton`. Antes isso era um `DisplayActionSheet` no `OnAppearing` — não volte a usar diálogo de sistema para isso. O nome do colaborador também é um campo da tela (`txtNome`), não um `DisplayPromptAsync`.

Os 10 itens do checklist vivem **apenas** em `CatalogoEpis` (array estático no code-behind), com uma flag `AplicaMesmoNivel` por item. `MontarChecklist()` filtra, numera e publica a lista via `BindableLayout.SetItemsSource`, renderizada pelo `DataTemplate` de `ItemEpi`. Tela e PDF consomem a mesma coleção — **ao adicionar ou alterar um item de EPI, mexa só em `CatalogoEpis`**, nunca no XAML.

Ao salvar: monta o PDF com **QuestPDF** em memória, grava em `FileSystem.Current.AppDataDirectory` e entrega via `Share.Default.RequestAsync`. **Não volte a gravar direto em pastas públicas** (ex.: `Downloads`): o scoped storage do Android bloqueia isso desde a API 29. A licença Community do QuestPDF é configurada no **construtor da página**, não no bootstrap. `using QColors = QuestPDF.Helpers.Colors;` é necessário para evitar colisão com `Microsoft.Maui.Graphics.Colors`.

**Diálogos:** `Dialogo.xaml` é um `ContentView` (véu + card) que substitui `DisplayAlert` e `DisplayActionSheet`, que ignoram o design system. Cada página o declara como **última camada de um `Grid` raiz** (`<local:Dialogo x:Name="Dialogo" />`) e chama `AvisarAsync` ou `PerguntarAsync`. Não use os diálogos nativos em código novo.

**Sem MVVM:** toda a lógica está em `*.xaml.cs` (event handlers). Não há ViewModels, serviços nem modelos além de `DadosUsuario` e `ItemEpi`. O `CommunityToolkit.Maui` está inicializado, mas nenhum recurso dele é usado ainda.

**Estilos:** o design system vive em `Resources/Styles/Colors.xaml` + `Styles.xaml` e está documentado em **`DESIGN.md`** — leia antes de mexer em qualquer tela. É **tema escuro único** (`UserAppTheme = AppTheme.Dark` em `App`), com cores diretas (sem `AppThemeBinding`). **Nenhum hex dentro de página**: só `StaticResource`. Ícones são SVGs em `Resources/Images/ic_*.svg`, referenciados no XAML com extensão **`.png`** (o Resizetizer converte no build) e com a cor embutida no arquivo. `EstiloNativo.RemoverBordaDosCampos()`, chamado no `MauiProgram`, tira a borda nativa do `Entry` por plataforma. XAML é compilado com `MauiXamlInflator=SourceGen`.

## Camada de dados

`CampoSeguro.Domain` (entidades, sem dependências) → `CampoSeguro.Data` (EF Core 10 + Npgsql, migrations em `Migracoes/`) → `CampoSeguro.Migrator` (console que aplica as migrations, lendo a conexão de `CAMPOSEGURO_DB`). Todos são `net10.0` puro: compilam com `dotnet build CampoSeguro.Migrator`, sem workload MAUI. Comandos de migration e convenções estão no `BANCO.md`.

Regras que não são óbvias pelo código:
- **Toda entidade de tenant herda `EntidadeDoTenant` e precisa de `FiltrarPorTenant<T>` no `OnModelCreating`**, além de chaves compostas `(TenantId, X)` nas relações. Se faltar o filtro, o modelo nem monta (`GarantirFiltroEmTodoTenant`).
- **A migration `Imutabilidade` é escrita à mão** (triggers). Ao regerar a `Inicial`, copie esse arquivo antes e restaure depois.
- **Nenhum backend aplica migration na inicialização.** Só o Migrator.
- Os pacotes do EF estão alinhados em 10.0.13 com uma referência explícita ao `Relational`; sem ela, o Npgsql traz o 10.0.4 para quem referencia `Data` e o build quebra com `CS1705`.
- **Senhas e tokens passam por `Data/Seguranca`**: `Senhas` (PBKDF2 do `PasswordHasher` do Identity, formato versionado), `Tokens` (256 bits aleatórios; o banco guarda só o SHA-256) e `LinksDeAcesso` (emite e reemite convites). Não gere hash nem token em outro lugar.
- **Cada aplicação vai para um repositório próprio.** Por isso nenhuma API referencia outra: o que é comum fica em `Domain`/`Data`, que serão o pacote compartilhado. Não crie dependência entre APIs, nem mesmo para reaproveitar código. Só `CampoSeguro.Testes` enxerga as duas, e será dividido junto. Normalização e validação de e-mail e CNPJ ficam em `Domain/Documentos.cs`.
- O primeiro administrador da Plataforma é criado por `CampoSeguro.Migrator --criar-admin "Nome" email` (senha lida do terminal sem eco ou de `CAMPOSEGURO_SENHA_ADMIN`, nunca de argumento).

## Plataforma (`CampoSeguro.Platform.Api`)

ASP.NET Core com minimal APIs, `net10.0`. Cadastra empresas (tenants) e convida gestores; é o único backend que enxerga todos os tenants.

```powershell
$env:CAMPOSEGURO_DB = "Host=localhost;Database=camposeguro;Username=...;Password=..."
$env:Jwt__Chave = "<pelo menos 32 bytes aleatórios>"
$env:Convites__UrlAceite = "https://gestao.camposeguro.com.br/convite"
dotnet run --project CampoSeguro.Platform.Api   # sobe em http://localhost:5080; OpenAPI em /openapi/v1.json (só Development)
```

Sem qualquer uma das três variáveis a API recusa subir. Rotas: `POST /autenticacao/login`; `GET /empresas`, `GET /empresas/{id}`, `POST /empresas` (empresa + primeiro gestor + convite numa transação), `POST /empresas/{id}/gestores`, `POST /empresas/{id}/gestores/{gestorId}/convite` (reenvio para quem ainda não tem senha: revoga o pendente e emite outro) e `POST /empresas/{id}/gestores/{gestorId}/redefinicao-senha` (o único caminho para um gestor que esqueceu a senha, enquanto não há e-mail).

Regras que não são óbvias pelo código:
- **`IgnoreQueryFilters` só existe em `TodosOsTenants.cs`.** O contexto da Plataforma não tem tenant, então qualquer consulta a entidade de tenant fora de `db.DeTodosOsTenants<T>()` devolve vazio. Nunca copie esse arquivo para outro backend.
- **Toda rota exige autenticação por padrão** (política de fallback); só o login tem `AllowAnonymous`. A cada requisição o token é conferido contra `administrador_plataforma.ativo`, então desativar um administrador corta o acesso na hora.
- **O link do convite leva o token no fragmento** (`.../convite#<token>`), para não aparecer em log de acesso nem no `Referer`. A página de aceite (etapa 3) precisa ler o fragmento e enviar o token por POST.
- Sem serviço de e-mail, o link volta na resposta para o administrador repassar; é o caminho oficial (ARQUITETURA §5), não um contorno.
- O login tem limite de 10 tentativas por minuto por IP (`Login:TentativasPorMinuto`). Atrás do Nginx, configure `ForwardedHeaders`, senão o limite vira global.

## Painel (`CampoSeguro.Manager.Api`)

Backend do gestor da empresa-cliente. Mesma estrutura da Plataforma, porta 5090, e exige `CAMPOSEGURO_DB`, `Jwt__Chave` (**diferente** da chave da Plataforma) e `Convites__UrlAceite` (a mesma da Plataforma). Rotas até agora:
- `POST /autenticacao/login` (só gestor; técnico com a senha certa recebe 403) e `GET /eu`.
- `POST /convites/consultar` e `POST /convites/aceitar`: abertas, para qualquer colaborador definir a senha pelo link.
- `/colaboradores`: `GET` (só ativos; `?incluirDesativados=true` para todos), `GET /{id}`, `POST` (cadastra e devolve o link do convite; sem `papel`, é técnico) e as ações `POST /{id}/convite`, `/redefinicao-senha`, `/desativar`, `/reativar`, `/anonimizar`.
- `/catalogos`: `GET` (`?incluirInativos=true`), `POST`, `GET|PUT /{id}`, `POST /{id}/desativar|reativar`, `GET /{id}/versoes/{numero}`; rascunho em `GET|POST|PUT|DELETE /{id}/rascunho` e `POST /{id}/rascunho/publicar`; modelos em `GET /modelos` e `POST /modelos/{codigo}`.

Regras que não são óbvias pelo código:
- **O tenant vem só do token**: `ContextoTenantDaRequisicao` lê as claims `tenant` e `sub` a cada consulta. Nada de tenant em corpo, query ou rota.
- **`IgnoreQueryFilters` só existe em `BuscaSemTenant.cs`**, para o que acontece antes de se saber a empresa (login por e-mail, convite pelo hash do token, conferência do token). Toda consulta ali filtra por uma chave que identifica uma pessoa só; nunca liste nada por esse caminho.
- Toda rota exige **gestor** autenticado por padrão (política de fallback com `RequireRole`); login e convites têm `AllowAnonymous` explícito. A cada requisição o token é conferido contra o banco (ativo, mesma empresa, ainda gestor).
- O aceite marca o convite como usado com `UPDATE ... WHERE usado_em IS NULL` antes de gravar a senha: é o que decide entre duas abas abrindo o mesmo link. Redefinição de senha também revoga as `sessao_dispositivo` do usuário.
- Link inválido, vencido, usado ou revogado dão **a mesma resposta**, de propósito.
- **Redefinição de senha pelo Painel só vale para técnico.** O link volta para quem o gerou; se um gestor pudesse gerar o de outro gestor, assumiria a conta dele. Gestor recupera a senha pela Plataforma.
- Desativar revoga os links pendentes e as sessões dos aparelhos na mesma gravação. Anonimizar (`Usuario.Anonimizar`) exige estar desativado, é irreversível e não toca no `apr.autor_nome`.
- **Catálogo:** criar abre o rascunho da versão 1; `PUT /rascunho` substitui a lista inteira, na ordem enviada; publicar congela; `POST /rascunho` abre a N+1 copiando a última publicada. Item com `itemId` mantém a identidade (é o que agrupa o indicador de não conformidade mesmo após corrigir o texto); sem `itemId` é item novo. **A API confere que o `itemId` é do mesmo catálogo**, o que o banco não faz. Itens antigos são apagados e os novos incluídos em gravações separadas, por causa dos índices únicos de ordem e item.
- **Modelos** (`Catalogos/Modelos.cs`) usam os mesmos textos do `CatalogoEpis` do app e são importados como **rascunho**: quem publica é a empresa, depois de revisar. Se mudar um texto lá, mude aqui também até a etapa 5 aposentar a lista fixa.
- Emissão de link (`LinksDeAcesso` em `Data/Seguranca`) e situação do acesso (`Usuario.Situacao`) são compartilhadas com a Plataforma: mude lá, não em cada API.

## Limitações conhecidas

Em aberto:
- O fluxo **nunca foi validado no Android** — tudo que foi verificado rodou no alvo Windows. Câmera, permissões e `Share` precisam de teste em aparelho real.
- No Windows a **barra de título fica clara**: o WinUI não acompanha o tema escuro do app. Não afeta Android.

Já corrigido (não reintroduzir): rótulos genéricos `"Item N"` no PDF; mapeamento trocado dos EPIs no modo "Mesmo Nível"; `Stream` descartado na pré-visualização da foto; opções de área sem destino e sem aviso; asset inexistente `analise_poste1.png`; build quebrado por `MCT001`; gravação em pasta pública no Android; ausência de permissão de câmera; navegação por `Application.Current.MainPage`; diálogos nativos fora do design system.

Débitos estruturais: autenticação mockada com senhas em texto plano no código; sessão gravada no `SecureStorage` mas nunca lida; nenhuma persistência/histórico de APRs; é possível salvar uma APR sem nenhum item marcado; campos `string` não-anuláveis sem inicialização sob `Nullable=enable`; `catch` genéricos exibindo `ex.Message` cru ao usuário.

Uma armadilha de rotina: **o app aberto trava os arquivos de `bin/`** e o build falha com `MSB3027`. Feche o processo `CampoSeguro` antes de compilar.

## Git

Repositório: `https://github.com/timbunana/MauiApp1Login3.git`, branch `master`. Histórico autorado por Jonas Andrade — confirme com o usuário antes de qualquer push.
