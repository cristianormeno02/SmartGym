using System;
using System.ComponentModel.DataAnnotations;

namespace SmartGym.Application.Common;

public class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Minutos durante los que un checkout externo puede pagarse antes de cancelarse por vencimiento.</summary>
    [Range(5, 10080)]
    public int CheckoutExpirationMinutes { get; set; } = 60;

    public TimeSpan ExpirationCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}
