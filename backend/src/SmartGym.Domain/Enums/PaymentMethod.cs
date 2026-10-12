namespace SmartGym.Domain.Enums;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    CreditCard = 3,
    DebitCard = 4,
    // Conserva el entero del antiguo valor MercadoPago: el proveedor se modela aparte en PaymentProvider.
    DigitalWallet = 5,
    Other = 6
}
