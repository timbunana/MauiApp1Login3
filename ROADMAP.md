# Roadmap — CampoSeguro

Estado de desenvolvimento do projeto. O **produto alvo** está descrito no `ARQUITETURA.md`; este arquivo mostra **onde estamos** em relação a ele.

> **Mantenha este arquivo atualizado ao fim de cada sessão de trabalho:** status das etapas, data, commit e o que entrou ou saiu das listas abaixo. Ver "Histórico" no fim.

**Última atualização:** 2026-10-23

Legenda: ✅ concluído · 🔄 em andamento · ⬜ não iniciado · ⛔ bloqueado

## Etapas do plano

Ordem de dependência definida no `ARQUITETURA.md` §9. As etapas 1 a 4 são pré-requisito da 5.

| # | Etapa | Status | Referência |
|---|---|---|---|
| 1 | `Domain` + `Data` + `Migrator`: banco, multi-tenancy, migrations | ✅ 2026-10-15 | `1562ce0`, `BANCO.md` |
| 2 | `Platform.Api` mínima: criar tenant e primeiro gestor | ✅ 2026-10-23 | `fa68ea2`, `CLAUDE.md` § Plataforma |
| 3 | `Manager.Api` + `Manager.Web`: colaboradores, convites, catálogos | 🔄 API pronta; falta o `Manager.Web` | `CLAUDE.md` § Painel |
| 4 | `Field.Api`: autenticação, download de catálogo, recebimento de APR idempotente | ✅ 2026-10-23 | `a54ca89`, `CLAUDE.md` § App de campo |
| 5 | Adaptar o app MAUI: SQLite, fila de sincronização, catálogo remoto, compressão de foto | ⬜ | |
| 6 | Indicadores no Painel | ⛔ | indicadores não definidos |
| 7 | Assinaturas na Plataforma | ⛔ | levantamento adiado |

### Escopo das etapas em aberto

**3. `Manager.Api` + `Manager.Web`**

Feito na API:
- Login do gestor, `GET /eu`, consulta e aceite de convite (vale para gestor e técnico; redefinição de senha revoga as sessões dos aparelhos).
- Colaboradores: cadastrar (com link de convite), listar, desativar (revoga links e sessões), reativar e anonimizar (LGPD).
- Links: reenviar convite e gerar redefinição de senha de técnico. O gestor que esqueceu a senha recupera pela Plataforma (`/redefinicao-senha`).
- Catálogos: criar, renomear, desativar; rascunho (editar, descartar, publicar, abrir a próxima versão); modelos "Telecom — Altura" e "Telecom — Mesmo Nível", importados como rascunho.

- Consulta das APRs: lista paginada com filtros, detalhe, PDF e fotos conferidos contra o hash antes de entregar.
- Sessões dos aparelhos de cada técnico (listar, revogar) e troca de senha pelo próprio usuário, no Painel e no app.

Falta:
- **`Manager.Web`**, incluindo a página de aceite do convite: lê o token do fragmento (`#`) do link e chama `POST /convites/consultar` e `/convites/aceitar`. Até existir, aceitar um convite exige chamar a API diretamente.

**Fora de qualquer etapa: `Platform.Web`.** O frontend da Administração não está no plano; hoje a Plataforma só é operável pela API. Sugestão: fazer junto com o `Manager.Web`, na mesma tecnologia.

**5. App MAUI** (o contrato com a `Field.Api` já está pronto e testado)
- Referenciar `CampoSeguro.Domain` para usar o `ConteudoApr` ao assinar: o servidor recusa APR cujo hash não confira.
- SQLite local com o rascunho da APR; GUID gerado no aparelho (APR e cada foto).
- Login real no lugar da lista fixa; token de renovação no `SecureStorage`.
- Exigir todos os itens respondidos antes de assinar (o servidor já recusa APR incompleta).
- Fila de sincronização com repetição automática.
- Catálogo remoto no lugar do `CatalogoEpis` fixo; "Altura" e "Mesmo Nível" viram dois catálogos.
- Resposta com três estados no lugar do checkbox.
- Compressão de foto (~1280px, JPEG médio), GPS e descrição do local.

## Trabalho em paralelo

Não depende das etapas acima.

