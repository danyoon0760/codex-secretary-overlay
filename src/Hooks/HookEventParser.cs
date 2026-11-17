using System.Text.Json;

namespace SecretaryOverlay;

internal static class HookEventParser
{
    public static PetEvent? Parse(JsonElement root, bool preview)
    {
        string Field(string name) => JsonFields.String(root, name);
        string eventName = Field("hook_event_name");
        if (!StateEngine.Poses.ContainsKey(eventName)) return null;
        string detail = eventName is "PreToolUse" or "PostToolUse"
            ? ActivityDescription.FromHook(root, Field("tool_name"), eventName == "PostToolUse") : "";
        // Raw answer/tool-output bodies are not part of the hook transport contract.
        return new(eventName, Field("session_id"), Field("tool_name"), preview,
            Field("transcript_path"), Field("turn_id"), Field("cwd"), detail);
    }
}
