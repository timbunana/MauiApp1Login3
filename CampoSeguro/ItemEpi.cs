namespace CampoSeguro;

// Um item do checklist de APR. A lista de itens vive em AprTelecom1.CatalogoEpis;
// aqui fica só o que a tela e o PDF precisam ler de cada um.
public sealed class ItemEpi
{
    public ItemEpi(int numero, string nome)
    {
        Numero = numero;
        Nome = nome;
    }

    public int Numero { get; }

    public string Nome { get; }

    public string Rotulo => $"{Numero}. {Nome}";

    // Two-way com o Switch do template.
    public bool Marcado { get; set; }
}
