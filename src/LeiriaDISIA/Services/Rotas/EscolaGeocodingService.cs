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

    /// <summary>Geocodifica a escola e calcula a distância à sede, gravando ambos os resultados na
    /// Escola (mas SEM chamar <c>SaveChanges</c> — fica ao critério do chamador decidir quando
    /// gravar, ex.: em conjunto com outras alterações do formulário de Editar Escola).</summary>
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

    /// <summary>Verificação de sanidade grosseira — ver a mesma verificação (e a explicação
    /// completa) em <see cref="PlaneamentoRotaService.EstaDentroDePortugalContinental"/>. Duplicada
    /// aqui (2 linhas) em vez de partilhada, para este ficheiro não depender de outro só por causa
    /// de um método tão pequeno.</summary>
    private static bool EstaDentroDePortugalContinental(double latitude, double longitude) =>
        latitude is >= 36.8 and <= 42.2 && longitude is >= -9.6 and <= -6.1;
}
