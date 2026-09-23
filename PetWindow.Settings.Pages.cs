using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TabControl = System.Windows.Controls.TabControl;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private static StackPanel SettingsTab(TabControl tabs, string title)
    {
        var content = new StackPanel { Margin = new Thickness(14) };
        tabs.Items.Add(new TabItem { Header = title, Padding = new Thickness(10, 7, 10, 7), Content = new ScrollViewer
            { Content = content, CanContentScroll = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        return content;
    }
    private void SettingsToggle(StackPanel parent, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 8, 0, 4) };
        box.Checked += (_, _) => { if (!syncingSettings) set(true); };
        box.Unchecked += (_, _) => { if (!syncingSettings) set(false); };
        settingsRefresh.Add(() => box.IsChecked = get()); parent.Children.Add(box);
    }
    private void SettingsModels(StackPanel parent, ModelFeature feature, Func<ModelProfile> get)
    {
        var choices = new ModelSettings(get, profile => SetModelProfile(feature, profile), feature != ModelFeature.Completion);
        parent.Children.Add(choices); settingsRefresh.Add(() => choices.Sync());
    }

    private void BuildSpeakingSettings(StackPanel speaking)
    {
        speaking.Children.Add(SettingsHeading("자동으로 말하기"));
        SettingsToggle(speaking, "자동으로 말하기", () => commentary.Enabled, SetCommentaryEnabled);
        var interval = new NumberSetting("자동 한마디 간격", "분", CommentaryPolicy.MinIntervalMinutes, CommentaryPolicy.MaxIntervalMinutes,
            () => commentary.IntervalMinutes, value => SetCommentaryInterval((int)value));
        speaking.Children.Add(interval); settingsRefresh.Add(interval.Sync);
        speaking.Children.Add(SettingsDescription("기본은 20분이에요. 간격을 바꾸면 지금부터 새 간격을 기다려요. 변경만으로 바로 말을 걸지는 않아요."));
        speaking.Children.Add(SettingsDescription("작업 중이 아닐 때 현재 사용 중인 창을 보고 말을 걸어요. 최근 5분 안에 마우스를 움직였거나, 영상 재생이 감지되면 동작해요. 화면 잠금·꺼짐 상태에서는 쉬어요."));
        SettingsModels(speaking, ModelFeature.Automatic, () => automaticModel);
        speaking.Children.Add(SettingsHeading("화면 보고 한마디"));
        speaking.Children.Add(SettingsDescription("원할 때 직접 요청할 수 있어요. 설정 창 뒤에서 사용하던 앱을 확인해요. 자동 말하기가 꺼져 있어도 사용할 수 있어요."));
        SettingsModels(speaking, ModelFeature.Manual, () => manualModel);
        var speak = SettingsButton("지금 말하기", () => _ = SpeakAsync(true));
        speaking.Children.Add(speak);
        settingsRefresh.Add(() =>
        {
            speak.IsEnabled = !commentary.Busy && HasCommentarySlot;
            speak.Content = commentary.Busy ? "한마디 준비 중…" : HasCommentarySlot ? "지금 말하기" : "말풍선 하나를 닫으면 사용할 수 있어요";
        });
        speaking.Children.Add(SettingsDescription("두 기능 모두 현재 창의 화면을 AI에 보내며 Codex 사용량이 소모돼요. 최근 한마디 5개를 참고하고, 앱을 종료하면 잊어요. 모델 선택만으로 말을 시작하지는 않아요.\n생각 깊이가 높을수록 응답에 더 많은 시간과 사용량이 들 수 있어요. 모델이 지원하는 단계만 선택할 수 있어요."));
        SettingsToggle(speaking, "영상 시청 중으로 직접 지정", () => manualViewing, value => { manualViewing = value; WriteStatus(); });
        speaking.Children.Add(SettingsDescription("영상 재생이 자동으로 감지되지 않을 때 켜세요. 한마디를 요청할 때 두 장면을 보고 흐름을 파악해요. 이 선택은 앱을 종료하면 해제돼요."));
    }

    private void BuildProgressSettings(StackPanel progress)
    {
        progress.Children.Add(SettingsHeading("작업 상황 말풍선"));
        SettingsToggle(progress, "작업 상황 말풍선 표시", () => showProgress, SetProgressEnabled);
        progress.Children.Add(SettingsDescription("Codex의 진행 설명과 세부 작업을 최대 3개 말풍선에 표시해요. 아래에는 프로젝트명 • 채팅명 • 세부 작업이 보여요. 말풍선의 ×는 표시만 닫고 작업은 멈추지 않아요."));
        progress.Children.Add(SettingsHeading("말풍선 모양과 위치"));
        var bubbleSettings = new BubbleAppearanceSettings(() => bubbleAppearance, SetBubbleAppearance);
        progress.Children.Add(bubbleSettings); settingsRefresh.Add(bubbleSettings.Sync);
        progress.Children.Add(SettingsDescription("열린 말풍선에도 바로 적용돼요. 머리와의 거리는 숫자가 클수록 멀어져요. 화면 가장자리에서는 화면 안으로 위치를 조정해요. 높이는 내용에 맞춰 자동으로 늘어나요."));
        completionSettingsPanel = CompletionSettings.Create(progressBoard.Style, SetCompletionStyle, () => completionModel);
        progress.Children.Add(completionSettingsPanel);
        settingsRefresh.Add(() => CompletionSettings.Sync(completionSettingsPanel, progressBoard.Style));
        progress.Children.Add(SettingsHeading("AI 요약에 사용할 모델"));
        SettingsModels(progress, ModelFeature.Completion, () => completionModel);
        progress.Children.Add(SettingsDescription("‘AI로 요약하기’를 선택했을 때 사용해요. 모델만 바꿔도 요약 방식이 자동으로 켜지지는 않아요. 진행 중인 요약은 시작할 때 선택한 모델로 끝내요.\n생각 깊이가 높을수록 응답에 더 많은 시간과 사용량이 들 수 있어요."));
        progress.Children.Add(SettingsDescription("작업 알림이 오지 않나요? 처음 연결할 때 Codex 설정 → Hook에서 이 펫의 연결을 허용했는지 확인하세요."));
    }

    private void BuildCharacterSettings(StackPanel character)
    {
        character.Children.Add(SettingsHeading("크기와 위치"));
        character.Children.Add(SettingsDescription("캐릭터를 드래그해서 옮기고, 마우스 휠로 크기를 조절하세요. 아래에서도 크기를 바꿀 수 있어요."));
        var sizeText = new TextBlock(); character.Children.Add(sizeText);
        var size = new Slider { Minimum = MinimumHeight, Maximum = MaximumHeight, Value = Height, Margin = new Thickness(0, 8, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetName(size, "캐릭터 크기");
        size.ValueChanged += (_, e) => { if (!syncingSettings) ChangeSize(e.NewValue / Height); };
        character.Children.Add(size);
        var sizeButtons = new WrapPanel(); sizeButtons.Children.Add(SettingsButton("캐릭터 줄이기", () => ChangeSize(1 / 1.1)));
        sizeButtons.Children.Add(SettingsButton("캐릭터 키우기", () => ChangeSize(1.1))); character.Children.Add(sizeButtons);
        settingsRefresh.Add(() => { size.Value = Height; sizeText.Text = $"현재 크기: {Height:0}"; });
        SettingsToggle(character, "숨쉬는 움직임 사용", () => motion, SetMotionEnabled);
        character.Children.Add(SettingsDescription("가만히 있을 때의 작은 움직임과 자세가 바뀔 때의 애니메이션을 켜거나 꺼요."));
        SettingsToggle(character, "캐릭터 아래 상태 글자 표시", () => showLabels, SetLabelVisibility);
        character.Children.Add(SettingsDescription("‘작업하고 있어요’ 같은 상태와 Codex 연결 여부를 보여줘요."));
        SettingsToggle(character, "마우스 클릭 통과시키기", () => clickThrough, SetClickThrough);
        character.Children.Add(SettingsDescription("캐릭터를 클릭해도 뒤에 있는 앱이 눌려요. 켜져 있으면 캐릭터를 드래그하거나 우클릭할 수 없어요. 이 설정 창에서 끄거나, 작업표시줄의 비서 아이콘을 우클릭해 해제하세요. 앱을 다시 켜면 해제돼요."));
        character.Children.Add(SettingsButton("캐릭터 다시 표시 · 클릭 통과 해제", RestoreInteraction));
    }

    private void BuildPreviewSettings(StackPanel preview)
    {
        preview.Children.Add(SettingsHeading("표정과 자세 미리보기"));
        preview.Children.Add(SettingsDescription("버튼을 누르면 상황별 표정과 자세를 볼 수 있어요. 실제 Codex 작업에는 영향을 주지 않아요. 새 작업 알림이 오면 실제 상태로 돌아가요."));
        preview.Children.Add(CreatePreviewButtons());
        preview.Children.Add(SettingsButton("미리보기 끝내기", () => { engine.Reset(Now); RenderProgress(); }));
        preview.Children.Add(SettingsDescription("미리보기를 끝내면 다음 작업 알림을 기다려요."));
    }

    private void BuildPersonalizationSettings(StackPanel personal)
    {
        personalizationEditor = new PersonalizationSettings(personalization, SavePersonalization,
            settingsPreviewOnly ? null : new PersonalizationDraftStorage());
        personal.Children.Add(personalizationEditor);
    }

}
