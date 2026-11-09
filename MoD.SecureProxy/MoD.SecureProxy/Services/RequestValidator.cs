using System.Text.Json;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

/// <summary>Validates caller JSON against server-owned parameter rules and separates values by location.</summary>
public sealed class RequestValidator
{
    public RequestValidationResult Validate(Operation operation, JsonElement? json)
    {
        if (json is null || json.Value.ValueKind != JsonValueKind.Object)
        {
            return Invalid("json_must_be_object");
        }

        var properties = json.Value.EnumerateObject().ToArray();
        if (properties.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return Invalid("duplicate_parameter");
        }

        var rules = operation.ParameterRules.ToArray();
        if (rules.Any(rule => string.IsNullOrWhiteSpace(rule.ParameterName)) ||
            rules.GroupBy(rule => rule.ParameterName, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            return ConfigurationError("invalid_parameter_rule");
        }

        var urlIdRule = rules.SingleOrDefault(rule => string.Equals(
            rule.ParameterName, "urlId", StringComparison.OrdinalIgnoreCase));
        if (urlIdRule is null || urlIdRule.Location != ParameterLocation.Path || !urlIdRule.IsRequired)
        {
            return ConfigurationError("url_id_path_rule_required");
        }

        if (!PathTemplateMatchesRules(operation.RelativePath, rules))
        {
            return ConfigurationError("invalid_path_template");
        }

        var ruleNames = rules.Select(rule => rule.ParameterName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!operation.AllowUnknownParameters && properties.Any(property => !ruleNames.Contains(property.Name)))
        {
            return Invalid("unknown_parameter");
        }

        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var path = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in properties)
        {
            var rule = rules.FirstOrDefault(candidate => string.Equals(
                candidate.ParameterName, property.Name, StringComparison.OrdinalIgnoreCase));
            // Unknown properties are body parameters only when the operation explicitly allows them.
            if (rule is null)
            {
                body[property.Name] = property.Value;
            }
        }

        foreach (var rule in rules)
        {
            if (!IsRuleConfigurationValid(rule))
            {
                return ConfigurationError("invalid_parameter_rule");
            }

            var matches = properties.Where(property => string.Equals(
                rule.ParameterName, property.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                if (rule.IsRequired)
                {
                    return Invalid("required_parameter_missing");
                }

                continue;
            }

            var value = matches[0].Value;
            if (!MatchesRule(rule, value, out var configurationIssue))
            {
                return configurationIssue
                    ? ConfigurationError("invalid_parameter_rule")
                    : Invalid("parameter_value_invalid");
            }

            switch (rule.Location)
            {
                case ParameterLocation.Body:
                    body[rule.ParameterName] = value;
                    break;
                case ParameterLocation.Path:
                    if (!TryGetScalar(value, out var pathValue) ||
                        !IsSafePathSegment(pathValue) ||
                        pathValue.Length > (rule.MaximumLength ?? 2048))
                    {
                        return Invalid("parameter_value_invalid");
                    }

                    path[rule.ParameterName] = pathValue;
                    break;
                case ParameterLocation.Query:
                    if (!TryGetScalar(value, out var queryValue) ||
                        queryValue.Any(char.IsControl) ||
                        queryValue.Length > (rule.MaximumLength ?? 4096))
                    {
                        return Invalid("parameter_value_invalid");
                    }

                    query[rule.ParameterName] = queryValue;
                    break;
                default:
                    return ConfigurationError("invalid_parameter_rule");
            }
        }

        return new RequestValidationResult(
            true,
            false,
            BodyJson: JsonSerializer.SerializeToUtf8Bytes(body),
            PathParameters: path,
            QueryParameters: query);
    }

    private static bool PathTemplateMatchesRules(string? relativePath, IReadOnlyCollection<ParameterRule> rules)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var placeholders = System.Text.RegularExpressions.Regex.Matches(relativePath, "\\{([A-Za-z][A-Za-z0-9_]*)\\}")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Groups[1].Value)
            .ToArray();
        if (relativePath.Contains('{') || relativePath.Contains('}'))
        {
            // Only exact, simple placeholders are accepted; escaped or malformed braces are configuration errors.
            var recognized = System.Text.RegularExpressions.Regex.Replace(
                relativePath, "\\{[A-Za-z][A-Za-z0-9_]*\\}", string.Empty);
            if (recognized.Contains('{') || recognized.Contains('}'))
            {
                return false;
            }
        }

        var pathRules = rules.Where(rule => rule.Location == ParameterLocation.Path).ToArray();
        return placeholders.Length == pathRules.Length &&
            placeholders.Distinct(StringComparer.OrdinalIgnoreCase).Count() == placeholders.Length &&
            placeholders.All(name => pathRules.Any(rule => string.Equals(
                name, rule.ParameterName, StringComparison.OrdinalIgnoreCase))) &&
            pathRules.All(rule => placeholders.Any(name => string.Equals(
                name, rule.ParameterName, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsRuleConfigurationValid(ParameterRule rule)
    {
        if (rule.MaximumLength is < 0 ||
            (rule.MinimumValue.HasValue && rule.MaximumValue.HasValue &&
             rule.MinimumValue.Value > rule.MaximumValue.Value) ||
            (rule.Location == ParameterLocation.Path && !rule.IsRequired))
        {
            return false;
        }

        if ((rule.Location is ParameterLocation.Path or ParameterLocation.Query) &&
            rule.DataType.Trim().ToLowerInvariant() is not ("string" or "integer" or "int" or "number" or "decimal" or "boolean" or "bool"))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetScalar(JsonElement value, out string scalar)
    {
        scalar = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => string.Empty
        };

        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
            JsonValueKind.True or JsonValueKind.False;
    }

    private static bool IsSafePathSegment(string value) =>
        !string.IsNullOrEmpty(value) &&
        value is not ("." or "..") &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('%') &&
        !value.Contains('?') &&
        !value.Contains('#') &&
        !value.Any(char.IsControl);

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
                if (!SupportsNumericRules(rule, out configurationIssue)) return false;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer)) return false;
                return WithinRange(integer, rule);

            case "number":
            case "decimal":
                if (!SupportsNumericRules(rule, out configurationIssue)) return false;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) return false;
                return WithinRange(number, rule);

            case "boolean":
            case "bool":
                if (HasUnsupportedLengthOrRange(rule)) { configurationIssue = true; return false; }
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False;

            case "object":
                if (HasUnsupportedLengthOrRange(rule)) { configurationIssue = true; return false; }
                return value.ValueKind == JsonValueKind.Object;

            case "array":
                if (HasUnsupportedLengthOrRange(rule)) { configurationIssue = true; return false; }
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

    private static RequestValidationResult Invalid(string errorCode) => new(false, false, errorCode);
    private static RequestValidationResult ConfigurationError(string errorCode) => new(false, true, errorCode);
}
