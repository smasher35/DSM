using LeiriaDISIA.Data;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services.Rotas;

/// <summary>
/// Geocodifica uma <see cref="Escola"/> (morada → latitude/longitude) e calcula a distância
/// rodoviária real até à sede do Município de Leiria, gravando o resultado na própria Escola (cache
/// — ver <see cref="Escola.DistanciaKmSede"/>) para nunca repetir a chamada externa sem necessidade.
/// Só corre quando explicitamente pedido pelo utilizador (nunca automaticamente ao abrir uma grelha
/// ou listagem) — ver Views/EscolaEditWindow.xaml.cs, botão "Recalcular Distância".
/// </summary>
public class EscolaGeocodingService
{
    private readonly AppDbContext _db;
    private readonly IGeocodingService _geocoding;
    private readonly IRoutingService _routing;

    // Coordenadas da sede (Largo da República) já não são geocodificadas em runtime — ver
    // EnderecoSedeMunicipio.Latitude/Longitude e o comentário lá. Mantém-se aqui um cache estático
    // simples só para não instanciar o record repetidamente.
    private static readonly CoordenadaGeografica CoordenadaSede = new(EnderecoSedeMunicipio.Latitude, EnderecoSedeMunicipio.Longitude);

    public EscolaGeocodingService(AppDbContext db) : this(db, new OpenRouteServiceClient())
    {
    }

    public EscolaGeocodingService(AppDbContext db, OpenRouteServiceClient clienteOpenRouteService)
    {
        _db = db;
        _geocoding = clienteOpenRouteService;
        _routing = clienteOpenRouteService;
    }

    /// <summary>Devolve as coordenadas fixas da sede (ver <see cref="EnderecoSedeMunicipio"/>) — já
    /// não faz nenhuma chamada externa. Mantém a assinatura assíncrona (e o par Coordenada/Erro)
    /// para não obrigar a alterar os chamadores existentes; o "Erro" é sempre <c>null</c> agora.</summary>
    public Task<(CoordenadaGeografica? Coordenada, string? Erro)> ObterCoordenadaSedeAsync(CancellationToken ct = default)
        => Task.FromResult<(CoordenadaGeografica?, string?)>((CoordenadaSede, null));

    /// <summary>Junta Morada + Código Postal + Localidade + ", Portugal", omitindo as partes que
    /// estiverem vazias — quanto mais completa a morada (idealmente com código postal), maior a
    /// confiança da geocodificação.</summary>
    public static string MontarMoradaCompleta(Escola escola)
    {
        var partes = new[] { escola.Morada, escola.CodigoPostal, escola.Localidade }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var morada = string.Join(", ", partes);
        return string.IsNullOrWhiteSpace(morada) ? string.Empty : $"{morada}, Portugal";
    }

    /// <summary>Geocodifica a escola a partir da MORADA (texto) e calcula a distância à sede — ver
    /// <see cref="RecalcularAPartirDeCoordenadasAsync"/> para o fluxo inverso (coordenadas → morada),
    /// preferível sempre que já se tenham coordenadas GPS exatas, por serem uma fonte mais fiável
    /// do que adivinhar a partir de texto (ver o comentário completo em
    /// <see cref="RecalcularAPartirDeCoordenadasAsync"/>). Grava ambos os resultados na Escola (mas
    /// SEM chamar <c>SaveChanges</c> — fica ao critério do chamador decidir quando gravar, ex.: em
    /// conjunto com outras alterações do formulário de Editar Escola).</summary>
    public async Task<(bool Sucesso, string? Erro)> RecalcularAsync(Escola escola, CancellationToken ct = default)
    {
        var moradaCompleta = MontarMoradaCompleta(escola);
        if (string.IsNullOrWhiteSpace(moradaCompleta))
            return (false, "Esta escola não tem morada preenchida — preencha a morada antes de calcular a distância.");

        var geocodificacao = await _geocoding.GeocodificarAsync(moradaCompleta, ct);
        if (!geocodificacao.Sucesso) return (false, geocodificacao.MensagemErro);

        // Nomes de rua comuns a várias terras de Portugal podem, raramente, ser geocodificados para
        // o sítio errado (ex.: "Rua da Escola" existe em dezenas de concelhos) — validar aqui, antes
        // de gravar, evita guardar uma coordenada disparatada que só seria detetada mais tarde, de
        // forma confusa, ao tentar planear uma rota com ela (ver PlaneamentoRotaService).
        if (!EstaDentroDePortugalContinental(geocodificacao.Coordenada!.Latitude, geocodificacao.Coordenada.Longitude))
            return (false,
                $"A morada foi encontrada, mas fora de Portugal continental (Latitude {geocodificacao.Coordenada.Latitude:F4}, " +
                $"Longitude {geocodificacao.Coordenada.Longitude:F4}) — o serviço de geocodificação provavelmente confundiu-a " +
                "com um local homónimo noutra zona do país. Reveja a morada (idealmente com código postal) e tente novamente.");

        var (coordenadaSede, erroSede) = await ObterCoordenadaSedeAsync(ct);
        if (coordenadaSede == null) return (false, erroSede);

        var distancia = await _routing.CalcularDistanciaAsync(geocodificacao.Coordenada!, coordenadaSede, ct);
        if (!distancia.Sucesso) return (false, distancia.MensagemErro);

        escola.Latitude = geocodificacao.Coordenada!.Latitude;
        escola.Longitude = geocodificacao.Coordenada.Longitude;
        escola.DistanciaKmSede = distancia.DistanciaKm;
        escola.DataUltimoCalculoDistancia = DateTime.Now;

        return (true, null);
    }

