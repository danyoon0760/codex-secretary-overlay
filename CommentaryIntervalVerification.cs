namespace SecretaryOverlay;

internal static class CommentaryIntervalVerification
{
    public static void Check(Action<bool, string> check)
    {
        var policy = new CommentaryPolicy(5_000);
        check(policy.IntervalMinutes == 20 && policy.NextAt == 1_205_000,
            "Existing commentary starts with a twenty-minute interval");

        policy.SetIntervalMinutes(5, 2_000_000);
        check(policy.NextAt == 2_300_000 &&
              !policy.CanStart(2_000_000, false, true, true, true, false) &&
              !policy.CanStart(2_299_999, false, true, true, true, false) &&
              policy.CanStart(2_300_000, false, true, true, true, false),
            "Changing a due interval schedules a new full delay without firing immediately");

        policy.Begin();
        policy.Finish(2_350_000, true);
        check(policy.NextAt == 2_650_000 && !policy.Busy,
            "Successful commentary schedules the selected interval after completion");
        policy.Begin();
        policy.Finish(2_700_000, false);
        check(policy.NextAt == 2_760_000 && !policy.Busy,
            "Commentary failure retains its one-minute retry delay");

        policy.ResetTimer(3_000_000);
        check(policy.NextAt == 3_300_000,
            "Resetting commentary uses the selected interval");

        policy.Enabled = false;
        policy.SetIntervalMinutes(60, 3_000_000);
        check(!policy.Enabled && !policy.CanStart(6_600_000, false, true, true, true, true) &&
              policy.CanStart(3_000_000, true, false, true, false, false),
            "Interval changes preserve automatic enablement and immediate manual requests");

        policy.Begin();
        policy.SetIntervalMinutes(10, 3_050_000);
        check(policy.Busy && !policy.CanStart(3_050_000, true, true, true, true, true),
            "Changing an interval cannot start a second request while busy");
        policy.Finish(3_100_000, true);
        check(policy.NextAt == 3_700_000,
            "A running request uses the newly selected interval when it finishes");

        policy.SetIntervalMinutes(int.MinValue, 4_000_000);
        check(policy.IntervalMinutes == 1 && policy.NextAt == 4_060_000,
            "Invalid small commentary intervals are limited to one minute");
        policy.SetIntervalMinutes(int.MaxValue, 4_000_000);
        check(policy.IntervalMinutes == 180 && policy.NextAt == 14_800_000,
            "Invalid large commentary intervals are limited to three hours");
    }
}
