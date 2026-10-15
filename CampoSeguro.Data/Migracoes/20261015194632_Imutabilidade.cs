using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampoSeguro.Data.Migracoes
{
    /// <summary>
    /// Garantias de auditoria que não podem depender de disciplina no código: APR assinada e
    /// versão de catálogo publicada não mudam, nem por um backend com bug, nem por SQL manual.
    /// Ficam em migration separada porque o EF não modela triggers; regerar a Inicial não as apaga.
    /// </summary>
    public partial class Imutabilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION fn_bloquear_alteracao() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Registros de % não podem ser alterados nem excluídos.', TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END $$;

                -- APR: só o PDF pode ser anexado depois, e uma vez só.
                CREATE FUNCTION fn_apr_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW.id, NEW.tenant_id, NEW.autor_id, NEW.autor_nome, NEW.catalogo_versao_id,
                        NEW.assinada_em, NEW.recebida_em, NEW.sessao_dispositivo_id, NEW.conteudo_hash,
                        NEW.latitude, NEW.longitude, NEW.precisao_metros, NEW.local_descricao,
                        NEW.observacao, NEW.criado_em, NEW.criado_por)
                       IS DISTINCT FROM
                       (OLD.id, OLD.tenant_id, OLD.autor_id, OLD.autor_nome, OLD.catalogo_versao_id,
                        OLD.assinada_em, OLD.recebida_em, OLD.sessao_dispositivo_id, OLD.conteudo_hash,
                        OLD.latitude, OLD.longitude, OLD.precisao_metros, OLD.local_descricao,
                        OLD.observacao, OLD.criado_em, OLD.criado_por) THEN
                        RAISE EXCEPTION 'APR % já assinada: o conteúdo não pode ser alterado.', OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF OLD.pdf_hash IS NOT NULL AND (NEW.pdf_hash, NEW.pdf_caminho) IS DISTINCT FROM (OLD.pdf_hash, OLD.pdf_caminho) THEN
                        RAISE EXCEPTION 'APR % já tem PDF anexado.', OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE FUNCTION fn_apr_exige_versao_publicada() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM catalogo_versao
                                   WHERE id = NEW.catalogo_versao_id AND publicada_em IS NOT NULL) THEN
                        RAISE EXCEPTION 'APR % referencia versão de catálogo não publicada.', NEW.id
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                -- A cópia congelada precisa ser cópia de verdade: item da versão usada, mesmo texto.
                CREATE FUNCTION fn_apr_item_confere_versao() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM apr a
                                   JOIN catalogo_versao_item cvi ON cvi.catalogo_versao_id = a.catalogo_versao_id
                                   WHERE a.id = NEW.apr_id
                                     AND cvi.catalogo_item_id = NEW.catalogo_item_id
                                     AND cvi.texto = NEW.texto) THEN
                        RAISE EXCEPTION 'Item % não confere com a versão de catálogo da APR %.', NEW.catalogo_item_id, NEW.apr_id
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                -- Evidência: o upload preenche caminho/recebida_em e o expurgo limpa o caminho;
                -- o que identifica a foto (hash, dimensões, APR) não muda.
                CREATE FUNCTION fn_apr_evidencia_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW.id, NEW.tenant_id, NEW.apr_id, NEW.hash, NEW.tamanho_bytes, NEW.largura,
                        NEW.altura, NEW.capturada_em, NEW.criado_em, NEW.criado_por)
                       IS DISTINCT FROM
                       (OLD.id, OLD.tenant_id, OLD.apr_id, OLD.hash, OLD.tamanho_bytes, OLD.largura,
                        OLD.altura, OLD.capturada_em, OLD.criado_em, OLD.criado_por) THEN
                        RAISE EXCEPTION 'Evidência % não pode ter a identificação alterada.', OLD.id
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                -- Versão publicada é o que as APRs citam: congela. Publicar exige ao menos um item.
                CREATE FUNCTION fn_catalogo_versao_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.publicada_em IS NOT NULL THEN
                        RAISE EXCEPTION 'Versão % do catálogo já publicada não pode ser alterada.', OLD.numero
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF TG_OP = 'UPDATE' AND NEW.publicada_em IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM catalogo_versao_item WHERE catalogo_versao_id = NEW.id) THEN
                        RAISE EXCEPTION 'Versão % do catálogo não pode ser publicada sem itens.', NEW.numero
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN CASE TG_OP WHEN 'DELETE' THEN OLD ELSE NEW END;
                END $$;

                CREATE FUNCTION fn_catalogo_versao_item_rascunho() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalogo_versao
                               WHERE publicada_em IS NOT NULL
                                 AND id IN (CASE WHEN TG_OP <> 'INSERT' THEN OLD.catalogo_versao_id END,
                                            CASE WHEN TG_OP <> 'DELETE' THEN NEW.catalogo_versao_id END)) THEN
                        RAISE EXCEPTION 'Itens de versão publicada não podem ser alterados.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN CASE TG_OP WHEN 'DELETE' THEN OLD ELSE NEW END;
                END $$;

                CREATE TRIGGER tg_apr_imutavel BEFORE UPDATE ON apr
                    FOR EACH ROW EXECUTE FUNCTION fn_apr_imutavel();
                CREATE TRIGGER tg_apr_sem_exclusao BEFORE DELETE ON apr
                    FOR EACH ROW EXECUTE FUNCTION fn_bloquear_alteracao();
                CREATE TRIGGER tg_apr_versao_publicada BEFORE INSERT ON apr
                    FOR EACH ROW EXECUTE FUNCTION fn_apr_exige_versao_publicada();

                CREATE TRIGGER tg_apr_item_imutavel BEFORE UPDATE OR DELETE ON apr_item
                    FOR EACH ROW EXECUTE FUNCTION fn_bloquear_alteracao();
                CREATE TRIGGER tg_apr_item_confere_versao BEFORE INSERT ON apr_item
                    FOR EACH ROW EXECUTE FUNCTION fn_apr_item_confere_versao();

                CREATE TRIGGER tg_apr_evidencia_imutavel BEFORE UPDATE ON apr_evidencia
                    FOR EACH ROW EXECUTE FUNCTION fn_apr_evidencia_imutavel();
                CREATE TRIGGER tg_apr_evidencia_sem_exclusao BEFORE DELETE ON apr_evidencia
                    FOR EACH ROW EXECUTE FUNCTION fn_bloquear_alteracao();

                CREATE TRIGGER tg_catalogo_versao_imutavel BEFORE UPDATE OR DELETE ON catalogo_versao
                    FOR EACH ROW EXECUTE FUNCTION fn_catalogo_versao_imutavel();
                CREATE TRIGGER tg_catalogo_versao_item_rascunho BEFORE INSERT OR UPDATE OR DELETE ON catalogo_versao_item
                    FOR EACH ROW EXECUTE FUNCTION fn_catalogo_versao_item_rascunho();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER tg_catalogo_versao_item_rascunho ON catalogo_versao_item;
                DROP TRIGGER tg_catalogo_versao_imutavel ON catalogo_versao;
                DROP TRIGGER tg_apr_evidencia_sem_exclusao ON apr_evidencia;
                DROP TRIGGER tg_apr_evidencia_imutavel ON apr_evidencia;
                DROP TRIGGER tg_apr_item_confere_versao ON apr_item;
                DROP TRIGGER tg_apr_item_imutavel ON apr_item;
                DROP TRIGGER tg_apr_versao_publicada ON apr;
                DROP TRIGGER tg_apr_sem_exclusao ON apr;
                DROP TRIGGER tg_apr_imutavel ON apr;

                DROP FUNCTION fn_catalogo_versao_item_rascunho();
                DROP FUNCTION fn_catalogo_versao_imutavel();
                DROP FUNCTION fn_apr_evidencia_imutavel();
                DROP FUNCTION fn_apr_item_confere_versao();
                DROP FUNCTION fn_apr_exige_versao_publicada();
                DROP FUNCTION fn_apr_imutavel();
                DROP FUNCTION fn_bloquear_alteracao();
                """);
        }
    }
}
