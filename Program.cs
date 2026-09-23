using System.IO;
using System.Windows;

namespace SecretaryOverlay;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--hook") || args.Contains("--test-hook"))
            {
                HookTransport.ForwardHookAsync(args.Contains("--test-hook")).GetAwaiter().GetResult();
                return 0;
            }

            PetEvent? command = args switch
            {
                ["--preview", var pose, ..] => new(pose, Preview: true),
                _ when args.Contains("--quit") => new("__quit"),
                _ when args.Contains("--idle") => new("__idle"),
                _ when args.Contains("--preview-commentary") => new("__commentary-preview"),
                _ when args.Contains("--preview-menu") => new("__menu-preview"),
                _ => null
            };
            if (command is not null)
            {
                HookTransport.SendAsync(command).GetAwaiter().GetResult();
                return 0;
            }
            AppStorage.Initialize();
            if (DevelopmentCommands.Run(args) is { } exitCode) return exitCode;

            using var mutex = new Mutex(true, "Local\\" + HookTransport.PipeName, out bool first);
            if (!first)
            {
                HookTransport.SendAsync(new PetEvent("__show")).GetAwaiter().GetResult();
                return 0;
            }

            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (_, e) =>
            {
                AppStorage.LogError(e.Exception);
                System.Windows.MessageBox.Show("비서 펫에서 복구할 수 없는 오류가 발생해 종료합니다. 오류 기록을 확인해 주세요.",
                    "비서 펫", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
                app.Shutdown(1);
            };
            return app.Run(new PetWindow());
        }
        catch (Exception ex)
        {
            AppStorage.LogError(ex);
            if (args.Length == 0)
                System.Windows.MessageBox.Show("비서 펫을 시작하지 못했습니다. 사용자 데이터 폴더와 오류 기록을 확인해 주세요.",
                    "비서 펫", MessageBoxButton.OK, MessageBoxImage.Error);
            return args.Contains("--hook") ? 0 : 1;
        }
    }
}
