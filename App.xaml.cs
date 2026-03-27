using System.Threading;
using System.Windows;

namespace PipeBendingDashboard
{
    public partial class App : Application
    {
        // 전역 Mutex — 단일 인스턴스 제어
        private static Mutex? _mutex;
        private const string MutexName = "PipeBendingDashboard_SingleInstance_IAAN";

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── 단일 인스턴스 체크 ───────────────────────────────
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // 이미 실행 중 → 팝업 후 종료
                MessageBox.Show(
                    "이미 프로그램이 실행 중입니다.\n\n" +
                    "작업 표시줄에서 실행 중인 창을 확인하세요.",
                    "IAAN — Pipe Bending Dashboard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                _mutex.Dispose();
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // ── 전역 예외 처리 ───────────────────────────────────
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    $"예기치 않은 오류가 발생했습니다:\n{ex.Exception.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 앱 종료 시 Mutex 해제
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
