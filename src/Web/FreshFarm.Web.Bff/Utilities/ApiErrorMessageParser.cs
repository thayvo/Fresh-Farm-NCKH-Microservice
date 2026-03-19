using System.Text.Json;

namespace FreshFarm.Web.Bff.Utilities;

public static class ApiErrorMessageParser
{
    public static async Task<string> ReadMessageAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ExtractMessage(body, fallback);
    }

    public static string ExtractMessage(string? body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        var trimmed = body.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            var itemValue = item.GetString();
                            if (!string.IsNullOrWhiteSpace(itemValue))
                            {
                                return itemValue.Trim();
                            }
                        }
                    }
                }
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message.Trim();
                }
            }

            if (root.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrWhiteSpace(title) &&
                    !string.Equals(title, "One or more validation errors occurred.", StringComparison.OrdinalIgnoreCase))
                {
                    return title.Trim();
                }
            }
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return fallback;
    }

    public static bool TryExtractLockedUntilUtc(string? body, out DateTime lockedUntilUtc)
    {
        lockedUntilUtc = default;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var trimmed = body.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (!root.TryGetProperty("lockedUntilUtc", out var lockedUntilElement))
            {
                return false;
            }

            var raw = lockedUntilElement.GetString();
            return DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out lockedUntilUtc);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
