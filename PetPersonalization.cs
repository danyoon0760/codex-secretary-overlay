using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

internal sealed record PetPersonalization(string Name, string CommentaryInstructions, string CompletionInstructions)
{
    public const int NameLimit = 32;
    public const int InstructionsLimit = 6000;
    public const string DefaultName = "비서 펫";
    public const string DefaultCommentary = """
        사용자 곁에서 지내는 비서 캐릭터로서, 지금 화면을 보고 문득 떠오른 말을 자연스럽게 건네세요.
        보고서처럼 설명할 필요는 없어요. 가벼운 감상이나 장난, 궁금한 점도 좋아요. 짧게 한국어로 말하세요.
        사용자에게 반말하지 말고, 항상 자연스러운 존댓말을 사용하세요.
        최근 한마디와 소재뿐 아니라 말투나 문장 틀도 겹치지 않게 자연스럽게 말을 건네세요.
        """;
    public const string DefaultCompletion = """
        원래 답변의 핵심을 사용자에게 직접 말하듯 자연스러운 한국어 존댓말로 전하세요.
        '안내했습니다', '설명했습니다', '도움을 드릴 수 있다고 전했습니다'처럼 발언 자체를 보고하지 마세요.
        실제 작업을 했다면 달라진 점을 먼저, 설명이나 제안이라면 그 내용 자체를 1~2개의 짧은 문장으로 알려주세요.
        인사나 짧은 대화는 원래 의도를 살려 직접 건네세요. 작업 결과처럼 바꾸거나 원문에 없는 제안을 덧붙이지 마세요.
        예: '도움을 드릴 수 있다고 안내했습니다.' 대신 '필요하신 일이 있으면 말씀해 주세요.'
        예: '설정 방법을 설명했습니다.' 대신 '자동 한마디 간격은 설정에서 바꿀 수 있어요.'
        예시는 말투 참고용이며 실제 요약에는 원래 답변에 있는 내용만 사용하세요.
        개발 용어는 가능한 한 쉽게 풀고, 원문에 있는 실제 검사 결과나 남은 확인 사항도 간단히 알려주세요.
        """;
    public static PetPersonalization Default { get; } = new(DefaultName, DefaultCommentary, DefaultCompletion);

    public static PetPersonalization Normalize(PetPersonalization? value)
    {
        if (value is null) return Default;
        string name = Regex.Replace(value.Name ?? "", @"\s+", " ").Trim();
        return new(Limit(name.Length == 0 ? DefaultName : name, NameLimit),
            Instructions(value.CommentaryInstructions, DefaultCommentary), Instructions(value.CompletionInstructions, DefaultCompletion));
    }
    private static string Instructions(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : Limit(value.Trim(), InstructionsLimit);
    private static string Limit(string value, int length) => value.Length <= length ? value
        : value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)];
}

internal static class PersonalizationStorage
{
    internal static string FilePath => Path.Combine(AppStorage.DataDirectory, "personalization.json");
    public static PetPersonalization Load(string? path = null)
    {
        try
        {
            return PetPersonalization.Normalize(JsonSerializer.Deserialize<PetPersonalization>(File.ReadAllText(path ?? FilePath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return PetPersonalization.Default; }
    }
    public static bool Save(PetPersonalization value, string? path = null) =>
        AtomicJsonFile.Save(path ?? FilePath, PetPersonalization.Normalize(value));
}
