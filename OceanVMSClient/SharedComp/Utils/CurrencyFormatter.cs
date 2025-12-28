using System.Globalization;

namespace OceanVMSClient.SharedComp.Utils
{
    public static class CurrencyFormatter
    {
        // Format nullable amounts, return "—" when null.
        // symbol: currency symbol (default ₹). culture: optional CultureInfo (default = CurrentCulture).
        public static string Format(decimal? amount, string symbol = "₹", CultureInfo? culture = null)
        {
            if (!amount.HasValue)
                return "—";

            var ci = culture ?? CultureInfo.CurrentCulture;
            return $"{symbol}{amount.Value.ToString("N2", ci)}";
        }
    }
}