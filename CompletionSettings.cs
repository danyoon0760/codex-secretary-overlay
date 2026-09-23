using System.Windows;
using System.Windows.Controls;

namespace SecretaryOverlay;

internal static class CompletionSettings
{
    public static StackPanel Create(CompletionStyle selected, Action<CompletionStyle> select, Func<ModelProfile>? profile = null)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 16) };
        panel.Children.Add(new TextBlock { Text = "작업이 끝나면 어떻게 알려줄까요?", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        var choices = new System.Windows.Controls.ComboBox { MinHeight = 30 };
        choices.Items.Add("원문에서 간추리기");
        choices.Items.Add("AI로 요약하기");
        choices.SelectedIndex = selected == CompletionStyle.D ? 1 : 0;
        panel.Tag = choices;
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
        void Describe() => description.Text = choices.SelectedIndex == 1
            ? "작업 결과를 짧고 자연스러운 말로 정리해요. AI 요약이 완성되면 완료 말풍선을 표시해요. 기다리는 동안에는 원문이나 작성 중인 요약을 표시하지 않아요. 완료 시 한 번 요약하며 Codex 사용량이 추가로 소모돼요. 요약하지 못하면 원문에서 간추려 보여줘요.\n현재 선택: " + ModelCatalog.Describe(profile?.Invoke() ?? ModelProfile.Default)
            : "답변에서 결과와 확인 내용을 골라 보여줘요. 추가 AI 사용량은 없어요.";
        bool syncing = false;
        choices.SelectionChanged += (_, _) => { Describe(); if (!syncing) select(choices.SelectedIndex == 1 ? CompletionStyle.D : CompletionStyle.B); };
        Describe();
        panel.Resources["RefreshDescription"] = (Action)Describe;
        panel.Resources["SyncSelection"] = (Action<CompletionStyle>)(style =>
        {
            syncing = true;
            try { choices.SelectedIndex = style == CompletionStyle.D ? 1 : 0; Describe(); }
            finally { syncing = false; }
        });
        panel.Children.Add(choices);
        panel.Children.Add(description);
        return panel;
    }

    public static void Sync(StackPanel? panel, CompletionStyle selected)
    {
        if (panel?.Resources["SyncSelection"] is Action<CompletionStyle> sync) sync(selected);
    }
}
