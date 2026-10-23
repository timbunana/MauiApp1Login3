namespace CampoSeguro.Manager.Api.Catalogos;

/// <summary>
/// Pontos de partida oferecidos a toda empresa nova. Os textos são os do <c>CatalogoEpis</c> do app,
/// que até a etapa 5 ainda usa a lista fixa: mantê-los iguais evita que a mesma verificação apareça
/// com duas redações durante a transição. Importar cria um <b>rascunho</b>; quem publica é a empresa,
/// depois de revisar, porque o checklist é dela.
/// </summary>
public static class Modelos
{
    public sealed record Modelo(string Codigo, string Nome, string Descricao, IReadOnlyList<string> Itens);

    private const string Capacete = "Uso de Capacete com Jugular";
    private const string Luvas = "Luvas de Proteção Adequadas";
    private const string Bota = "Bota de Segurança com Biqueira";
    private const string Oculos = "Óculos de Proteção";

    public static readonly IReadOnlyList<Modelo> Todos =
    [
        new("telecom-altura", "Telecom — Altura", "Trabalho em altura: postes, torres e escadas.",
        [
            Capacete,
            "Cinto Tipo Paraquedista e Talabarte",
            Luvas,
            Bota,
            Oculos,
            "Sinalização de Segurança na Área",
            "Escada Inspecionada e Amarrada",
            "Ferramentas Isoladas/Em Bom Estado",
            "Ausência de Cabos Elétricos Desencapados",
            "Condições Climáticas Favoráveis",
        ]),
        new("telecom-mesmo-nivel", "Telecom — Mesmo Nível", "Trabalho no nível do solo.",
        [
            Capacete,
            Luvas,
            Bota,
            Oculos,
        ]),
    ];

    public static Modelo? PorCodigo(string codigo) => Todos.FirstOrDefault(m => m.Codigo == codigo);
}
