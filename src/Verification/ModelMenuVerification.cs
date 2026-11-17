using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class ModelMenuVerification
{
    public static void Check(Action<bool, string> check)
    {
        using var metadata = JsonDocument.Parse("""
            {"models":[
              {"slug":"gpt-5.6-luna","visibility":"list","input_modalities":["text","image"],"supported_reasoning_levels":[{"effort":"low"},{"effort":"medium"},{"effort":"high"}]},
              {"slug":"gpt-5.6-sol","visibility":"list","input_modalities":["text","image"],"supported_reasoning_levels":[{"effort":"low"},{"effort":"high"}]},
              {"slug":"text-only","visibility":"list","input_modalities":["text"],"supported_reasoning_levels":[{"effort":"medium"}]},
              {"slug":"hidden-model","visibility":"hide","supported_reasoning_levels":[{"effort":"low"}]}
            ]}
            """);
        var catalog = ModelCatalog.Parse(metadata.RootElement);
        check(catalog.Length == 3 && catalog.All(m => m.Id != "hidden-model"), "Model menus use only visible models from local Codex metadata");
        ModelProfile automatic = new(), manual = new(), completion = new();
        int selections = 0;
        var choices = ModelMenus.Choices(catalog, automatic, true, profile => { automatic = profile; selections++; });
        check(choices.Length == 2 && choices.Single(m => m.Checked).Label == "GPT-5.6 Luna", "Image commentary excludes text-only models and checks the full model name");
        check(choices[0].Children!.Single(e => e.Checked).Label == "보통", "The selected reasoning effort has its own check mark");
        check(choices[1].Children!.Select(e => e.Label).SequenceEqual(new[] { "낮음", "높음" }), "Each model lists exactly its supported reasoning levels");
        check(selections == 0 && choices.All(m => m.Action is null), "Opening or hovering over model branches does not select or execute anything");
        choices[1].Children![0].Action!();
        check(automatic == new ModelProfile("gpt-5.6-sol", "low") && manual == ModelProfile.Default && completion == ModelProfile.Default && selections == 1,
            "Selecting a leaf updates only the automatic feature's model and reasoning");
        var manualChoices = ModelMenus.Choices(catalog, manual, true, profile => manual = profile);
        manualChoices[0].Children![2].Action!();
        check(manual.Reasoning == "high" && automatic.Reasoning == "low" && completion.Reasoning == "medium", "Manual and completion settings remain independent from automatic settings");
        var completionChoices = ModelMenus.Choices(catalog, completion, false, profile => completion = profile);
        check(completionChoices.Length == 3, "Text-only models remain eligible for final-answer summaries");
        completionChoices[1].Children![1].Action!();
        var layout = new Layout(11, 22, 560, AutomaticModel: automatic, ManualModel: manual, CompletionModel: completion);
        check(JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout)) == layout, "Three independent profiles survive saved-layout serialization");
        var legacy = JsonSerializer.Deserialize<Layout>("{\"Left\":11,\"Top\":22,\"Height\":560}")!;
        check(ModelProfile.Validated(legacy.AutomaticModel) == ModelProfile.Default && ModelProfile.Validated(legacy.ManualModel) == ModelProfile.Default
            && ModelProfile.Validated(legacy.CompletionModel) == ModelProfile.Default, "Existing settings migrate all three features to the previous Luna-medium defaults");
        check(ModelProfile.Validated(new("bad model", "x")) == ModelProfile.Default, "Invalid saved model data cannot become command configuration");
        foreach (var profile in new[] { automatic, manual, completion })
        {
            var thread = JsonSerializer.SerializeToElement(CodexRequestMessages.ThreadStart(Path.GetTempPath(), profile)).GetProperty("params");
            var turn = JsonSerializer.SerializeToElement(CodexRequestMessages.TurnStart("selected-thread", "화면 설명", ["screen.png"], profile, null)).GetProperty("params");
            check(thread.GetProperty("model").GetString() == profile.Model && turn.GetProperty("model").GetString() == profile.Model
                && turn.GetProperty("effort").GetString() == profile.Reasoning && turn.GetProperty("input")[1].GetProperty("path").GetString() == "screen.png",
                "The actual JSON-RPC thread and turn receive the selected profile exactly: " + ModelCatalog.Describe(profile));
        }
        var snapshot = CodexRequestMessages.TurnStart("snapshot-thread", "요약할 내용", [], automatic, null);
        automatic = new("gpt-5.6-luna", "high");
        var snapshotParameters = JsonSerializer.SerializeToElement(snapshot).GetProperty("params");
        check(snapshotParameters.GetProperty("model").GetString() == "gpt-5.6-sol" && snapshotParameters.GetProperty("effort").GetString() == "low",
            "A later setting change cannot mutate an already-created streaming request");
        var completionSettings = CompletionSettings.Create(CompletionStyle.D, _ => { }, () => completion);
        check(completionSettings.Children.OfType<System.Windows.Controls.TextBlock>().Last().Text.Contains("GPT-5.6 Sol · 높음"), "The settings window describes the chosen D model with its version");
        completion = new("gpt-5.6-luna", "low");
        CompletionSettings.Sync(completionSettings, CompletionStyle.D);
        check(completionSettings.Children.OfType<System.Windows.Controls.TextBlock>().Last().Text.Contains("GPT-5.6 Luna · 낮음"), "An open settings window refreshes its full model description after menu selection");
        CheckNative(check, catalog);
    }

    private static void CheckNative(Action<bool, string> check, ModelOption[] catalog)
    {
        var selected = ModelProfile.Default;
        bool ran = false;
        var menu = new NativePopupMenu();
        IntPtr root = menu.Handle;
        try
        {
            var children = new[] { new MenuEntry("지금 실행", () => ran = true), MenuEntry.Separator }
                .Concat(ModelMenus.Choices(catalog, selected, true, profile => selected = profile)).ToArray();
            menu.AddEntries([new("지금 한마디", Children: children), new("다른 기능", Children: [new("같은 이름", () => { })])]);
            var feature = GetSubMenu(root, 0);
            var luna = GetSubMenu(feature, 2);
            var sol = GetSubMenu(feature, 3);
            check(feature != IntPtr.Zero && luna != IntPtr.Zero && sol != IntPtr.Zero && GetMenuItemCount(sol) == 2,
                "The real Windows menu has function, model and reasoning submenu handles for native hover navigation");
            check((GetMenuState(feature, 2, 0x400) & 0x8) != 0 && (GetMenuState(luna, 1, 0x400) & 0x8) != 0,
                "Native model and reasoning items carry checked flags");
            uint choose = GetMenuItemID(sol, 0);
            uint run = GetMenuItemID(feature, 0);
            uint other = GetMenuItemID(GetSubMenu(root, 1), 0);
            check(choose != run && choose != other && run != other, "Nested menu commands have unique IDs across all branches");
            var action = menu.Resolve(choose);
            menu.Dispose(); // Production invokes only after TrackPopupMenuEx and Dispose have finished.
            action!();
            check(selected == new ModelProfile("gpt-5.6-sol", "low") && !ran, "Native leaf selection works after menu close without triggering a manual request");
            check(!IsMenu(root) && !IsMenu(feature) && !IsMenu(sol), "Closing the native root releases all submenu handles");
        }
        finally { menu.Dispose(); }
    }

    public static async Task<int> SmokeAsync()
    {
        var profile = new ModelProfile("gpt-5.6-sol", "low");
        var result = await CompletionSummaryClient.GenerateAsync("자동 한마디와 지금 한마디, 완료 문구의 모델 설정을 각각 저장하도록 적용했어요.", CancellationToken.None, profile);
        Directory.CreateDirectory(AppStorage.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(AppStorage.DataDirectory, "model-selection-smoke.json"), JsonSerializer.Serialize(new { ok = true, profile, result }, AppStorage.Json));
        return 0;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsMenu(IntPtr menu);
}
