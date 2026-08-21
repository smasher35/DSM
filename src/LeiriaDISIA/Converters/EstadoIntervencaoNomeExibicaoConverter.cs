using System.Globalization;
using System.Windows.Data;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Converters;

/// <summary>
/// Equivalente a <see cref="EstadoPedidoNomeExibicaoConverter"/>, mas para
/// <see cref="EstadoIntervencao"/> — mostra o nome de exibição configurado em
/// Administração → Dados Fixos → "Estados das Intervenções".
/// </summary>
public class EstadoIntervencaoNomeExibicaoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is EstadoIntervencao estado ? EstadoCores.NomeExibicaoEstadoIntervencao(estado) : value ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
