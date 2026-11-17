namespace SecretaryOverlay;

internal sealed class TaskProgress(string session)
{
    public long BubbleIdentity { get; } = BubbleDisplayIdentity.Next();
    public string Key => Session + "/" + BubbleIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public int QuestionNumber { get; init; } = 1;
    public bool PromptReceived { get; set; }
    public string Session { get; } = session;
    public string Turn { get; set; } = "";
    public string Project { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string ChatTitle { get; set; } = "";
    public string Body { get; set; } = "생각하고 있어요.";
    public bool HasKoreanProgress { get; set; }
    public string Detail { get; set; } = "작업을 시작하고 있어요";
    public bool DetailFromTranscript { get; set; }
    public string State { get; set; } = "UserPromptSubmit";
    public bool Dismissed { get; set; }
    public bool Active { get; set; } = true;
    public string Completion { get; set; } = "";
    public string CompletionDetail { get; set; } = "응답 완료";
    public string FinalAnswer { get; set; } = "";
    public CompletionPresentation? NaturalCompletion { get; set; }
    public bool SummaryAttempted { get; set; }
    public bool SummaryPending { get; set; }
    public bool SummaryFailed { get; set; }
    public string StreamingCompletion { get; set; } = "";
    public string StreamingReasoning { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    private CompletionStyle completionStyle;
    public bool WaitingForSummary => completionStyle == CompletionStyle.D && !Active && State == "Stop"
        && NaturalCompletion is null && !SummaryFailed;

    public bool IsCompleted => !Active && State is "Stop" or "SessionEnd";
    public bool CompletionReady => !Active && !SummaryPending && !WaitingForSummary;

    public void ResetSummary()
    {
        NaturalCompletion = null;
        SummaryAttempted = SummaryPending = SummaryFailed = false;
        ClearStreamingText();
    }

    public void BeginSummary()
    {
        SummaryAttempted = SummaryPending = true;
        SummaryFailed = false;
        ClearStreamingText();
    }

    public void CancelSummary()
    {
        SummaryAttempted = false;
        FinishSummary();
    }

    public void FinishSummary()
    {
        SummaryPending = false;
        ClearStreamingText();
    }

    private void ClearStreamingText() => StreamingCompletion = StreamingReasoning = "";

    public void RefreshCompletion(CompletionStyle style)
    {
        completionStyle = style;
        if (!Active && State == "SessionEnd")
        {
            Body = "수고하셨습니다.";
            Detail = "채팅 종료";
            return;
        }
        if (WaitingForSummary)
        {
            Body = "";
            Detail = "한마디로 정리 중";
            return;
        }
        if (!IsCompleted || (Completion.Length == 0 && NaturalCompletion is null)) return;
        Body = CompletionBody(style);
        Detail = CompletionStatus(style);
    }

    private string CompletionBody(CompletionStyle style)
    {
        if (style != CompletionStyle.D) return Completion;
        return NaturalCompletion?.Body ?? Completion;
    }

    private string CompletionStatus(CompletionStyle style)
    {
        if (style != CompletionStyle.D) return CompletionDetail;
        if (SummaryPending)
            return StreamingReasoning.Length > 0 ? "생각하는 중 · 완료 문구 요약" : "한마디로 정리 중";
        if (SummaryFailed) return "요약 연결 실패 · 원문 표시";
        return NaturalCompletion?.Detail ?? CompletionDetail;
    }
}
