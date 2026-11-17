using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

public sealed record ModelProfile(string Model = "gpt-5.6-luna", string Reasoning = "medium")
{
    public static ModelProfile Default { get; } = new();
    public static ModelProfile Validated(ModelProfile? value) => value is not null
        && Regex.IsMatch(value.Model ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$")
        && ModelCatalog.EffortNames.ContainsKey(value.Reasoning ?? "") ? value : Default;
}
