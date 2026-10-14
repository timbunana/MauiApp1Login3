# Arquitetura — CampoSeguro

Proposta de arquitetura do produto. Registra as decisões já tomadas, o porquê de cada uma e o que ainda está em aberto. Quem for implementar deve ler isto antes do `DESIGN.md`.

> **Estado atual:** só existe hoje o app MAUI, com autenticação falsa (lista fixa no código), catálogo de EPIs fixo no código e nenhuma persistência além do PDF gerado. Tudo descrito aqui é o alvo, não o que está construído.

## 1. Visão geral

Três aplicações, cada uma com **backend e frontend próprios**, compartilhando **um único banco PostgreSQL**:

| Aplicação | Quem usa | Função |
|---|---|---|
| **Plataforma** | Equipe CampoSeguro | Cadastra empresas-clientes, administra assinaturas |
| **Painel** | Gestor da empresa | Colaboradores, catálogos de checklist, indicadores |
| **App** | Técnico em campo | Preenche a APR, offline-first |

Cada backend tem suas rotas, sua autenticação e seu ciclo de deploy independente. O que eles compartilham é **o modelo de dados**, que é único por definição: a APR preenchida no app é a mesma que o gestor consulta no painel.

**Por que banco compartilhado e não um por aplicação:** painel e app operam sobre os mesmos registros. Bancos separados exigiriam sincronizar dados entre serviços e conviver com cópias divergentes — complexidade de microsserviço sem o problema que a justifique.

## 2. Estrutura da solução

```
CampoSeguro.Domain        # entidades, regras de negócio, contratos
CampoSeguro.Data          # DbContext, migrações, filtros de tenant
CampoSeguro.Migrator      # único projeto que aplica migrações

CampoSeguro.Platform.Api  +  CampoSeguro.Platform.Web
CampoSeguro.Manager.Api   +  CampoSeguro.Manager.Web
CampoSeguro.Field.Api     +  CampoSeguro.Field.App (MAUI)
```

`Domain` e `Data` são **bibliotecas de classe compartilhadas**. Sem isso, a mesma tabela acabaria com três mapeamentos mantidos em paralelo — e no dia em que divergirem, o dado quebra em silêncio.

**As migrações têm um dono só.** Nenhum dos três backends aplica migração na inicialização: isso os faria disputar o banco e, cedo ou tarde, corromper o estado. O `Migrator` roda no deploy, sozinho.

**Plataforma e Painel são aplicações separadas de propósito.** A Plataforma é a única que enxerga todos os tenants; o Painel é restrito a um. Mantê-las no mesmo projeto é o cenário clássico em que um serviço compartilhado vaza privilégio de uma para a outra.

## 3. Multi-tenancy

**Banco único, schema único, coluna `TenantId` em toda tabela de negócio.**

Três regras inegociáveis:

1. **O `TenantId` vem sempre do token JWT**, nunca do corpo ou da query da requisição. Caso contrário o cliente escolhe de qual empresa quer ler.
2. **Filtro global no EF Core** (`HasQueryFilter`), para que esquecer o `WHERE` seja impossível por padrão, e não uma questão de disciplina do desenvolvedor.
3. **Arquivos particionados por tenant** e servidos por endpoint autorizado, nunca por caminho adivinhável. O filtro do ORM não protege o sistema de arquivos — é por ali que o isolamento costuma furar.

A Plataforma é o único contexto que ignora o filtro. Esse bypass deve ser explícito, concentrado num único ponto do código e inacessível aos outros backends.

## 4. Modelo de dados

Entidades centrais (campos de auditoria `CriadoEm`/`CriadoPor` implícitos em todas):

- **Tenant** — empresa cliente. Nome, documento, situação da assinatura.
- **Usuario** — `Email` (identificador de login), `SenhaHash`, `TenantId`, `Papel` (`AdminPlataforma`, `Gestor`, `Tecnico`), `Ativo`.
- **Catalogo** — conjunto de verificação de uma atividade, por tenant. Ex.: "Telecom — Altura". Versionado.
- **CatalogoItem** — item do checklist. Texto, ordem, `Ativo`. **Nunca excluído**, apenas desativado.
- **Apr** — o registro em si: tenant, autor, catálogo e **versão** usados, tipo de atividade, data, situação.
- **AprItem** — **cópia congelada** de cada item no momento da assinatura: o texto do item e se estava conforme.
- **AprEvidencia** — foto vinculada à APR: caminho do arquivo, dimensões, hash.
- **Convite** — `TokenHash`, `UsuarioId`, `ExpiraEm`, `UsadoEm`.

**A cópia congelada em `AprItem` é o ponto mais importante deste modelo.** Se a APR apenas referenciasse os itens do catálogo, editar o checklist mudaria o conteúdo de todas as APRs passadas — um documento assinado em março passaria a dizer outra coisa em agosto. Sendo documento de segurança do trabalho, isso inviabilizaria qualquer auditoria.

## 5. Identidade e acesso

**Não há auto-cadastro.** O fluxo é sempre por convite:

1. A Plataforma cria o Tenant e o primeiro Gestor.
2. O Gestor cadastra o colaborador no Painel, informando um e-mail (**pode ser pessoal**).
3. O sistema gera um convite e envia o link; o Painel **também exibe o link para o gestor copiar**.
4. O colaborador define a senha pelo link. Só então o login passa a valer.

