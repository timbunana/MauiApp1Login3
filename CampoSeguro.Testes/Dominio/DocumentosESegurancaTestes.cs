using CampoSeguro.Data.Seguranca;
using CampoSeguro.Domain;

namespace CampoSeguro.Testes.Dominio;

public sealed class CnpjTestes
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    public void Aceita_cnpj_valido_com_ou_sem_mascara(string cnpj) =>
        Assert.True(Cnpj.Valido(Cnpj.Normalizar(cnpj)));

    [Theory]
    [InlineData("11222333000182")] // segundo dígito errado
    [InlineData("11222333000191")] // primeiro dígito errado
    [InlineData("11111111111111")] // sequência repetida passa no cálculo, mas não existe
    [InlineData("1122233300018")]  // 13 dígitos
    [InlineData("")]
    public void Recusa_cnpj_invalido(string cnpj) => Assert.False(Cnpj.Valido(cnpj));

    [Fact]
    public void Normalizar_tira_a_mascara() => Assert.Equal("11222333000181", Cnpj.Normalizar(" 11.222.333/0001-81 "));
}

public sealed class EmailsTestes
{
    [Theory]
    [InlineData("gil@alfa.com.br")]
    [InlineData("  Gil.Souza+obra@Alfa.com.br ")]
    public void Aceita_endereco_simples(string email) => Assert.True(Emails.Valido(email));

    [Theory]
    [InlineData("")]
    [InlineData("gil")]
    [InlineData("gil@")]
    [InlineData("gil@localhost")]
    [InlineData("Gil <gil@alfa.com.br>")]
    [InlineData("a@b.com, c@d.com")]
    public void Recusa_o_que_nao_e_um_endereco_so(string email) => Assert.False(Emails.Valido(email));

    [Fact]
    public void Normalizar_apara_e_poe_em_minusculas() => Assert.Equal("gil@alfa.com.br", Emails.Normalizar("  Gil@Alfa.COM.br "));
}

public sealed class SenhasTestes
{
    [Fact]
    public void Confere_a_senha_certa_e_recusa_a_errada()
    {
        var hash = Senhas.Gerar("senha-longa-de-teste");

        Assert.Equal(ResultadoSenha.Valida, Senhas.Conferir(hash, "senha-longa-de-teste"));
        Assert.Equal(ResultadoSenha.Invalida, Senhas.Conferir(hash, "senha-longa-de-testE"));
    }

    [Fact]
    public void Hash_e_salgado() => Assert.NotEqual(Senhas.Gerar("mesma-senha-123"), Senhas.Gerar("mesma-senha-123"));

    [Fact]
    public void Sem_hash_a_senha_e_sempre_invalida() =>
        Assert.Equal(ResultadoSenha.Invalida, Senhas.Conferir(null, "qualquer-coisa-1"));

    [Fact]
    public void Senha_acima_do_maximo_e_recusada_sem_calcular()
    {
        var gigante = new string('a', Senhas.TamanhoMaximo + 1);
        Assert.Equal(ResultadoSenha.Invalida, Senhas.Conferir(Senhas.Gerar("senha-longa-de-teste"), gigante));
    }

    [Theory]
    [InlineData("curta", true)]
    [InlineData("            ", true)]
    [InlineData("doze-chars-1", false)]
    public void Problema_aponta_senha_fraca(string senha, bool temProblema) =>
        Assert.Equal(temProblema, Senhas.Problema(senha) is not null);
}

public sealed class TokensTestes
{
    [Fact]
    public void Token_tem_256_bits_em_base64_para_url()
    {
        var token = Tokens.Gerar();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.NotEqual(token, Tokens.Gerar());
    }

    [Fact]
    public void Hash_tem_32_bytes_e_e_deterministico()
    {
        var token = Tokens.Gerar();

        Assert.Equal(32, Tokens.Hash(token).Length);
        Assert.Equal(Tokens.Hash(token), Tokens.Hash(token));
    }
}
