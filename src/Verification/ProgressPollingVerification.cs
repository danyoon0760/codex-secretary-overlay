using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class ProgressPollingVerification
{
    public static void Check(Action<bool, string> check)
    {
        var policy = new ProgressPollingPolicy();
        var task = new TaskProgress("polling-session") { Turn = "first" };
        check(policy.TryBeginRead(task, 0) && !policy.TryBeginRead(task, 149)
            && policy.TryBeginRead(task, 150), "Visible active progress keeps its 150 ms cadence");

        task.Dismissed = true;
        check(!policy.TryBeginRead(task, 300) && !policy.TryBeginRead(task, 1_149)
            && policy.TryBeginRead(task, 1_150), "Dismissed active tasks keep tracking at a slower cadence");
        policy.NotifyHook(task, 1_160);
        check(policy.TryBeginRead(task, 1_160), "Lifecycle hooks wake a dismissed reader immediately");

        task.Active = false;
        task.State = "Stop";
        policy.NotifyHook(task, 1_200);
        check(policy.TryBeginRead(task, 1_200) && policy.TryBeginRead(task, 1_350)
            && policy.TryBeginRead(task, 11_100), "Stop keeps a ten second fast window for late final answers, including closed bubbles");
        policy.NotifyHook(task, 11_110);
        check(policy.TryBeginRead(task, 11_110) && !policy.TryBeginRead(task, 11_260)
            && !policy.TryBeginRead(task, 16_109) && policy.TryBeginRead(task, 16_110),
            "Repeated inactive hooks wake once without prolonging the grace window");

        task.Turn = "second";
        task.Active = true;
        task.Dismissed = false;
        check(policy.TryBeginRead(task, 16_120) && !policy.TryBeginRead(task, 16_269)
            && policy.TryBeginRead(task, 16_270), "A new turn immediately resumes the active cadence");
        policy.NotifyHook(task, 16_280);
        policy.NotifyHook(task, 16_281);
        check(policy.TryBeginRead(task, 16_282) && !policy.TryBeginRead(task, 16_283),
            "Hooks arriving during an earlier read retain one pending wake");
        policy.Remove(task.Session);
        check(policy.TryBeginRead(task, 16_290), "Removing a reader also discards its old polling schedule");

        CheckLateFinalAsync(check).GetAwaiter().GetResult();
    }

    private static async Task CheckLateFinalAsync(Action<bool, string> check)
    {
        const string session = "11111111-1111-4111-8111-111111111111";
        const string turn = "22222222-2222-4222-8222-222222222222";
        string folder = Path.Combine(Path.GetTempPath(), "SecretaryPollingTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "transcript.jsonl");
        try
        {
            await File.WriteAllTextAsync(path, "").ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var board = new ProgressBoard();
            var task = board.Accept(new PetEvent("UserPromptSubmit", session, Turn: turn), "test")!;
            var reader = new ProgressTranscript(path, session, turn, now.AddSeconds(-1));
            var policy = new ProgressPollingPolicy();
            if (policy.TryBeginRead(task, 0)) await reader.ReadUpdatesAsync(CancellationToken.None).ConfigureAwait(false);
            board.Accept(new PetEvent("Stop", session, Turn: turn), "test");
            policy.NotifyHook(task, 100);
            if (policy.TryBeginRead(task, 100)) await reader.ReadUpdatesAsync(CancellationToken.None).ConfigureAwait(false);
            if (policy.TryBeginRead(task, 10_000)) await reader.ReadUpdatesAsync(CancellationToken.None).ConfigureAwait(false);

            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new
            {
                timestamp = now, type = "event_msg", payload = new
                {
                    type = "task_complete", thread_id = session, turn_id = turn,
                    last_agent_message = "늦게 저장된 완료 설명입니다."
                }
            }) + "\n").ConfigureAwait(false);
            check(!policy.TryBeginRead(task, 10_200), "A finished transcript is not reopened on each active timer tick");
            if (policy.TryBeginRead(task, 15_000))
                foreach (var message in await reader.ReadUpdatesAsync(CancellationToken.None).ConfigureAwait(false)) board.Apply(message);
            check(task.FinalAnswer == "늦게 저장된 완료 설명입니다." && !task.Active,
                "A final answer written after the grace window still reaches its completed task");
        }
        finally { Directory.Delete(folder, true); }
    }
}
