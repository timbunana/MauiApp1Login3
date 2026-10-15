# Roadmap — CampoSeguro

Estado de desenvolvimento do projeto. O **produto alvo** está descrito no `ARQUITETURA.md`; este arquivo mostra **onde estamos** em relação a ele.

> **Mantenha este arquivo atualizado ao fim de cada sessão de trabalho:** status das etapas, data, commit e o que entrou ou saiu das listas abaixo. Ver "Histórico" no fim.

**Última atualização:** 2026-10-15

Legenda: ✅ concluído · 🔄 em andamento · ⬜ não iniciado · ⛔ bloqueado

## Etapas do plano

Ordem de dependência definida no `ARQUITETURA.md` §9. As etapas 1 a 4 são pré-requisito da 5.

| # | Etapa | Status | Referência |
|---|---|---|---|
| 1 | `Domain` + `Data` + `Migrator`: banco, multi-tenancy, migrations | ✅ 2026-10-15 | `1562ce0`, `BANCO.md` |
| 2 | `Platform.Api` mínima: criar tenant e primeiro gestor | ⬜ | |
| 3 | `Manager.Api` + `Manager.Web`: colaboradores, convites, catálogos | ⬜ | |
| 4 | `Field.Api`: autenticação, download de catálogo, recebimento de APR idempotente | ⬜ | |
| 5 | Adaptar o app MAUI: SQLite, fila de sincronização, catálogo remoto, compressão de foto | ⬜ | |
| 6 | Indicadores no Painel | ⛔ | indicadores não definidos |
| 7 | Assinaturas na Plataforma | ⛔ | levantamento adiado |

### Escopo das etapas em aberto

**2. `Platform.Api`**
- Autenticação dos administradores da plataforma. Ainda não há fluxo para criar o primeiro deles; precisa de comando no Migrator ou de uma ferramenta de linha de comando.
- Endpoints para criar o tenant e convidar o primeiro gestor.
- Único backend que usa `IgnoreQueryFilters`.

**3. `Manager.Api` + `Manager.Web`**
- Colaboradores: cadastrar, desativar, anonimizar (LGPD).
- Convites: gerar, reenviar (revogando o anterior), exibir o link para o gestor copiar e enviar por e-mail.
- Catálogos: editar o rascunho, publicar e abrir a próxima versão.

**4. `Field.Api`**
- Login com `sessao_dispositivo` e refresh token.
- Download da versão publicada do catálogo.
- Recebimento da APR idempotente, com APR e itens numa única transação.
- Upload de fotos e PDF, servidos só por endpoint autorizado.

**5. App MAUI**
- SQLite local com o rascunho da APR; GUID gerado no aparelho.
- Fila de sincronização com repetição automática.
- Catálogo remoto no lugar do `CatalogoEpis` fixo; "Altura" e "Mesmo Nível" viram dois catálogos.
- Resposta com três estados no lugar do checkbox.
- Compressão de foto (~1280px, JPEG médio), GPS e descrição do local.
- Login real no lugar da lista fixa.

## Trabalho em paralelo

Não depende das etapas acima.

| Item | Status | Observação |
|---|---|---|
| Validar o app no Android (câmera, permissões, `Share`) | ⬜ | Maior risco do que já existe; exige workload `maui`, Android SDK e JDK |
| Projeto de testes automatizados | ⬜ | Os testes do banco (31 casos SQL + 13 EF) rodaram fora do repositório; trazê-los com Testcontainers ou PostgreSQL local |
| Infra: Docker Compose, Nginx, HTTPS | ⛔ | Depende de onde o servidor vai ficar |
| Backup do PostgreSQL fora da máquina | ⛔ | Idem; item que não deve ser cortado |
| E-mail transacional com SPF e DKIM | ⬜ | Necessário para os convites |
| PR de `dev-gabrielsouza` para `master` | ⬜ | |

## Decisões pendentes

| Decisão | Com quem | Bloqueia |
|---|---|---|
| Onde o servidor vai ficar | TI | Infra, backup, etapa 4 em produção |
| Tecnologia do `Manager.Web` (Blazor, React…) | Equipe | Etapa 3 |
| Algoritmo de hash de senha (`PasswordHasher` do Identity ou Argon2) | Equipe | Etapas 2 a 4 |
| Indicadores do dashboard | Produto | Etapa 6 |
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
- O banco não impede incluir `apr_item` numa APR já recebida; a API precisa garantir.
- O banco não confere se o item de uma versão pertence ao mesmo catálogo.

## Histórico

| Data | O que mudou | Commit |
|---|---|---|
| 2026-10-15 | Etapa 1 concluída: modelo de dados, filtro de tenant, migrations `Inicial` e `Imutabilidade`, `BANCO.md` | `1562ce0` |
| 2026-10-15 | Criado este roadmap | — |