| Item | Status | Observação |
|---|---|---|
| Validar o app no Android (câmera, permissões, `Share`) | ⬜ | Maior risco do que já existe; exige workload `maui`, Android SDK e JDK |
| Projeto de testes automatizados | ✅ 2026-10-23 | `CampoSeguro.Testes`, 133 casos (domínio, banco, as três APIs e o fluxo de ponta a ponta); precisa de um PostgreSQL em `CAMPOSEGURO_TESTE_DB` |
| Separar em repositórios: núcleo (`Domain`, `Data`, `Migrator`), Administração, Gestão e App | ⬜ | Depende de como o núcleo será distribuído; os testes se dividem junto, e o fluxo Plataforma → Painel vira teste do núcleo ou de ponta a ponta |
| CI rodando os testes | ⬜ | Precisa de um PostgreSQL no runner |
| Infra: Docker Compose, Nginx, HTTPS | ⛔ | Depende de onde o servidor vai ficar |
| Backup do PostgreSQL fora da máquina | ⛔ | Idem; item que não deve ser cortado |
| E-mail transacional com SPF e DKIM | ⬜ | Necessário para os convites |
| PR de `dev-gabrielsouza` para `master` | ⬜ | |

## Decisões pendentes

| Decisão | Com quem | Bloqueia |
|---|---|---|
| Onde o servidor vai ficar | TI | Infra, backup, etapa 4 em produção |
| Como distribuir o núcleo (`Domain`/`Data`) entre os repositórios: pacote NuGet privado (GitHub Packages, Azure Artifacts) ou submódulo git | Equipe | Separação dos repositórios |
| Tecnologia do `Manager.Web` e do `Platform.Web` (Blazor, React…) | Equipe | Frontends da etapa 3 e da Administração |
| Algoritmo de hash de senha: adotado o PBKDF2 do `PasswordHasher` do Identity na etapa 2; confirmar ou trocar por Argon2 | Equipe | Nada: o formato é versionado e a troca regrava o hash no login |
| Indicadores do dashboard | Produto | Etapa 6 |
| Suportar CNPJ alfanumérico (Receita, desde jul/2026) | Produto | Cadastro de empresas novas com esse formato |
| Assinaturas e cobrança | Produto | Etapa 7 |

As decisões já tomadas ficam no `ARQUITETURA.md` §11.

## Débitos conhecidos

**App MAUI** (detalhes no `CLAUDE.md`)
- Autenticação falsa, com senhas em texto puro no código.
- Sessão gravada no `SecureStorage` mas nunca lida.
- Sem persistência nem histórico de APRs.
- É possível salvar uma APR sem nenhum item marcado.
- `catch` genéricos que mostram `ex.Message` ao usuário.
- Campos `string` não anuláveis sem inicialização.
- Barra de título clara no Windows.

**Camada de dados** (detalhes no `BANCO.md`)
- O banco não impede incluir `apr_item` numa APR já recebida; hoje a `Field.Api` garante (não há rota para isso).
- O banco não confere se o item de uma versão pertence ao mesmo catálogo; hoje a edição do rascunho no Painel garante.
- CNPJ só numérico no CHECK e na validação; o alfanumérico é recusado.

**APIs** (detalhes no `CLAUDE.md`)
- Limite de tentativas de login por IP, na Plataforma e no Painel: atrás do Nginx vira global até configurar `ForwardedHeaders`.

## Histórico

| Data | O que mudou | Commit |
|---|---|---|
| 2026-10-15 | Etapa 1 concluída: modelo de dados, filtro de tenant, migrations `Inicial` e `Imutabilidade`, `BANCO.md` | `1562ce0` |
| 2026-10-15 | Criado este roadmap | `00aff5a` |
| 2026-10-23 | Etapa 2 concluída: `Platform.Api` (login JWT, empresas, convite e reenvio de gestor), `Data/Seguranca`, `--criar-admin` no Migrator | `fa68ea2` |
| 2026-10-23 | Projeto `CampoSeguro.Testes` (79 casos). Etapa 3 iniciada: `Manager.Api` com login do gestor, `/eu` e aceite de convite | `2c49e35` |
| 2026-10-23 | Colaboradores no Painel (cadastro, links, desativar, reativar, anonimizar); redefinição de senha do gestor pela Plataforma; decisão de repositórios separados registrada | `c30eff2` |
| 2026-10-23 | Catálogos no Painel: ciclo rascunho → publicação → próxima versão e modelos de Telecom | `da3a723` |
| 2026-10-23 | Etapa 4 concluída: `Field.Api` (sessão por aparelho, catálogo publicado, APR idempotente com hash conferido, fotos e PDF); `ConteudoApr` e `Armazenamento` no núcleo | `a54ca89` |
| 2026-10-23 | Backend da etapa 3 completo: consulta de APRs no Painel (com conferência de hash dos arquivos), sessões dos aparelhos e troca de senha | — |
