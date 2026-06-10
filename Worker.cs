/*
===============================================================================
CalculatorWorker2
===============================================================================

OBJETIVO
--------
Este Worker Service executa continuamente em segundo plano e tem como objetivo
abrir e encerrar a Calculadora do Windows em intervalos definidos.

FUNCIONAMENTO
-------------
1. O serviço inicia e entra em um loop contínuo.
2. A Calculadora do Windows é aberta.
3. O serviço aguarda 30 segundos.
4. A Calculadora é encerrada.
5. O serviço aguarda 1 minuto.
6. O ciclo se repete até que o serviço seja parado.

SESSION 0
---------
Serviços Windows executam normalmente na Session 0, que não possui interface
gráfica interativa para o usuário.

Para permitir que a Calculadora apareça na área de trabalho do usuário logado,
o sistema:

- Obtém a sessão ativa do Windows.
- Obtém o token de segurança do usuário logado.
- Duplica esse token para criar um token primário.
- Cria o ambiente do usuário.
- Utiliza CreateProcessAsUser para iniciar a Calculadora na sessão correta.

LOGS
----
O arquivo C:\temp\worker.txt é utilizado para registrar informações de
diagnóstico, incluindo:

- Inicialização do serviço.
- Execução de cada ciclo.
- Abertura da Calculadora.
- Sessão detectada.
- Chamadas às APIs Win32.
- PID do processo criado.
- Erros inesperados.

APIs NATIVAS UTILIZADAS
-----------------------
- WTSGetActiveConsoleSessionId
- WTSQueryUserToken
- DuplicateTokenEx
- CreateEnvironmentBlock
- CreateProcessAsUser
- CloseHandle

OBSERVAÇÕES
-----------
- O serviço continua executando mesmo que o usuário feche a Calculadora
  manualmente.
- O loop somente é interrompido quando o serviço recebe uma solicitação
  de cancelamento (stoppingToken).
- O código libera os handles e recursos Win32 utilizados para evitar
  vazamentos de memória e recursos do sistema.

===============================================================================
*/

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CalculatorWorker2;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        File.AppendAllText(
            @"C:\temp\worker.txt",
            $"ExecuteAsync iniciou em {DateTime.Now}\n");

        while (!stoppingToken.IsCancellationRequested)
        {
            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"Loop executado em {DateTime.Now}\n");

            try
            {
                _logger.LogInformation("Iniciando processo da Calculadora...");

                File.AppendAllText(
                    @"C:\temp\worker.txt",
                    $"Chamando AbrirCalculadora() em {DateTime.Now}\n");

                AbrirCalculadora();

                _logger.LogInformation("Calculadora aberta com sucesso.");

                File.AppendAllText(
                    @"C:\temp\worker.txt",
                    $"Retornou de AbrirCalculadora() em {DateTime.Now}\n");

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                _logger.LogInformation("Encerrando Calculadora...");

                foreach (var calc in Process.GetProcessesByName("CalculatorApp"))
                {
                    try
                    {
                        calc.Kill(true);
                        await calc.WaitForExitAsync(stoppingToken);

                        _logger.LogInformation(
                            "Processo encerrado. PID: {Pid}",
                            calc.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Erro ao encerrar processo PID {Pid}",
                            calc.Id);
                    }
                }

                _logger.LogInformation("Aguardando próximo ciclo...");

                await Task.Delay(
                    TimeSpan.FromMinutes(1),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker cancelado.");
                break;
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    @"C:\temp\worker.txt",
                    $"ERRO ExecuteAsync: {ex}\n");

                _logger.LogError(
                    ex,
                    "Erro inesperado durante a execução.");

                await Task.Delay(
                    TimeSpan.FromSeconds(10),
                    stoppingToken);
            }
        }
    }

    private void AbrirCalculadora()
    {
        File.AppendAllText(
            @"C:\temp\worker.txt",
            $"Entrou em AbrirCalculadora(). Session={Process.GetCurrentProcess().SessionId}\n");

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"Session 0 detectada\n");

            _logger.LogInformation(
                "Session 0 detectada. Usando CreateProcessAsUser...");

            Session0Helper.Lancar(
                @"C:\Windows\System32\calc.exe");
        }
        else
        {
            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"Sessão normal detectada\n");

            Process.Start(new ProcessStartInfo
            {
                FileName = "calc.exe",
                UseShellExecute = true
            });
        }
    }

    private static class Session0Helper
    {
        [DllImport("wtsapi32.dll", SetLastError = true)]
        static extern bool WTSQueryUserToken(
            uint sessionId,
            out IntPtr token);

        [DllImport("kernel32.dll")]
        static extern uint WTSGetActiveConsoleSessionId();

        [DllImport(
            "advapi32.dll",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        static extern bool CreateProcessAsUser(
            IntPtr token,
            string? app,
            string? cmd,
            IntPtr pa,
            IntPtr ta,
            bool inherit,
            uint flags,
            IntPtr env,
            string? dir,
            ref STARTUPINFO si,
            out PROCESS_INFORMATION pi);

        [DllImport("userenv.dll", SetLastError = true)]
        static extern bool CreateEnvironmentBlock(
            out IntPtr env,
            IntPtr token,
            bool inherit);

        [DllImport("userenv.dll")]
        static extern bool DestroyEnvironmentBlock(
            IntPtr env);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(
            IntPtr h);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool DuplicateTokenEx(
            IntPtr existing,
            uint access,
            IntPtr attr,
            int impersonation,
            int type,
            out IntPtr newToken);

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }
     public static void Lancar(string exe)
        {
            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[1] Entrou em Lancar() - {DateTime.Now}\n");

            uint session = WTSGetActiveConsoleSessionId();

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[2] SessionId = {session}\n");

            bool ok = WTSQueryUserToken(
                session,
                out var userToken);

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[3] WTSQueryUserToken = {ok}, Token = {userToken}\n");

            ok = DuplicateTokenEx(
                userToken,
                0xF01FF,
                IntPtr.Zero,
                2,
                1,
                out var primary);

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[4] DuplicateTokenEx = {ok}, Primary = {primary}\n");

            ok = CreateEnvironmentBlock(
                out var env,
                primary,
                false);

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[5] CreateEnvironmentBlock = {ok}\n");

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
                dwFlags = 1,
                wShowWindow = 5
            };

            ok = CreateProcessAsUser(
                primary,
                null,
                $"\"{exe}\"",
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x20 | 0x400,
                env,
                null,
                ref si,
                out var pi);

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[6] CreateProcessAsUser = {ok}\n");

            if (!ok)
            {
                File.AppendAllText(
                    @"C:\temp\worker.txt",
                    $"[ERRO] Win32Error = {Marshal.GetLastWin32Error()}\n");
            }
            else
            {
                File.AppendAllText(
                    @"C:\temp\worker.txt",
                    $"[7] PID criado = {pi.dwProcessId}\n");
            }

            if (pi.hProcess != IntPtr.Zero)
                CloseHandle(pi.hProcess);

            if (pi.hThread != IntPtr.Zero)
                CloseHandle(pi.hThread);

            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);

            if (primary != IntPtr.Zero)
                CloseHandle(primary);

            if (env != IntPtr.Zero)
                DestroyEnvironmentBlock(env);

            File.AppendAllText(
                @"C:\temp\worker.txt",
                $"[8] Fim de Lancar()\n");
        }
    }
}