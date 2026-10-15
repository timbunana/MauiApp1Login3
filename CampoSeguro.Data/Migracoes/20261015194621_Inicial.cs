using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampoSeguro.Data.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrador_plataforma",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    email_normalizado = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    senha_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrador_plataforma", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    situacao_assinatura = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant", x => x.id);
                    table.CheckConstraint("ck_tenant_documento", "documento ~ '^[0-9]{14}$'");
                });

            migrationBuilder.CreateTable(
                name: "catalogo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo", x => x.id);
                    table.UniqueConstraint("ak_catalogos_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_catalogo_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    email_normalizado = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    senha_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    papel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    desativado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anonimizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                    table.UniqueConstraint("ak_usuarios_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_usuario_anonimizado", "anonimizado_em IS NULL OR NOT ativo");
                    table.CheckConstraint("ck_usuario_desativado", "ativo OR desativado_em IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_usuario_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalogo_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_item", x => x.id);
                    table.UniqueConstraint("ak_catalogo_itens_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_catalogo_item_catalogo",
                        columns: x => new { x.tenant_id, x.catalogo_id },
                        principalTable: "catalogo",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalogo_item_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalogo_versao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    publicada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_versao", x => x.id);
                    table.UniqueConstraint("ak_catalogo_versoes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_catalogo_versao_numero", "numero >= 1");
                    table.ForeignKey(
                        name: "fk_catalogo_versao_catalogo",
                        columns: x => new { x.tenant_id, x.catalogo_id },
                        principalTable: "catalogo",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalogo_versao_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "convite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convite", x => x.id);
                    table.CheckConstraint("ck_convite_expira", "expira_em > criado_em");
                    table.CheckConstraint("ck_convite_token_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_convite_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_convite_usuario",
                        columns: x => new { x.tenant_id, x.usuario_id },
                        principalTable: "usuario",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessao_dispositivo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    dispositivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessao_dispositivo", x => x.id);
                    table.UniqueConstraint("ak_sessoes_dispositivo_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sessao_dispositivo_token_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_sessao_dispositivo_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessao_dispositivo_usuario",
                        columns: x => new { x.tenant_id, x.usuario_id },
                        principalTable: "usuario",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalogo_versao_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalogo_versao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalogo_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_versao_item", x => x.id);
                    table.CheckConstraint("ck_catalogo_versao_item_ordem", "ordem >= 1");
                    table.ForeignKey(
                        name: "fk_catalogo_versao_item_catalogo_item",
                        columns: x => new { x.tenant_id, x.catalogo_item_id },
                        principalTable: "catalogo_item",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalogo_versao_item_catalogo_versao",
                        columns: x => new { x.tenant_id, x.catalogo_versao_id },
                        principalTable: "catalogo_versao",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalogo_versao_item_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    catalogo_versao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sessao_dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conteudo_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    precisao_metros = table.Column<double>(type: "double precision", nullable: true),
                    local_descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pdf_caminho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pdf_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apr", x => x.id);
                    table.UniqueConstraint("ak_aprs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_apr_conteudo_hash", "octet_length(conteudo_hash) = 32");
                    table.CheckConstraint("ck_apr_coordenadas", "(latitude IS NULL) = (longitude IS NULL)");
                    table.CheckConstraint("ck_apr_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_apr_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_apr_pdf", "pdf_caminho IS NULL OR pdf_hash IS NOT NULL");
                    table.CheckConstraint("ck_apr_pdf_hash", "pdf_hash IS NULL OR octet_length(pdf_hash) = 32");
                    table.CheckConstraint("ck_apr_precisao", "precisao_metros >= 0");
                    table.ForeignKey(
                        name: "fk_apr_autor",
                        columns: x => new { x.tenant_id, x.autor_id },
                        principalTable: "usuario",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_catalogo_versao",
                        columns: x => new { x.tenant_id, x.catalogo_versao_id },
                        principalTable: "catalogo_versao",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_sessao_dispositivo",
                        columns: x => new { x.tenant_id, x.sessao_dispositivo_id },
                        principalTable: "sessao_dispositivo",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_evidencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    largura = table.Column<int>(type: "integer", nullable: false),
                    altura = table.Column<int>(type: "integer", nullable: false),
                    capturada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caminho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expurgada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apr_evidencia", x => x.id);
                    table.CheckConstraint("ck_apr_evidencia_dimensoes", "tamanho_bytes > 0 AND largura > 0 AND altura > 0");
                    table.CheckConstraint("ck_apr_evidencia_expurgo", "expurgada_em IS NULL OR (caminho IS NULL AND recebida_em IS NOT NULL)");
                    table.CheckConstraint("ck_apr_evidencia_hash", "octet_length(hash) = 32");
                    table.CheckConstraint("ck_apr_evidencia_recebida", "caminho IS NULL OR recebida_em IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_apr_evidencia_apr",
                        columns: x => new { x.tenant_id, x.apr_id },
                        principalTable: "apr",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_evidencia_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalogo_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resposta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apr_item", x => x.id);
                    table.CheckConstraint("ck_apr_item_ordem", "ordem >= 1");
                    table.CheckConstraint("ck_apr_item_resposta", "resposta IN ('Conforme', 'NaoConforme', 'NaoAplica')");
                    table.ForeignKey(
                        name: "fk_apr_item_apr",
                        columns: x => new { x.tenant_id, x.apr_id },
                        principalTable: "apr",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_item_catalogo_item",
                        columns: x => new { x.tenant_id, x.catalogo_item_id },
                        principalTable: "catalogo_item",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_apr_item_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_administrador_plataforma_email_normalizado",
                table: "administrador_plataforma",
                column: "email_normalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apr_tenant_id_assinada_em",
                table: "apr",
                columns: new[] { "tenant_id", "assinada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_tenant_id_autor_id_assinada_em",
                table: "apr",
                columns: new[] { "tenant_id", "autor_id", "assinada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_tenant_id_catalogo_versao_id",
                table: "apr",
                columns: new[] { "tenant_id", "catalogo_versao_id" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_tenant_id_sessao_dispositivo_id",
                table: "apr",
                columns: new[] { "tenant_id", "sessao_dispositivo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_evidencia_tenant_id_apr_id",
                table: "apr_evidencia",
                columns: new[] { "tenant_id", "apr_id" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_item_apr_id_catalogo_item_id",
                table: "apr_item",
                columns: new[] { "apr_id", "catalogo_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apr_item_apr_id_ordem",
                table: "apr_item",
                columns: new[] { "apr_id", "ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apr_item_tenant_id_apr_id",
                table: "apr_item",
                columns: new[] { "tenant_id", "apr_id" });

            migrationBuilder.CreateIndex(
                name: "ix_apr_item_tenant_id_catalogo_item_id_resposta",
                table: "apr_item",
                columns: new[] { "tenant_id", "catalogo_item_id", "resposta" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_tenant_id_nome",
                table: "catalogo",
                columns: new[] { "tenant_id", "nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_item_tenant_id_catalogo_id",
                table: "catalogo_item",
                columns: new[] { "tenant_id", "catalogo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_catalogo_id_numero",
                table: "catalogo_versao",
                columns: new[] { "catalogo_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_tenant_id_catalogo_id",
                table: "catalogo_versao",
                columns: new[] { "tenant_id", "catalogo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_catalogo_versao_rascunho",
                table: "catalogo_versao",
                column: "catalogo_id",
                unique: true,
                filter: "publicada_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_item_catalogo_versao_id_catalogo_item_id",
                table: "catalogo_versao_item",
                columns: new[] { "catalogo_versao_id", "catalogo_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_item_catalogo_versao_id_ordem",
                table: "catalogo_versao_item",
                columns: new[] { "catalogo_versao_id", "ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_item_tenant_id_catalogo_item_id",
                table: "catalogo_versao_item",
                columns: new[] { "tenant_id", "catalogo_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_versao_item_tenant_id_catalogo_versao_id",
                table: "catalogo_versao_item",
                columns: new[] { "tenant_id", "catalogo_versao_id" });

            migrationBuilder.CreateIndex(
                name: "ix_convite_tenant_id_usuario_id",
                table: "convite",
                columns: new[] { "tenant_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_convite_token_hash",
                table: "convite",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_convite_pendente",
                table: "convite",
                columns: new[] { "usuario_id", "tipo" },
                unique: true,
                filter: "usado_em IS NULL AND revogado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sessao_dispositivo_tenant_id_usuario_id",
                table: "sessao_dispositivo",
                columns: new[] { "tenant_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sessao_dispositivo_token_hash",
                table: "sessao_dispositivo",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_documento",
                table: "tenant",
                column: "documento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_email_normalizado",
                table: "usuario",
                column: "email_normalizado",
                unique: true,
                filter: "ativo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrador_plataforma");

            migrationBuilder.DropTable(
                name: "apr_evidencia");

            migrationBuilder.DropTable(
                name: "apr_item");

            migrationBuilder.DropTable(
                name: "catalogo_versao_item");

            migrationBuilder.DropTable(
                name: "convite");

            migrationBuilder.DropTable(
                name: "apr");

            migrationBuilder.DropTable(
                name: "catalogo_item");

            migrationBuilder.DropTable(
                name: "catalogo_versao");

            migrationBuilder.DropTable(
                name: "sessao_dispositivo");

            migrationBuilder.DropTable(
                name: "catalogo");

            migrationBuilder.DropTable(
                name: "usuario");

            migrationBuilder.DropTable(
                name: "tenant");
        }
    }
}