**O token de convite é material sensível.** Gerado por gerador criptográfico, **guardado com hash** no banco, de uso único e com validade de **72 horas**. Guardá-lo em texto permitiria assumir contas nunca ativadas caso o banco vaze. O mesmo mecanismo atende "esqueci minha senha" — construir uma vez, usar nos dois fluxos.

**Reenvio de convite é funcionalidade da primeira versão**, não melhoria futura: convite expirado sem botão de reenvio vira chamado de suporte.

**Exibir o link no Painel não é contorno, é caminho oficial.** Com e-mail pessoal, Gmail e Hotmail filtram agressivamente; sem SPF e DKIM no domínio remetente, boa parte dos convites vai para spam e o técnico não entra. O gestor repassa pelo canal que já usa com a equipe.

**Unicidade de e-mail vale apenas entre usuários ativos.** O técnico sai da empresa A e entra na B com o mesmo e-mail pessoal — com unicidade global rígida, o gestor da B é bloqueado por um registro de um tenant que ele sequer enxerga, e só a equipe da Plataforma resolveria, caso a caso. Ao desativar o colaborador, a empresa A libera o e-mail.

**Desativação é sempre *soft delete*.** O usuário perde acesso e some do Painel, mas permanece no banco: as APRs que ele assinou precisam manter o autor. Para atender exclusão sob a LGPD, anonimiza-se o contato preservando o registro da APR.

## 6. Sincronização offline

**O app é offline-first.** Torre, poste e área rural são exatamente onde falta sinal — se a APR dependesse de rede, o técnico travaria no início do serviço e acabaria preenchendo de memória depois, pior que papel.

- A APR é gravada em **SQLite local** e vale como registro imediato.
- O **id é um GUID gerado no celular**, não um auto-incremento do banco, para o registro existir antes de qualquer contato com o servidor.
- A sincronização é uma **fila com repetição automática**; o envio é **idempotente** (reenviar o mesmo GUID não duplica).
- O catálogo é baixado e **cacheado localmente**, com a versão registrada em cada APR.

**Autenticação nunca bloqueia a criação de APR.** Credencial validada uma vez, *refresh token* de vida longa no `SecureStorage`; se o token expirar com o técnico sem sinal, ele continua registrando normalmente — a validação acontece na sincronização.

Com mais de 100 técnicos, isso também protege a operação: se o servidor cair numa manhã, no modelo sempre-online a empresa inteira para de emitir APR, que é justamente o documento que não pode faltar.

## 7. Fotos e armazenamento

**O custo mensal do produto é disco, não banco.** Uma foto de celular tem 2–4 MB; a ~150 APRs por dia são mais de 150 GB no primeiro ano.

- **Comprimir no app antes de enviar** — ~1280px, JPEG de qualidade média, algo em torno de 200–400 KB. Para evidência de EPI é suficiente e corta o armazenamento em cerca de dez vezes.
- Arquivos em disco, particionados por tenant; o banco guarda apenas o caminho e o hash.
- Definir **política de retenção** (ex.: fotos por 24 meses, PDF indefinidamente).

## 8. Implantação

Servidor único, com **Docker Compose**: três APIs, três frontends, PostgreSQL e Nginx como proxy reverso, roteando por subdomínio (`admin.`, `gestao.`, `api.`) com HTTPS via Let's Encrypt. São oito processos — dimensione a máquina sabendo disso.

**Requisito que precisa ser levado à TI:** os celulares sincronizam **pela rede móvel, de qualquer lugar**. A API precisa de endereço público com certificado válido. Alternativa por VPN em todos os aparelhos costuma ser pior de operar em campo.

**Servidor único é ponto único de falha.** Aceitável desde que exista **backup automático do PostgreSQL saindo da máquina** — backup no mesmo servidor não é backup. É o item que não deve ser cortado por economia.

## 9. Etapas sugeridas

1. `Domain` + `Data` + `Migrator`, com tenant e filtros globais desde o primeiro commit
2. `Platform.Api` mínima: criar tenant e primeiro gestor
3. `Manager.Api` + `Manager.Web`: colaboradores, convites, catálogos
4. `Field.Api`: autenticação, download de catálogo, recebimento de APR idempotente
5. Adaptar o app MAUI: SQLite local, fila de sincronização, catálogo remoto, compressão de foto
6. Indicadores no Painel
7. Assinaturas na Plataforma

As etapas 1 a 4 são pré-requisito da 5: enquanto não existirem, o app continua com catálogo fixo e autenticação falsa.

## 10. Em aberto

- **Onde o servidor vai ficar** — pendente de conversa com a TI.
- **Indicadores do dashboard** — ainda não definidos. Três saem de graça do modelo e têm valor direto para segurança do trabalho: volume de APRs por período e equipe; percentual com pendência; e **quais itens mais aparecem como não conforme** — este último aponta onde falta EPI ou treinamento.
- **Assinaturas e cobrança** — levantamento adiado.

## 11. Decisões fechadas

- **Identidade visual única.** O produto é CampoSeguro para todas as empresas; não há logo nem cores por cliente. O design system (`DESIGN.md`) segue com cores fixas, sem necessidade de tokens parametrizáveis por tenant. O nome da empresa aparece como *dado* no PDF e no painel — nunca como marca.
