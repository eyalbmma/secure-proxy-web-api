using System.Text.Json;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

public sealed class RequestValidator
{
    public RequestValidationResult Validate(Operation operation, JsonElement? json)
    {
        if (operation.ParameterRules.Any(rule => rule.Location != ParameterLocation.Body))
        {
            // Path/query binding depends on the unresolved urlId request semantics.
            return new RequestValidationResult(false, true, "unsupported_parameter_location");
        }

        if (json is null || json.Value.ValueKind != JsonValueKind.Object)
        {
            return new RequestValidationResult(false, false, "json_must_be_object");
        }

        var properties = json.Value.EnumerateObject().ToArray();
        if (properties
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return new RequestValidationResult(false, false, "duplicate_parameter");
        }

        var bodyRules = operation.ParameterRules
            .Where(rule => rule.Location == ParameterLocation.Body)
            .ToArray();

        if (!operation.AllowUnknownParameters &&
            properties.Any(property => !bodyRules.Any(rule =>
                string.Equals(rule.ParameterName, property.Name, StringComparison.OrdinalIgnoreCase))))
        {
            return new RequestValidationResult(false, false, "unknown_parameter");
        }

        foreach (var rule in bodyRules)
        {
            if (string.IsNullOrWhiteSpace(rule.ParameterName) ||
                rule.MaximumLength is < 0 ||
                (rule.MinimumValue.HasValue && rule.MaximumValue.HasValue &&
                 rule.MinimumValue.Value > rule.MaximumValue.Value))
            {
                return new RequestValidationResult(false, true, "invalid_parameter_rule");
            }

            var matches = properties
                .Where(property => string.Equals(
                    rule.ParameterName,
                    property.Name,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
            {
                if (rule.IsRequired)
                {
                    return new RequestValidationResult(false, false, "required_parameter_missing");
                }

                continue;
            }

            if (!MatchesRule(rule, matches[0].Value, out var configurationIssue))
            {
                return new RequestValidationResult(
                    false,
                    configurationIssue,
                    configurationIssue ? "invalid_parameter_rule" : "parameter_value_invalid");
            }
        }

        return new RequestValidationResult(true, false);
    }

    private static bool MatchesRule(ParameterRule rule, JsonElement value, out bool configurationIssue)
    {
        configurationIssue = false;
        var dataType = rule.DataType.Trim().ToLowerInvariant();

        switch (dataType)
        {
            case "string":
                if (rule.MinimumValue.HasValue || rule.MaximumValue.HasValue)
                {
                    configurationIssue = true;
                    return false;
                }

                return value.ValueKind == JsonValueKind.String &&
                    (rule.MaximumLength is null || value.GetString()!.Length <= rule.MaximumLength.Value);

            case "integer":
            case "int":
                if (!SupportsNumericRules(rule, out configurationIssue))
                {
                    return false;
                }

                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
                {
                    return false;
                }

                return WithinRange(integer, rule);

            case "number":
            case "decimal":
                if (!SupportsNumericRules(rule, out configurationIssue))
                {
                    return false;
                }

                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
                {
                    return false;
                }

                return WithinRange(number, rule);

            case "boolean":
            case "bool":
                if (HasUnsupportedLengthOrRange(rule))
                {
                    configurationIssue = true;
                    return false;
                }

                return value.ValueKind is JsonValueKind.True or JsonValueKind.False;

            case "object":
                if (HasUnsupportedLengthOrRange(rule))
                {
                    configurationIssue = true;
                    return false;
                }

                return value.ValueKind == JsonValueKind.Object;

            case "array":
                if (HasUnsupportedLengthOrRange(rule))
                {
                    configurationIssue = true;
                    return false;
                }

                return value.ValueKind == JsonValueKind.Array;

            default:
                configurationIssue = true;
                return false;
        }
    }

    private static bool SupportsNumericRules(ParameterRule rule, out bool configurationIssue)
    {
        configurationIssue = rule.MaximumLength.HasValue;
        return !configurationIssue;
    }

    private static bool HasUnsupportedLengthOrRange(ParameterRule rule) =>
        rule.MaximumLength.HasValue || rule.MinimumValue.HasValue || rule.MaximumValue.HasValue;

    private static bool WithinRange(decimal value, ParameterRule rule) =>
        (!rule.MinimumValue.HasValue || value >= rule.MinimumValue.Value) &&
        (!rule.MaximumValue.HasValue || value <= rule.MaximumValue.Value);
}
