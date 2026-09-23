using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace SecretaryOverlay;

internal sealed class PersonalizationSettings : StackPanel
{
    internal TextBox PetName { get; } = new() { MaxLength = PetPersonalization.NameLimit, MinHeight = 30 };
    internal TextBox Commentary { get; } = Editor();
    internal TextBox Completion { get; } = Editor();
    internal Button Save { get; } = new() { Content = "저장", MinWidth = 90, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 8, 8, 4) };
    internal Button Reset { get; } = new() { Content = "전체 기본값 불러오기", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 8, 0, 4) };
    internal TextBlock Status { get; } = Note("");
    internal TextBlock SaveHint { get; } = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private PetPersonalization saved;
    private readonly PersonalizationDraftStorage? draftStorage;
    private readonly DispatcherTimer draftTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private PersonalizationDraft? storedDraft;
    private bool cleanupPending;
    private bool filling;

    public PersonalizationSettings(PetPersonalization current, Func<PetPersonalization, bool> save,
        PersonalizationDraftStorage? draftStorage = null)
    {
        saved = PetPersonalization.Normalize(current);
        this.draftStorage = draftStorage;
        storedDraft = draftStorage?.Load();
        bool restored = storedDraft?.Saved == saved;
        Children.Add(Note("이름과 말투를 직접 정하세요. 작성 중인 내용은 임시 보관되어 창을 닫거나 앱을 다시 켜도 이어 쓸 수 있어요. 실제 비서에게는 ‘저장’을 눌러야 적용돼요."));
        Children.Add(Heading("비서 이름")); Children.Add(PetName);
        Children.Add(Note("최대 32자. 말풍선 위쪽·설정 창·작업표시줄 아이콘과 AI의 자기소개에 사용해요. 프로젝트명과 채팅명은 바뀌지 않아요."));
        Children.Add(Heading("화면 보고 말하기 지침"));
        Children.Add(Note("설정한 간격의 자동 한마디와 ‘지금 말하기’에 함께 적용해요. 원하는 말투·길이·관심사를 적어주세요."));
        Children.Add(Commentary);
        var resetCommentary = SmallButton("이 지침을 기본값으로", () => Commentary.Text = PetPersonalization.DefaultCommentary);
        Children.Add(resetCommentary);
        Children.Add(Heading("작업 완료 요약 지침"));
        Children.Add(Note("‘AI로 요약하기’에만 적용해요. ‘원문에서 간추리기’와 작업 중 진행 설명은 바뀌지 않아요."));
        Children.Add(Completion);
        Children.Add(SmallButton("이 지침을 기본값으로", () => Completion.Text = PetPersonalization.DefaultCompletion));
        Children.Add(Note("각 지침은 최대 6,000자예요. 빈칸으로 저장하면 기본 지침을 사용해요. 보이지 않는 일을 지어내지 않기, 실패·미완료 사항 남기기, 말풍선 출력 형식은 계속 유지해요."));
        var buttons = new WrapPanel(); buttons.Children.Add(Save); buttons.Children.Add(Reset); Children.Add(buttons); Children.Add(Status);
        Fill(restored ? storedDraft!.Value : saved);
        void Edited(object sender, TextChangedEventArgs e)
        {
            if (filling) return;
            Status.Text = Draft() == saved ? "저장된 설정을 표시하고 있어요." : "아직 적용하지 않았어요. 작성 내용은 임시 보관하고, ‘저장’을 누르면 다음 요청부터 적용해요.";
            RefreshSaveHint();
            ScheduleDraft();
        }
        PetName.TextChanged += Edited; Commentary.TextChanged += Edited; Completion.TextChanged += Edited;
        draftTimer.Tick += (_, _) => FlushDraft();
        Unloaded += (_, _) => FlushDraft();
        Reset.Click += (_, _) => { Fill(PetPersonalization.Default); ScheduleDraft(); Status.Text = "기본값을 불러왔어요. ‘저장’을 눌러 적용하세요."; RefreshSaveHint(); };
        Save.Click += (_, _) =>
        {
            var value = PetPersonalization.Normalize(Draft());
            FlushDraft();
            if (!save(value))
            {
                Status.Text = "저장하지 못했어요. 작성한 내용은 그대로 있으니 다시 시도하세요.";
                RefreshSaveHint("저장 실패 · 다시 시도해 주세요");
                return;
            }
            bool cleared = storedDraft is null || draftStorage is null || draftStorage.ClearMatching(storedDraft);
            if (cleared) storedDraft = null;
            cleanupPending = !cleared;
            saved = value; Fill(value);
            Status.Text = cleared
                ? "저장했어요. 다음 요청부터 적용되며, 이미 시작한 요청과 표시된 요약은 그대로 유지돼요."
                : "설정은 저장했지만 임시 보관본을 정리하지 못했어요. 다시 저장하면 정리를 재시도해요.";
            RefreshSaveHint(cleared ? "저장했어요" : "저장됨 · 임시 보관본 정리 실패");
        };
        Status.Text = restored ? "저장하지 않은 작성 내용을 불러왔어요. 아직 적용되지 않았으니 ‘저장’을 눌러 적용하세요." : "저장된 설정을 표시하고 있어요.";
        RefreshSaveHint();
        System.Windows.Automation.AutomationProperties.SetName(PetName, "비서 이름");
        System.Windows.Automation.AutomationProperties.SetName(Commentary, "화면 보고 말하기 지침");
        System.Windows.Automation.AutomationProperties.SetName(Completion, "작업 완료 요약 지침");
    }
    internal FrameworkElement CreateStickySaveBar()
    {
        if (Save.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(Save);
        Save.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel { Margin = new Thickness(14, 8, 14, 4) };
        DockPanel.SetDock(Save, Dock.Right);
        row.Children.Add(Save);
        row.Children.Add(SaveHint);
        return new Border { BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(0, 1, 0, 0), Child = row, Visibility = Visibility.Collapsed };
    }
    private void RefreshSaveHint(string? message = null)
    {
        bool dirty = Draft() != saved;
        Save.IsEnabled = dirty || cleanupPending;
        SaveHint.Text = message ?? (dirty ? "아직 적용 안 됨" : "저장된 설정");
    }
    private void ScheduleDraft()
    {
        if (draftStorage is null) return;
        draftTimer.Stop(); draftTimer.Start();
    }
    public void FlushDraft()
    {
        draftTimer.Stop();
        if (draftStorage is null) return;
        var value = Draft();
        bool persisted;
        if (value == saved)
        {
            persisted = storedDraft is null || draftStorage.ClearMatching(storedDraft);
            if (persisted) storedDraft = null;
            cleanupPending = !persisted;
        }
        else
        {
            var draft = new PersonalizationDraft(saved, value);
            persisted = draft == storedDraft || draftStorage.Save(draft);
            if (persisted) storedDraft = draft;
        }
        if (!persisted)
        {
            Status.Text = "작성 내용을 임시 보관하지 못했어요. 창을 닫기 전에 ‘저장’을 눌러 주세요.";
            RefreshSaveHint("임시 보관 실패 · 저장해 주세요");
        }
        else if (value == saved) RefreshSaveHint();
    }
    private PetPersonalization Draft() => new(PetName.Text, Commentary.Text, Completion.Text);
    private void Fill(PetPersonalization value)
    {
        filling = true;
        try { PetName.Text = value.Name; Commentary.Text = value.CommentaryInstructions; Completion.Text = value.CompletionInstructions; }
        finally { filling = false; }
    }
    private static TextBox Editor() => new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MaxLength = PetPersonalization.InstructionsLimit, Padding = new Thickness(6) };
    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 10, 0, 6) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 6, 0, 8) };
    private static Button SmallButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 5, 0, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        button.Click += (_, _) => click(); return button;
    }
}
