using System.Text.Json.Serialization;

namespace TopRacerLauncher;

/// <summary>Промокод на дополнительные минуты.</summary>
public sealed class PromoCode
{
    public string Code { get; set; } = "";
    public int Minutes { get; set; } = 30;
    public int UsesLeft { get; set; } = 1;
    public DateTime? ExpiresUtc { get; set; }

    [JsonIgnore]
    public string Info => Minutes + " мин  •  осталось: " + UsesLeft
        + (ExpiresUtc.HasValue ? "  •  до " + ExpiresUtc.Value.ToLocalTime().ToString("dd.MM.yyyy") : "");
}

/// <summary>Проверка и погашение промокодов.</summary>
public static class PromoService
{
    public static bool TryRedeem(string rawCode, out int minutes, out string error)
    {
        minutes = 0;
        error = "";

        var code = (rawCode ?? "").Trim();
        if (code.Length == 0)
        {
            error = "Введите код.";
            return false;
        }

        var settings = App.Settings;

        if (settings.RedeemedPromos.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Этот код уже использован на этом компьютере.";
            return false;
        }

        var promo = settings.PromoCodes.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

        if (promo == null)
        {
            error = "Такого промокода нет.";
            return false;
        }

        if (promo.UsesLeft <= 0)
        {
            error = "У промокода закончились использования.";
            return false;
        }

        if (promo.ExpiresUtc.HasValue && promo.ExpiresUtc.Value < DateTime.UtcNow)
        {
            error = "Срок действия промокода истёк.";
            return false;
        }

        promo.UsesLeft--;
        settings.RedeemedPromos.Add(code);
        minutes = promo.Minutes;

        HistoryLog.Add("promo", detail: code + " +" + promo.Minutes + " мин");
        App.SaveState();
        return true;
    }

    /// <summary>Создаёт новый код и сохраняет его.</summary>
    public static PromoCode Create(int minutes, int uses, int daysValid)
    {
        var code = GenerateCode();

        var promo = new PromoCode
        {
            Code = code,
            Minutes = Math.Clamp(minutes, 5, 240),
            UsesLeft = Math.Clamp(uses, 1, 500),
            ExpiresUtc = daysValid > 0 ? DateTime.UtcNow.AddDays(daysValid) : null,
        };

        App.Settings.PromoCodes.Add(promo);
        App.SaveState();

        return promo;
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (int attempt = 0; attempt < 50; attempt++)
        {
            var chars = new char[6];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];

            var code = new string(chars);
            if (!App.Settings.PromoCodes.Any(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)))
                return code;
        }

        return "TOP" + Random.Shared.Next(100, 999);
    }
}