    /// <summary>Calcula a distância à sede a partir de COORDENADAS GPS exatas fornecidas
    /// diretamente pelo utilizador (ex.: coladas do Google Maps, ou de um GPS de campo) — nunca
    /// chama o serviço de geocodificação por morada. Uma morada por extenso ("Rua X, nº Y") é
    /// ambígua sempre que o mesmo nome de rua exista noutro concelho (frequente em Portugal — ex.:
    /// "Rua da Escola" existe em dezenas de sítios), e o serviço de geocodificação pode acertar no
    /// concelho errado sem forma de o próprio serviço se aperceber; coordenadas GPS exatas não têm
    /// essa ambiguidade nenhuma — são a fonte da verdade.
    ///
    /// Depois de calcular a distância, tenta preencher Morada/Código Postal/Localidade a partir das
    /// próprias coordenadas (geocodificação inversa) — como comodidade, para não ter de escrever a
    /// morada à mão depois de fornecer as coordenadas; se a geocodificação inversa não encontrar
    /// morada nenhuma (coordenada em zona rural/isolada), os campos de endereço ficam como
    /// estavam — isso nunca impede o cálculo da distância, que só depende das coordenadas.
    ///
    /// Tal como <see cref="RecalcularAsync"/>, grava os resultados na Escola mas SEM chamar
    /// <c>SaveChanges</c> — fica ao critério do chamador.</summary>
    public async Task<(bool Sucesso, string? Erro)> RecalcularAPartirDeCoordenadasAsync(
        Escola escola, CoordenadaGeografica coordenada, CancellationToken ct = default)
    {
        // Verificação de sanidade grosseira, tal como em RecalcularAsync — aqui serve sobretudo
        // para apanhar enganos de digitação (ex.: esquecer o sinal negativo da longitude, ou
        // trocar latitude com longitude), já que a coordenada em si veio diretamente do
        // utilizador, não de uma adivinhação por texto.
        if (!EstaDentroDePortugalContinental(coordenada.Latitude, coordenada.Longitude))
            return (false,
                $"As coordenadas fornecidas (Latitude {coordenada.Latitude:F4}, Longitude {coordenada.Longitude:F4}) " +
                "caem fora de Portugal continental — confirme se não trocou latitude com longitude, ou se não falta " +
                "o sinal negativo na longitude (em Portugal a longitude é sempre negativa).");

        var (coordenadaSede, erroSede) = await ObterCoordenadaSedeAsync(ct);
        if (coordenadaSede == null) return (false, erroSede);

        var distancia = await _routing.CalcularDistanciaAsync(coordenada, coordenadaSede, ct);
        if (!distancia.Sucesso) return (false, distancia.MensagemErro);

        escola.Latitude = coordenada.Latitude;
        escola.Longitude = coordenada.Longitude;
        escola.DistanciaKmSede = distancia.DistanciaKm;
        escola.DataUltimoCalculoDistancia = DateTime.Now;

        // A geocodificação inversa é só uma comodidade — se falhar (sem rede, serviço em baixo,
        // etc.) ou não encontrar morada nenhuma, isso não deve impedir o resultado principal (a
        // distância, que já foi calculada e gravada acima com sucesso); por isso os seus erros
        // nunca fazem este método devolver Sucesso = false.
        var enderecoInverso = await _geocoding.GeocodificarInversoAsync(coordenada, ct);
        if (enderecoInverso.Sucesso)
        {
            if (!string.IsNullOrWhiteSpace(enderecoInverso.Morada)) escola.Morada = enderecoInverso.Morada;
            if (!string.IsNullOrWhiteSpace(enderecoInverso.CodigoPostal)) escola.CodigoPostal = enderecoInverso.CodigoPostal;
            if (!string.IsNullOrWhiteSpace(enderecoInverso.Localidade)) escola.Localidade = enderecoInverso.Localidade;
        }

        return (true, null);
    }

    /// <summary>Verificação de sanidade grosseira — ver a mesma verificação (e a explicação
    /// completa) em <see cref="PlaneamentoRotaService.EstaDentroDePortugalContinental"/>. Duplicada
    /// aqui (2 linhas) em vez de partilhada, para este ficheiro não depender de outro só por causa
    /// de um método tão pequeno.</summary>
    private static bool EstaDentroDePortugalContinental(double latitude, double longitude) =>
        latitude is >= 36.8 and <= 42.2 && longitude is >= -9.6 and <= -6.1;
}
