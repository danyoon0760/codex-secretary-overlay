
namespace SecretaryOverlay;

internal sealed partial class SpeechBubble
{
    public void ClearAmbient() { ambientVersion++; ambient = ambientDetail = ""; ambientStreaming = false; }

    public long BeginAmbientStream() => ++ambientVersion;

    public void DiscardAmbientStream(long version)
    {
        if (version != ambientVersion) return;
        ClearAmbient(); Render();
    }

    public bool UpdateAmbientStream(long version, string value, string detail = "")
    {
        if (version != ambientVersion || !HasAmbientSlot) return false;
        value = ProgressDisplayText.Clean(value);
        if (value.Length == 0) return true; // No visible update; the current request is still valid.
        if (ambientShownVersion != version) ambientIdentity = BubbleDisplayIdentity.Next();
        ambientShownVersion = version;
        ambient = value;
        ambientDetail = detail;
        ambientStreaming = true;
        Render();
        return true;
    }

    public bool CompleteAmbientStream(long version, string value, string detail = "")
    {
        if (version != ambientVersion) return false;
        if (ProgressDisplayText.Clean(value).Length == 0)
        {
            DiscardAmbientStream(version);
            return false;
        }
        if (!UpdateAmbientStream(version, value, detail)) return false;
        ambientStreaming = false;
        UpdateAutoClose();
        return true;
    }

    public bool Say(string value, bool progress = false)
    {
        if (!HasAmbientSlot) return false;
        value = ProgressDisplayText.Clean(value);
        if (value.Length == 0) return false;
        ambientVersion++;
        ambientIdentity = BubbleDisplayIdentity.Next();
        ambientShownVersion = ambientVersion;
        ambient = value;
        ambientDetail = "";
        ambientStreaming = false;
        Render();
        return true;
    }
}
