using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

/// <summary>
/// Resuelve entre los proveedores registrados. Uno sin credenciales configuradas no se registra, así que
/// pedirlo equivale a pedir uno inexistente.
/// </summary>
public class PaymentProviderResolver : IPaymentProviderResolver
{
    private readonly IReadOnlyDictionary<PaymentProvider, IPaymentProvider> _providers;

    public PaymentProviderResolver(IEnumerable<IPaymentProvider> providers)
    {
        _providers = providers
            .GroupBy(p => p.Provider)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public IPaymentProvider Resolve(PaymentProvider provider) =>
        TryResolve(provider, out var paymentProvider)
            ? paymentProvider!
            : throw ConflictException.WithCode(
                PaymentErrorCodes.ProviderNotSupported,
                $"El proveedor de pagos {provider} no está disponible.");

    public bool TryResolve(PaymentProvider provider, out IPaymentProvider? paymentProvider) =>
        _providers.TryGetValue(provider, out paymentProvider);
}
