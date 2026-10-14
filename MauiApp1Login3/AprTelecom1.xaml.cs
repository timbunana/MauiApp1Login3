using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Define um apelido para evitar o conflito de classes 'Colors' entre MAUI e QuestPDF
using QColors = QuestPDF.Helpers.Colors;

namespace MauiApp1Login3;

public partial class AprTelecom1 : ContentPage
{
    private const string TrabalhoMesmoNivel = "Trabalho no Mesmo Nível";
    private const string TrabalhoEmAltura = "Trabalho em Altura";

    // Fonte única dos itens de verificação: alimenta tanto a tela quanto o PDF.
    private static readonly (string Nome, bool AplicaMesmoNivel)[] CatalogoEpis =
    {
        ("Uso de Capacete com Jugular", true),
        ("Cinto Tipo Paraquedista e Talabarte", false),
        ("Luvas de Proteção Adequadas", true),
        ("Bota de Segurança com Biqueira", true),
        ("Óculos de Proteção", true),
        ("Sinalização de Segurança na Área", false),
        ("Escada Inspecionada e Amarrada", false),
        ("Ferramentas Isoladas/Em Bom Estado", false),
        ("Ausência de Cabos Elétricos Desencapados", false),
        ("Condições Climáticas Favoráveis", false),
    };

    private List<ItemEpi> itensDaAtividade = new();
    private byte[]? fotoEvidencia;
    private string tipoAtividadeSelecionada = string.Empty;

    public AprTelecom1()
    {
        InitializeComponent();

        // Configuração obrigatória da licença comunitária do QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private void OnMesmoNivelClicked(object sender, EventArgs e) => SelecionarTipo(TrabalhoMesmoNivel);

    private void OnAlturaClicked(object sender, EventArgs e) => SelecionarTipo(TrabalhoEmAltura);

    private void SelecionarTipo(string tipo)
    {
        tipoAtividadeSelecionada = tipo;

        bool mesmoNivel = tipo == TrabalhoMesmoNivel;
        BtnMesmoNivel.Style = ObterEstilo(mesmoNivel ? "PrimaryButton" : "SecondaryButton");
        BtnAltura.Style = ObterEstilo(mesmoNivel ? "SecondaryButton" : "PrimaryButton");
        BtnMesmoNivel.HeightRequest = 48;
        BtnAltura.HeightRequest = 48;

        MontarChecklist();
    }

    private static Style ObterEstilo(string chave) => (Style)Application.Current!.Resources[chave];

    // Monta a lista aplicável à atividade escolhida. Tela e PDF leem desta mesma coleção,
    // então o relatório nunca diverge do que foi exibido.
    private void MontarChecklist()
    {
        bool mesmoNivel = tipoAtividadeSelecionada == TrabalhoMesmoNivel;

        itensDaAtividade = CatalogoEpis
            .Where(epi => !mesmoNivel || epi.AplicaMesmoNivel)
            .Select((epi, indice) => new ItemEpi(indice + 1, epi.Nome))
            .ToList();

        BindableLayout.SetItemsSource(ListaEpis, itensDaAtividade);
        SubtituloLabel.Text = $"{tipoAtividadeSelecionada} · {itensDaAtividade.Count} itens";
        ChecklistCard.IsVisible = true;
    }

    private async Task<bool> GarantirPermissaoCameraAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            await Dialogo.AvisarAsync(
                "Permissão necessária",
                "Para registrar a evidência é preciso liberar o acesso à câmera nas configurações do aparelho.");
            return false;
        }

        return true;
    }

    // Método acionado ao clicar no botão da câmera
    private async void OnTirarFotoClicked(object sender, EventArgs e)
    {
        // O MediaPicker abre o app de câmera do sistema, que é quem oferece frontal/traseira —
        // por isso aqui a escolha é apenas entre câmera e galeria.
        string? acao = await Dialogo.PerguntarAsync(
            "Evidência",
            "De onde vem a foto?",
            new[] { "Tirar foto", "Escolher da galeria" },
            "Cancelar");

        if (acao is null or "Cancelar")
            return;

        try
        {
            FileResult? photo;

            if (acao == "Escolher da galeria")
            {
                photo = await MediaPicker.Default.PickPhotoAsync();
            }
            else
            {
                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    await Dialogo.AvisarAsync(
                        "Indisponível",
                        "Este aparelho não suporta captura de foto pela câmera.");
                    return;
                }

                if (!await GarantirPermissaoCameraAsync())
                    return;

                photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Foto da Evidência" });
            }

            if (photo != null)
            {
                // Copia os bytes de imediato: o arquivo temporário do MediaPicker pode sumir e
                // um Stream aberto aqui já estaria descartado quando a imagem fosse renderizada.
                byte[] bytes;
                using (Stream stream = await photo.OpenReadAsync())
                using (var memoria = new MemoryStream())
                {
                    await stream.CopyToAsync(memoria);
                    bytes = memoria.ToArray();
                }

                fotoEvidencia = bytes;
                FotoEvidenciaImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                FotoBorder.IsVisible = true;
                FotoBtn.Text = "Trocar evidência";
            }
        }
        catch (Exception ex)
        {
            await Dialogo.AvisarAsync("Erro", $"Não foi possível obter a imagem: {ex.Message}");
        }
    }

    // Método para Salvar os dados e gerar o PDF, entregue pelo compartilhamento do sistema
    private async void OnSalvarClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(tipoAtividadeSelecionada))
        {
            await Dialogo.AvisarAsync(
                "Falta escolher",
                "Informe se a atividade é no mesmo nível ou em altura antes de salvar.");
            return;
        }

        string nomeUsuario = txtNome.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nomeUsuario))
        {
            await Dialogo.AvisarAsync(
                "Falta a identificação",
                "Preencha o nome de quem está assinando a APR.");
            return;
        }

        // Limpa caracteres especiais do nome para evitar erros ao salvar o arquivo
        string nomeArquivoSeguro = string.Concat(nomeUsuario.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim().Replace(" ", "_");
        string dataHoraStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"APR_{nomeArquivoSeguro}_{dataHoraStr}.pdf";

        bool todosOk = itensDaAtividade.All(i => i.Marcado);

        try
        {
            byte[]? fotoBytes = fotoEvidencia;

            // Criação do documento PDF estruturado via QuestPDF em memória
            using var pdfMemoryStream = new MemoryStream();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(QColors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(QColors.Grey.Darken3));

                    page.Header().Text("Relatório de APR - Telecomunicações")
                        .SemiBold().FontSize(18).FontColor(QColors.Blue.Medium);

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text($"Colaborador: {nomeUsuario}");
                        col.Item().Text($"Tipo de Atividade: {tipoAtividadeSelecionada}");
                        col.Item().Text($"Data e Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                        col.Item().Text($"Status Geral: {(todosOk ? "Todos os itens OK" : "Pendências encontradas")}");

                        col.Item().LineHorizontal(1).LineColor(QColors.Grey.Lighten1);

                        col.Item().Text("Itens de Verificação:").Bold();
                        foreach (var item in itensDaAtividade)
                        {
                            string statusTexto = item.Marcado ? "Conforme (OK)" : "Não Conforme / Desligado";
                            col.Item().Text($"• {item.Nome}: {statusTexto}");
                        }

                        if (fotoBytes != null)
                        {
                            col.Item().PaddingTop(10).Text("Evidência Fotográfica:").Bold();
                            col.Item().MaxWidth(250).MaxHeight(250).Image(fotoBytes);
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Gerado por aplicativo MAUI - Página ");
                        x.CurrentPageNumber();
                    });
                });
            })
            .GeneratePdf(pdfMemoryStream);

            pdfMemoryStream.Position = 0;

            // Grava na área privada do app: funciona em todas as plataformas e sem pedir
            // permissão de armazenamento (o Android bloqueia escrita direta em pastas públicas).
            string filePath = Path.Combine(FileSystem.Current.AppDataDirectory, fileName);

            using (var fileStream = File.Create(filePath))
            {
                await pdfMemoryStream.CopyToAsync(fileStream);
            }

            // Entrega o arquivo pelo compartilhamento do sistema (e-mail, WhatsApp, Drive...),
            // já que a pasta do app não é navegável.
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Relatório de APR",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            await Dialogo.AvisarAsync("Erro", $"Não foi possível gerar o PDF: {ex.Message}");
        }
    }

    // Método para o botão Sair
    private async void OnSairClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
