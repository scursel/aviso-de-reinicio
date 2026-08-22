// ============================================================================
//  Aviso de Reinicio - Lembrete diario de reinicio para computadores de caixa
//  ----------------------------------------------------------------------------
//  Windows (WinForms, .NET Framework 4.x). Compila com o csc.exe que ja vem
//  no Windows (execute build.bat). Nao precisa instalar nada nas maquinas.
//
//  Funcionamento:
//   * Fica na bandeja do sistema e, todo dia no horario configurado
//     (padrao 02:00), abre um pop-up pedindo o reinicio.
//   * "OK, adiar" fecha o pop-up; ele volta apos X minutos (padrao 5),
//     repetindo ate o computador ser reiniciado.
//   * "Reiniciar agora" agenda o reinicio do Windows e registra no log.
//   * Se o PC estava desligado na hora do aviso, o aviso aparece logo depois
//     que alguem ligar (apenas se o PC realmente ainda nao foi reiniciado).
//   * Log em CSV: %APPDATA%\AvisoDeReinicio\log.csv
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Aviso de Reinício")]
[assembly: AssemblyDescription("Lembrete diário de reinício para computadores de caixa (PDV)")]
[assembly: AssemblyCompany("Scursel")]
[assembly: AssemblyProduct("Aviso de Reinício")]
[assembly: AssemblyCopyright("Desenvolvido por Scursel")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace AvisoDeReinicio
{
    // ----------------------------- utilitarios ------------------------------
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        public static string AppDir;      // %APPDATA%\AvisoDeReinicio
        public static string ConfigPath;  // config.ini
        public static string LogPath;     // log.csv
        public static string FlagPath;    // reinicio_pendente.flag
        public static string LastBootPath; // ultimo_boot.txt
        public static string LastCheckPath; // ultima_checagem.txt

        [STAThread]
        private static void Main(string[] args)
        {
            // Autoteste (usado na construcao): roda testes determinísticos em um
            // diretorio temporario proprio (nada toca no config/log do usuario)
            // e sai com codigo 0 (ok) ou 1 (falha).
            if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.Combine(Path.GetTempPath(), "AvisoDeReinicioSelftest");
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                InitPaths(dir);
                List<string> falhas = SelfTest.Executar();
                Log("Teste automático",
                    "selftest: " + (falhas.Count == 0 ? "OK" : falhas.Count + " falha(s)"));
                foreach (string f in falhas) LogErro(f);
                Environment.Exit(falhas.Count == 0 ? 0 : 1);
            }

            // Diretorio de dados alternativo, para desenvolvimento/testes:
            // AvisoDeReinicio.exe --appdir <caminho> [--demo|--config]
            string appDir = null;
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "--appdir", StringComparison.OrdinalIgnoreCase))
                { appDir = args[i + 1]; break; }

            // Uma unica instancia por usuario (a instancia de teste usa outro mutex).
            bool createdNew;
            string mutexName = (appDir == null)
                ? @"Local\AvisoDeReinicio_SingleInstance"
                : @"Local\AvisoDeReinicio_SingleInstance_Teste";
            using (Mutex mtx = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew) return;

                InitPaths(appDir);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }

        public static void InitPaths(string appDirOverride)
        {
            AppDir = (appDirOverride != null)
                ? Path.GetFullPath(appDirOverride)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvisoDeReinicio");
            Directory.CreateDirectory(AppDir);
            ConfigPath = Path.Combine(AppDir, "config.ini");
            LogPath = Path.Combine(AppDir, "log.csv");
            FlagPath = Path.Combine(AppDir, "reinicio_pendente.flag");
            LastBootPath = Path.Combine(AppDir, "ultimo_boot.txt");
            LastCheckPath = Path.Combine(AppDir, "ultima_checagem.txt");
        }

        // Momento do ultimo boot (relogio - tempo ligado). GetTickCount64
        // inclui o tempo em suspensao/hibernacao, entao o calculo e confiavel.
        public static Version AppVersion()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? v : new Version(0, 0, 0, 0);
        }

        public static DateTime LastBoot()
        {
            try { return DateTime.Now - TimeSpan.FromMilliseconds((double)GetTickCount64()); }
            catch { return DateTime.Now; }
        }

        public static string Fmt(DateTime dt)
        {
            return dt.ToString("dd/MM/yyyy HH:mm");
        }

        public static void Log(string evento, string detalhe)
        {
            Log(DateTime.Now, evento, detalhe);
        }

        public static void Log(DateTime quando, string evento, string detalhe)
        {
            try
            {
                string linha = quando.ToString("yyyy-MM-dd HH:mm:ss") + ";" + evento + ";" +
                               ((detalhe == null) ? "" : detalhe.Replace(';', ',')) + Environment.NewLine;
                // UTF-8 com BOM: abre certinho no Excel.
                File.AppendAllText(LogPath, linha, new UTF8Encoding(true));
            }
            catch
            {
                // nunca derruba o aplicativo por causa de log
            }
            MaybeRotateLog();
        }

        public const long LogRotateBytes = 2L * 1024 * 1024;

        public static string ArchiveCurrentLog()
        {
            string destName = "log-" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
            string dest = Path.Combine(AppDir, destName);
            if (File.Exists(dest))
            {
                destName = "log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv";
                dest = Path.Combine(AppDir, destName);
            }
            if (File.Exists(LogPath))
                File.Move(LogPath, dest);
            return destName;
        }

        public static void MaybeRotateLog()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                FileInfo fi = new FileInfo(LogPath);
                if (fi.Length < LogRotateBytes) return;
                string dest = ArchiveCurrentLog();
                Log(Eventos.LogArquivado, "rotação automática (>2 MB) → " + dest);
            }
            catch (Exception ex)
            {
                LogErro("rotacao log: " + ex.Message);
            }
        }

        // Problemas tecnicos (raros) vao para um arquivo separado,
        // para o log principal ficar simples.
        public static void LogErro(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDir, "erros.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + msg + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch { }
        }
    }

    // -------------------- calculos temporais (funcoes puras) -----------------
    public enum ModoReinicio { Fixo, Uptime }

    public enum AcaoTick { Nada, Agendar45s, MostrarPopup, MostrarCountdown }

    public class EstadoTick
    {
        public ModoReinicio Modo;
        public DateTime Agora;
        public TimeSpan Uptime;
        public TimeSpan Horario;         // RestartTime (modo fixo)
        public TimeSpan Folga;           // SatisfiedHours (modo fixo)
        public TimeSpan Limite;          // UptimeHours (modo uptime)
        public DateTime? AgendadoAte;    // snooze ou catch-up pendente
        public TimeSpan? UptimeAnterior; // uptime no tick anterior (modo uptime)
        public int OkCount;              // adiamentos do ciclo atual
        public bool ForceEnabled;
        public int MaxOk;
        public bool OpenForm;
        public bool Disabled;
    }

    public class ResultadoTick
    {
        public AcaoTick Acao;
        public DateTime? NovoAgendadoAte;
    }

    // Toda a decisão do agendador vive aqui, como funções puras de
    // (agora, uptime, configuração). O ciclo é DERIVADO, não persistido:
    // apenas um boot novo (uptime zerado) encerra um ciclo em andamento,
    // mesmo após crash, saída ou relançamento do processo.
    public static class Cronologia
    {
        public static readonly TimeSpan AtrasoCatchup = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan JanelaSlotAoVivo = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan GapSuspensao = TimeSpan.FromSeconds(60);

        public static TimeSpan UptimeAgora()
        {
            return TimeSpan.FromMilliseconds((double)Program.GetTickCount64());
        }

        // Slot diário mais recente até agora (<= agora).
        public static DateTime SlotMaisRecente(DateTime agora, TimeSpan horario)
        {
            DateTime hoje = agora.Date.Add(horario);
            return agora >= hoje ? hoje : hoje.AddDays(-1);
        }

        // Uptime que a máquina tinha no instante do slot (<= 0 se bootou depois).
        public static TimeSpan UptimeNoSlot(TimeSpan uptime, DateTime agora, DateTime slot)
        {
            return uptime - (agora - slot);
        }

        // Regra do modo fixo: elegível no slot S  <=>  boot <= S - folga.
        // Avaliada SOMENTE no slot: nunca dispara por uptime completando fora
        // dele (fim da deriva do "boot + 20 h").
        public static bool ElegivelNoSlotFixo(TimeSpan uptime, DateTime agora, TimeSpan horario, TimeSpan folga)
        {
            return UptimeNoSlot(uptime, agora, SlotMaisRecente(agora, horario)) >= folga;
        }

        public static bool VencidoPorUptime(TimeSpan uptime, TimeSpan limite)
        {
            return uptime >= limite;
        }

        public static bool BootouRecentemente(TimeSpan uptime, TimeSpan janela)
        {
            return uptime < janela;
        }

        public static bool CicloDerivado(EstadoTick st)
        {
            if (st.Modo == ModoReinicio.Uptime)
                return VencidoPorUptime(st.Uptime, st.Limite);
            return ElegivelNoSlotFixo(st.Uptime, st.Agora, st.Horario, st.Folga);
        }

        // Decisão completa de um tick. Ordem: desativado/janela aberta ->
        // ciclo derivado -> agendamento pendente (snooze/catch-up) ->
        // primeira observação ("ao vivo" dispara já; "perdida" agenda 45 s).
        public static ResultadoTick Avaliar(EstadoTick st)
        {
            ResultadoTick r = new ResultadoTick();
            // Por padrao o agendamento pendente e' PRESERVADO (o adaptador
            // grava r.NovoAgendadoAte mesmo quando a acao e' Nada).
            r.NovoAgendadoAte = st.AgendadoAte;
            if (st.Disabled || st.OpenForm) { r.Acao = AcaoTick.Nada; return r; }
            if (!CicloDerivado(st)) { r.Acao = AcaoTick.Nada; return r; }

            if (st.AgendadoAte.HasValue)
            {
                if (st.Agora < st.AgendadoAte.Value) { r.Acao = AcaoTick.Nada; return r; }
                // Snooze ou catch-up venceu: dispara agora.
                r.NovoAgendadoAte = null;
                r.Acao = (st.ForceEnabled && st.OkCount >= st.MaxOk)
                    ? AcaoTick.MostrarCountdown : AcaoTick.MostrarPopup;
                return r;
            }

            bool aoVivo;
            if (st.Modo == ModoReinicio.Uptime)
            {
                // "Ao vivo" = o tick anterior ainda estava abaixo do limite e não
                // houve salto grande de uptime (salto = máquina suspensa).
                aoVivo = st.UptimeAnterior.HasValue &&
                         st.UptimeAnterior.Value < st.Limite &&
                         (st.Uptime - st.UptimeAnterior.Value) <= GapSuspensao;
            }
            else
            {
                aoVivo = (st.Agora - SlotMaisRecente(st.Agora, st.Horario)) <= JanelaSlotAoVivo;
            }

            if (aoVivo)
            {
                r.Acao = (st.ForceEnabled && st.OkCount >= st.MaxOk)
                    ? AcaoTick.MostrarCountdown : AcaoTick.MostrarPopup;
            }
            else
            {
                // O slot/vencimento elegível foi perdido com o app fechado ou
                // suspenso: catch-up em ~45 s (uma única vez).
                r.Acao = AcaoTick.Agendar45s;
                r.NovoAgendadoAte = st.Agora + AtrasoCatchup;
            }
            return r;
        }
    }

    // ------------------------ janelas: foco e topmost ------------------------
    internal static class Win32Janela
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr depois, int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;

        // Única tentativa de foco (na abertura). O Windows pode recusar
        // (processo em segundo plano); o pop-up continua topmost e visível.
        public static void TentarFoco(IntPtr hWnd)
        {
            try { SetForegroundWindow(hWnd); } catch { }
        }

        // Reafirma somente a ordem-z (grupo topmost), nunca rouba foco.
        public static void ReafirmarTopmost(IntPtr hWnd)
        {
            try { SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate); } catch { }
        }
    }

    // ------------------------- testes determinísticos -----------------------
    // Roda via --selftest, sempre em diretorio temporário isolado.
    public static class SelfTest
    {
        private static List<string> _falhas = new List<string>();
        private static int _total;

        private static void Check(bool cond, string nome)
        {
            _total++;
            if (!cond) _falhas.Add(nome);
        }

        public static List<string> Executar()
        {
            // ---------- T1: limites N-1 / N / N+1 ----------
            DateTime agora = new DateTime(2026, 8, 19, 14, 0, 0);
            TimeSpan horario = agora.TimeOfDay;   // slot == agora
            TimeSpan folga = TimeSpan.FromHours(20);
            Check(!Cronologia.ElegivelNoSlotFixo(TimeSpan.FromHours(19) + TimeSpan.FromMinutes(59), agora, horario, folga), "T1 fixo 19h59 inelegível");
            Check(Cronologia.ElegivelNoSlotFixo(TimeSpan.FromHours(20), agora, horario, folga), "T1 fixo 20h00 elegível");
            Check(Cronologia.ElegivelNoSlotFixo(TimeSpan.FromHours(20) + TimeSpan.FromMinutes(1), agora, horario, folga), "T1 fixo 20h01 elegível");
            TimeSpan limite = TimeSpan.FromHours(24);
            Check(!Cronologia.VencidoPorUptime(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59), limite), "T1 uptime 23h59");
            Check(Cronologia.VencidoPorUptime(TimeSpan.FromHours(24), limite), "T1 uptime 24h00");
            Check(Cronologia.VencidoPorUptime(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1), limite), "T1 uptime 24h01");

            // ---------- T2: modo fixo (F1-F7) ----------
            // 16/08=sáb 17=seg 18=ter 19=qua 20=qui 21=sex
            Check(AvaliarFixo(new DateTime(2026, 8, 18, 2, 0, 10), TimeSpan.FromHours(25) + TimeSpan.FromSeconds(10)) == AcaoTick.MostrarPopup,
                "F1 boot antes do slot, uptime suficiente: dispara no slot");
            Check(AvaliarFixo(new DateTime(2026, 8, 18, 6, 0, 0), TimeSpan.FromHours(20)) == AcaoTick.Nada,
                "F2 boot seg 10h: NÃO dispara ao completar 20h (era a deriva)");
            Check(AvaliarFixo(new DateTime(2026, 8, 20, 2, 0, 10), TimeSpan.FromHours(40) + TimeSpan.FromSeconds(10)) == AcaoTick.MostrarPopup,
                "F2 dispara no slot do dia seguinte (40h)");
            Check(AvaliarFixo(new DateTime(2026, 8, 18, 9, 0, 0), TimeSpan.FromHours(1)) == AcaoTick.Nada,
                "F3 boot depois do slot: sem catch-up no dia");
            Check(AvaliarFixo(new DateTime(2026, 8, 18, 9, 0, 0), TimeSpan.FromHours(49)) == AcaoTick.Agendar45s,
                "F4 app iniciado após slot elegível perdido: catch-up 45s");
            Check(AvaliarFixo(new DateTime(2026, 8, 18, 9, 0, 0), TimeSpan.FromHours(11)) == AcaoTick.Nada,
                "F5 app iniciado após slot inelegível: nada");
            Check(AvaliarFixoAgendado(new DateTime(2026, 8, 20, 23, 57, 0), TimeSpan.FromHours(60), new DateTime(2026, 8, 21, 0, 0, 0)) == AcaoTick.Nada,
                "F6a snooze futuro atravessando meia-noite: espera");
            Check(AvaliarFixoAgendado(new DateTime(2026, 8, 21, 0, 0, 5), TimeSpan.FromHours(60), new DateTime(2026, 8, 21, 0, 0, 0)) == AcaoTick.MostrarPopup,
                "F6b meia-noite não cancela o ciclo/snooze");
            Check(AvaliarFixo(new DateTime(2026, 8, 20, 2, 0, 10), TimeSpan.FromHours(23) + TimeSpan.FromMinutes(50) + TimeSpan.FromSeconds(10)) == AcaoTick.MostrarPopup,
                "F7 reinício ao aviso 02:10 -> próximo disparo 02:00 (sem deriva)");

            // ---------- T3: modo uptime (U1-U5) ----------
            Check(AvaliarUptime(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59), null, null, 24) == AcaoTick.Nada, "U1 abaixo do limite");
            Check(AvaliarUptime(TimeSpan.FromHours(24), TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59), null, 24) == AcaoTick.MostrarPopup, "U1 vencimento ao vivo: dispara já");
            Check(AvaliarUptime(TimeSpan.FromHours(24), null, null, 24) == AcaoTick.Agendar45s, "U4 relançamento no mesmo boot já vencido: catch-up");
            Check(AvaliarUptime(TimeSpan.FromHours(34), TimeSpan.FromHours(20), null, 24) == AcaoTick.Agendar45s, "U3 acordou de suspensão já vencido: catch-up");
            Check(AvaliarUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25), new DateTime(2026, 8, 21, 12, 5, 0), 24, new DateTime(2026, 8, 21, 12, 3, 0)) == AcaoTick.Nada, "U1b snooze pendente: espera");
            Check(AvaliarUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25), new DateTime(2026, 8, 21, 12, 5, 0), 24, new DateTime(2026, 8, 21, 12, 6, 0)) == AcaoTick.MostrarPopup, "U1c snooze venceu: volta");
            Check(AvaliarUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25), null, 48) == AcaoTick.Nada, "U5 limite alterado 24->48: re-deriva e aguarda");

            EstadoTick aberto = EstadoUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25), null, 24);
            aberto.OpenForm = true;
            Check(Cronologia.Avaliar(aberto).Acao == AcaoTick.Nada, "T3 popup aberto: não decide");
            EstadoTick desativado = EstadoUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25), null, 24);
            desativado.Disabled = true;
            Check(Cronologia.Avaliar(desativado).Acao == AcaoTick.Nada, "T3 DesativarAviso: suprime");

            // Regressão do "snooze apagado": caminhos Nada DEVEM preservar o
            // agendamento pendente em NovoAgendadoAte (o adaptador o grava).
            EstadoTick espera = EstadoUptime(TimeSpan.FromHours(30), TimeSpan.FromHours(25),
                new DateTime(2026, 8, 21, 12, 5, 0), 24);
            espera.Agora = new DateTime(2026, 8, 21, 12, 4, 0);
            ResultadoTick rtEspera = Cronologia.Avaliar(espera);
            Check(rtEspera.Acao == AcaoTick.Nada && rtEspera.NovoAgendadoAte == espera.AgendadoAte,
                "T3b snooze futuro preservado em NovoAgendadoAte");
            EstadoTick desativado2 = EstadoFixo(new DateTime(2026, 8, 21, 12, 4, 0), TimeSpan.FromHours(40));
            desativado2.Disabled = true;
            desativado2.AgendadoAte = new DateTime(2026, 8, 21, 12, 5, 0);
            ResultadoTick rtDesat = Cronologia.Avaliar(desativado2);
            Check(rtDesat.Acao == AcaoTick.Nada && rtDesat.NovoAgendadoAte == desativado2.AgendadoAte,
                "T3b desativado preserva agendamento pendente");

            // ---------- T4: migração e validação de config ----------
            File.WriteAllText(Program.ConfigPath,
                "# config antigo (sem chaves novas)\r\nRestartTime=02:00\r\nSnoozeMinutes=5\r\n");
            ReminderConfig c1 = ReminderConfig.Load();
            Check(c1.Modo == ModoReinicio.Fixo && c1.UptimeHours == 24 && c1.SatisfiedHours == 20, "T4 INI antigo -> padrões (fixo/24/20)");
            Check(c1.AutoUpdate, "T4 INI sem AutoUpdate -> novo padrão ligado");
            File.WriteAllText(Program.ConfigPath, "RestartMode=xyz\r\n");
            ReminderConfig c2 = ReminderConfig.Load();
            Check(c2.Modo == ModoReinicio.Fixo, "T4 RestartMode inválido -> fixo");
            File.WriteAllText(Program.ConfigPath, "RestartMode=Uptime\r\nUptimeHours=999\r\nSatisfiedHours=0\r\n");
            ReminderConfig c3 = ReminderConfig.Load();
            Check(c3.Modo == ModoReinicio.Uptime && c3.UptimeHours == 96 && c3.SatisfiedHours == 1, "T4 clamps (96/1)");
            File.WriteAllText(Program.ConfigPath, "RestartTime=lixo\r\n");
            ReminderConfig c4 = ReminderConfig.Load();
            Check(c4.RestartTime == new TimeSpan(2, 0, 0), "T4 RestartTime inválido -> 02:00");

            // ---------- T6: auto-update (equivalência com a condição antiga) ----------
            TimeSpan[] idades = new TimeSpan[] {
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(31),
                TimeSpan.FromHours(19),
                TimeSpan.FromHours(20),
                TimeSpan.FromHours(25) };
            foreach (TimeSpan idade in idades)
            {
                bool antiga = idade < TimeSpan.FromHours(20) && idade < TimeSpan.FromMinutes(30);
                Check(antiga == Cronologia.BootouRecentemente(idade, TimeSpan.FromMinutes(30)),
                    "T6 equivalência auto-update (" + idade + ")");
            }

            // ---------- T5: selftest não gera eventos de produção ----------
            if (File.Exists(Program.LogPath))
            {
                foreach (string linha in File.ReadAllLines(Program.LogPath))
                    Check(!(linha.Contains("Aviso exibido") || linha.Contains("Adiado")),
                        "T5 nenhum evento de produção no selftest");
            }

            Program.LogErro("selftest: " + (_total - _falhas.Count) + "/" + _total + " verificações OK");
            return _falhas;
        }

        private static EstadoTick EstadoFixo(DateTime agora, TimeSpan uptime)
        {
            EstadoTick st = new EstadoTick();
            st.Modo = ModoReinicio.Fixo;
            st.Agora = agora;
            st.Uptime = uptime;
            st.Horario = new TimeSpan(2, 0, 0);
            st.Folga = TimeSpan.FromHours(20);
            st.MaxOk = 10;
            return st;
        }

        private static AcaoTick AvaliarFixo(DateTime agora, TimeSpan uptime)
        {
            return Cronologia.Avaliar(EstadoFixo(agora, uptime)).Acao;
        }

        private static AcaoTick AvaliarFixoAgendado(DateTime agora, TimeSpan uptime, DateTime agendadoAte)
        {
            EstadoTick st = EstadoFixo(agora, uptime);
            st.AgendadoAte = agendadoAte;
            return Cronologia.Avaliar(st).Acao;
        }

        private static EstadoTick EstadoUptime(TimeSpan uptime, TimeSpan? uptimeAnterior, DateTime? agendadoAte, int limiteHoras)
        {
            EstadoTick st = new EstadoTick();
            st.Modo = ModoReinicio.Uptime;
            st.Agora = new DateTime(2026, 8, 21, 12, 0, 0);
            st.Uptime = uptime;
            st.UptimeAnterior = uptimeAnterior;
            st.AgendadoAte = agendadoAte;
            st.Limite = TimeSpan.FromHours(limiteHoras);
            st.MaxOk = 10;
            return st;
        }

        private static AcaoTick AvaliarUptime(TimeSpan uptime, TimeSpan? uptimeAnterior, DateTime? agendadoAte, int limiteHoras)
        {
            return Cronologia.Avaliar(EstadoUptime(uptime, uptimeAnterior, agendadoAte, limiteHoras)).Acao;
        }

        private static AcaoTick AvaliarUptime(TimeSpan uptime, TimeSpan? uptimeAnterior, DateTime? agendadoAte, int limiteHoras, DateTime agora)
        {
            EstadoTick st = EstadoUptime(uptime, uptimeAnterior, agendadoAte, limiteHoras);
            st.Agora = agora;
            return Cronologia.Avaliar(st).Acao;
        }
    }

    // -------------------- eventos do log (nomes simples) ---------------------
    public static class Eventos
    {
        public const string AvisoExibido = "Aviso exibido";
        public const string AdiadoOk = "Adiado (OK)";
        public const string AdiadoAutomatico = "Adiado (automático)";
        public const string AdiadoJanelaFechada = "Adiado (janela fechada)";
        public const string ReinicioSolicitado = "Reinício solicitado";
        public const string FalhaAoReiniciar = "Falha ao reiniciar";
        public const string ComputadorReiniciado = "Computador reiniciado";
        public const string ContagemRegressiva = "Contagem regressiva";
        public const string ConfiguracoesAlteradas = "Configurações alteradas";
        public const string AvisosDesativados = "Avisos desativados";
        public const string AvisosReativados = "Avisos reativados";
        public const string LogLimpo = "Log limpo";
        public const string LogArquivado = "Log arquivado";
        public const string SenhaIncorreta = "Senha incorreta";
        public const string AtualizacaoDisponivel = "Atualização disponível";
        public const string AtualizacaoAplicada = "Atualização aplicada";
    }

    // ----------------------------- entrada de log ----------------------------
    public class LogEntry
    {
        public DateTime When;
        public string Evento;
        public string Detalhe;

        public LogEntry(DateTime when, string evento, string detalhe)
        {
            When = when; Evento = evento; Detalhe = detalhe;
        }
    }

    public static class LogReader
    {
        public static List<LogEntry> Read(int maxLines)
        {
            List<LogEntry> list = new List<LogEntry>();
            try
            {
                if (!File.Exists(Program.LogPath)) return list;
                foreach (string raw in ReadLastLines(Program.LogPath, maxLines))
                {
                    string l = (raw == null ? "" : raw).Trim().TrimStart('\uFEFF');
                    if (l.Length == 0) continue;
                    string[] p = l.Split(';');
                    DateTime dt;
                    if (p.Length >= 2 &&
                        DateTime.TryParseExact(p[0].Trim(), "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    {
                        list.Add(new LogEntry(dt, p[1].Trim(), p.Length > 2 ? p[2].Trim() : ""));
                    }
                }
            }
            catch { }
            return list;
        }

        // Le as ultimas N linhas sem carregar o arquivo inteiro.
        private static List<string> ReadLastLines(string path, int maxLines)
        {
            List<string> acc = new List<string>();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0) return acc;
                long pos = fs.Length;
                byte[] buf = new byte[4096];
                List<byte> chunk = new List<byte>();
                while (pos > 0 && acc.Count < maxLines)
                {
                    int toRead = (int)Math.Min(buf.Length, pos);
                    pos -= toRead;
                    fs.Seek(pos, SeekOrigin.Begin);
                    int n = fs.Read(buf, 0, toRead);
                    for (int i = n - 1; i >= 0; i--)
                    {
                        if (buf[i] == (byte)'\n')
                        {
                            if (chunk.Count > 0)
                            {
                                acc.Add(BytesToLine(chunk));
                                chunk.Clear();
                                if (acc.Count >= maxLines) break;
                            }
                        }
                        else chunk.Add(buf[i]);
                    }
                }
                if (chunk.Count > 0 && acc.Count < maxLines)
                    acc.Add(BytesToLine(chunk));
            }
            acc.Reverse();
            return acc;
        }

        private static string BytesToLine(List<byte> rev)
        {
            byte[] raw = new byte[rev.Count];
            for (int i = 0; i < rev.Count; i++)
                raw[i] = rev[rev.Count - 1 - i];
            return Encoding.UTF8.GetString(raw).TrimEnd('\r');
        }

        public static int CountAll(string evento)
        {
            int n = 0;
            foreach (LogEntry e in Read(5000))
            {
                if (e.Evento == evento) n++;
            }
            return n;
        }

        public static int CountToday(string evento)
        {
            int n = 0;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            foreach (LogEntry e in Read(5000))
            {
                if (e.Evento == evento && e.When.ToString("yyyy-MM-dd") == today) n++;
            }
            return n;
        }
    }    // ------------------------------ configuracao -----------------------------
    public class ReminderConfig
    {
        public TimeSpan RestartTime = new TimeSpan(2, 0, 0);   // padrao 02:00
        public ModoReinicio Modo = ModoReinicio.Fixo;          // fixo | uptime
        public int UptimeHours = 24;                           // modo uptime: ciclo em horas
        public int SnoozeMinutes = 5;                          // pop-up volta apos X min
        public bool ForceEnabled = false;                      // forca reinicio automatico
        public int MaxOkBeforeForce = 10;                      // apos X "OK" no mesmo ciclo
        public int PopupTimeoutMinutes = 15;                   // sem clique, adia sozinho
        public int SatisfiedHours = 20;                        // boot recente = ja satisfeito
        public string SenhaHash = "";                          // vazio = senha desligada
        public string SenhaSalt = "";
        public bool ProtegerSair = true;
        public bool ProtegerLimparLog = true;
        public bool AutoUpdate = true;                         // padrao: instalar apos boot recente

        public static ReminderConfig Load()
        {
            ReminderConfig c = new ReminderConfig();
            try
            {
                if (File.Exists(Program.ConfigPath))
                {
                    foreach (string raw in File.ReadAllLines(Program.ConfigPath))
                    {
                        string line = (raw == null ? "" : raw).Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = line.Substring(eq + 1).Trim();
                        TimeSpan ts;
                        int n;
                        switch (key)
                        {
                            case "restarttime":
                                if (TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out ts)) c.RestartTime = ts;
                                break;
                            case "restartmode":
                                string mv = val.ToLowerInvariant();
                                if (mv == "uptime") c.Modo = ModoReinicio.Uptime;
                                else if (mv == "fixo") c.Modo = ModoReinicio.Fixo;
                                else if (val.Length > 0)
                                    Program.LogErro("config: RestartMode inválido ('" + val + "'), usando fixo");
                                break;
                            case "uptimehours":
                                if (int.TryParse(val, out n)) c.UptimeHours = Math.Max(1, Math.Min(96, n));
                                break;
                            case "snoozeminutes":
                                if (int.TryParse(val, out n)) c.SnoozeMinutes = Math.Max(1, Math.Min(120, n));
                                break;
                            case "forceenabled":
                                c.ForceEnabled = (val == "1" || val.ToLowerInvariant() == "true");
                                break;
                            case "maxokbeforeforce":
                                if (int.TryParse(val, out n)) c.MaxOkBeforeForce = Math.Max(1, Math.Min(50, n));
                                break;
                            case "popuptimeoutminutes":
                                if (int.TryParse(val, out n)) c.PopupTimeoutMinutes = Math.Max(1, Math.Min(120, n));
                                break;
                            case "satisfiedhours":
                                if (int.TryParse(val, out n)) c.SatisfiedHours = Math.Max(1, Math.Min(48, n));
                                break;
                            case "senhahash":
                                c.SenhaHash = val;
                                break;
                            case "senhasalt":
                                c.SenhaSalt = val;
                                break;
                            case "protegersair":
                                c.ProtegerSair = (val == "1" || val.ToLowerInvariant() == "true");
                                break;
                            case "protegerlimparlog":
                                c.ProtegerLimparLog = (val == "1" || val.ToLowerInvariant() == "true");
                                break;
                            case "autoupdate":
                                c.AutoUpdate = (val == "1" || val.ToLowerInvariant() == "true");
                                break;
                        }
                    }
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Configuracao do AvisoDeReinicio");
                sb.AppendLine("RestartTime=" + RestartTime.ToString(@"hh\:mm"));
                sb.AppendLine("RestartMode=" + (Modo == ModoReinicio.Uptime ? "uptime" : "fixo"));
                sb.AppendLine("UptimeHours=" + UptimeHours);
                sb.AppendLine("SnoozeMinutes=" + SnoozeMinutes);
                sb.AppendLine("ForceEnabled=" + (ForceEnabled ? "1" : "0"));
                sb.AppendLine("MaxOkBeforeForce=" + MaxOkBeforeForce);
                sb.AppendLine("PopupTimeoutMinutes=" + PopupTimeoutMinutes);
                sb.AppendLine("SatisfiedHours=" + SatisfiedHours);
                sb.AppendLine("SenhaHash=" + (SenhaHash == null ? "" : SenhaHash));
                sb.AppendLine("SenhaSalt=" + (SenhaSalt == null ? "" : SenhaSalt));
                sb.AppendLine("ProtegerSair=" + (ProtegerSair ? "1" : "0"));
                sb.AppendLine("ProtegerLimparLog=" + (ProtegerLimparLog ? "1" : "0"));
                sb.AppendLine("AutoUpdate=" + (AutoUpdate ? "1" : "0"));
                File.WriteAllText(Program.ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // Senha de supervisor (opt-in). SenhaHash vazio = recurso desligado.
    // PBKDF2-HMAC-SHA1 via Rfc2898DeriveBytes (mscorlib, sem referencia nova).
    public static class Supervisor
    {
        public const int Iterations = 100000;
        public const int SaltSize = 16;
        public const int HashSize = 32;

        public static bool IsEnabled(ReminderConfig cfg)
        {
            return cfg != null && cfg.SenhaHash != null && cfg.SenhaHash.Length > 0;
        }

        public static void SetPassword(ReminderConfig cfg, string password)
        {
            byte[] salt = new byte[SaltSize];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);
            cfg.SenhaSalt = Convert.ToBase64String(salt);
            cfg.SenhaHash = Hash(password, salt);
        }

        public static void ClearPassword(ReminderConfig cfg)
        {
            cfg.SenhaHash = "";
            cfg.SenhaSalt = "";
        }

        public static bool Verify(ReminderConfig cfg, string password)
        {
            if (!IsEnabled(cfg)) return true;
            byte[] salt;
            try { salt = Convert.FromBase64String(cfg.SenhaSalt == null ? "" : cfg.SenhaSalt); }
            catch { return false; }
            return FixedTimeEquals(Hash(password, salt), cfg.SenhaHash);
        }

        private static string Hash(string password, byte[] salt)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(password == null ? "" : password, salt, Iterations))
                return Convert.ToBase64String(kdf.GetBytes(HashSize));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            int max = Math.Max(ba.Length, bb.Length);
            int diff = ba.Length ^ bb.Length;
            for (int i = 0; i < max; i++)
            {
                byte xa = i < ba.Length ? ba[i] : (byte)0;
                byte xb = i < bb.Length ? bb[i] : (byte)0;
                diff |= xa ^ xb;
            }
            return diff == 0;
        }
    }

    public static class PasswordPrompt
    {
        public static bool Ask(IWin32Window owner, ReminderConfig cfg, string motivo)
        {
            if (!Supervisor.IsEnabled(cfg)) return true;

            using (Form f = new Form())
            {
                f.Text = "Senha de supervisor";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.ClientSize = new Size(340, 128);
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

                Label l = new Label();
                l.Text = motivo;
                l.SetBounds(12, 12, 316, 20);
                TextBox tb = new TextBox();
                tb.UseSystemPasswordChar = true;
                tb.SetBounds(12, 38, 316, 24);
                Button ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(164, 80, 80, 28);
                Button cancel = new Button();
                cancel.Text = "Cancelar";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(250, 80, 80, 28);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Controls.Add(l);
                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);

                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (Supervisor.Verify(cfg, tb.Text)) return true;
                Program.Log(Eventos.SenhaIncorreta, motivo);
                MessageBox.Show(owner, "Senha incorreta.", "Aviso de Reinício",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
    }

    // ------------------- inicio automatico (registro HKCU) -------------------
    public static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AvisoDeReinicio";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    if (enabled) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }

    // Atualizacao pelo GitHub: 302 em /releases/latest, sem api.github.com.
    public class UpdateInfo
    {
        public string Tag;
        public Version Version;
        public string InstallerPath;
    }

    public static class Updater
    {
        public const string LatestUrl = "https://github.com/scursel/aviso-de-reinicio/releases/latest";
        public const string DownloadRoot = "https://github.com/scursel/aviso-de-reinicio/releases/download/";
        private static bool _tlsReady;

        public static void EnsureTls()
        {
            if (_tlsReady) return;
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
            _tlsReady = true;
        }

        public static string UserAgent()
        {
            return "AvisoDeReinicio/" + Program.AppVersion().ToString();
        }

        public static Version Normalize(Version v)
        {
            if (v == null) return new Version(0, 0, 0);
            int b = v.Build < 0 ? 0 : v.Build;
            return new Version(v.Major, v.Minor, b);
        }

        // So devolve info se a tag remota for MAIOR que a versao local.
        public static UpdateInfo CheckLatest()
        {
            try
            {
                string loc = GetRedirectLocation(LatestUrl);
                if (string.IsNullOrEmpty(loc)) return null;
                int ix = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                if (ix < 0) return null;
                string tag = loc.Substring(ix + 5).Trim();
                int q = tag.IndexOfAny(new char[] { '?', '#' });
                if (q >= 0) tag = tag.Substring(0, q);
                tag = tag.Trim('/');
                if (tag.Length == 0) return null;

                string num = tag.Trim();
                if (num.Length > 0 && (num[0] == 'v' || num[0] == 'V'))
                    num = num.Substring(1);
                Version remote;
                if (!Version.TryParse(num, out remote)) return null;
                if (Normalize(remote).CompareTo(Normalize(Program.AppVersion())) <= 0)
                    return null;

                UpdateInfo info = new UpdateInfo();
                info.Tag = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag : ("v" + tag);
                info.Version = remote;
                return info;
            }
            catch
            {
                return null;
            }
        }

        public static bool DownloadAndVerify(UpdateInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Tag)) return false;
            try
            {
                string fileName = "Instalador-AvisoDeReinicio-" + info.Tag + ".exe";
                string dest = Path.Combine(Path.GetTempPath(), fileName);
                string sumsUrl = DownloadRoot + info.Tag + "/SHA256SUMS.txt";
                string exeUrl = DownloadRoot + info.Tag + "/" + fileName;

                string sums = DownloadText(sumsUrl, 10000);
                if (string.IsNullOrEmpty(sums)) return false;
                string expected = FindHash(sums, fileName);
                if (string.IsNullOrEmpty(expected)) return false;

                if (!DownloadFile(exeUrl, dest, 60000)) return false;
                string actual = FileSha256(dest);
                if (actual == null || string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    try { File.Delete(dest); } catch { }
                    return false;
                }
                info.InstallerPath = dest;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool LaunchInstaller(UpdateInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.InstallerPath) || !File.Exists(info.InstallerPath))
                return false;
            string tasks = Autostart.IsEnabled() ? "autostart" : "";
            string args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=\"" + tasks + "\"";
            Process.Start(info.InstallerPath, args);
            return true;
        }

        private static string GetRedirectLocation(string url)
        {
            EnsureTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.AllowAutoRedirect = false;
            req.UserAgent = UserAgent();
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return resp.Headers[HttpResponseHeader.Location];
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp == null) return null;
                using (resp)
                    return resp.Headers[HttpResponseHeader.Location];
            }
        }

        private static string DownloadText(string url, int timeoutMs)
        {
            EnsureTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 8;
            req.UserAgent = UserAgent();
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static bool DownloadFile(string url, string dest, int timeoutMs)
        {
            EnsureTls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 8;
            req.UserAgent = UserAgent();
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(dest))
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    dst.Write(buf, 0, n);
            }
            return File.Exists(dest) && new FileInfo(dest).Length > 0;
        }

        private static string FindHash(string sums, string fileName)
        {
            string[] lines = sums.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                string hash = line.Substring(0, sp).Trim();
                string name = line.Substring(sp).Trim();
                if (name.Length >= 2 && name[0] == '*') name = name.Substring(1);
                name = name.Replace('/', '\\');
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(name), fileName, StringComparison.OrdinalIgnoreCase))
                    return hash;
            }
            return null;
        }

        private static string FileSha256(string path)
        {
            using (SHA256Managed sha = new SHA256Managed())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
    // ------------------------- app da bandeja (nucleo) -----------------------
    public class TrayApp : ApplicationContext
    {
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _timer;
        private ReminderConfig _cfg;
        private DateTime? _agendadoAte;     // snooze ou catch-up pendente
        private TimeSpan? _uptimeAnterior;  // uptime no tick anterior
        private int _okCount;               // adiamentos do ciclo atual
        private DateTime _guardShutdownAte = DateTime.MinValue;  // janela do shutdown.exe
        private Form _openForm;              // pop-up/countdown/config aberto
        private ConfigForm _configForm;      // uma unica tela de configuracoes
        private bool _forceDemo;
        private bool _disabled;
        private MenuItem _menuUpdate;
        private UpdateInfo _pendingUpdate;
        private volatile bool _updateBusy;
        private volatile bool _checkDone;
        private DateTime _lastCheckDay = DateTime.MinValue;

        public TrayApp()
        {
            _cfg = ReminderConfig.Load();
            Program.MaybeRotateLog();

            // Detecta reinicio comparando o boot atual com o ultimo conhecido.
            // A flag so diferencia "pelo app" de "por fora" (menu Iniciar, Update).
            DateTime boot = Program.LastBoot();
            DateTime previous = DateTime.MinValue;
            try
            {
                if (File.Exists(Program.LastBootPath))
                {
                    string raw = File.ReadAllText(Program.LastBootPath).Trim();
                    DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out previous);
                }
            }
            catch { }

            bool flagExistia = false;
            try
            {
                if (File.Exists(Program.FlagPath))
                {
                    flagExistia = true;
                    File.Delete(Program.FlagPath);
                }
            }
            catch { }

            if (previous != DateTime.MinValue && Math.Abs((boot - previous).TotalMinutes) > 2)
            {
                Program.Log(boot, Eventos.ComputadorReiniciado, flagExistia ? "pelo app" : "por fora");
            }

            try
            {
                File.WriteAllText(Program.LastBootPath, boot.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch { }

            // Bandeja
            try
            {
                Icon icon;
                try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { icon = SystemIcons.Application; }

                _tray = new NotifyIcon();
                _tray.Icon = icon;
                _tray.Text = "Aviso de Reinicio - lembrete de reinicio diario";
                _tray.Visible = true;

                ContextMenu menu = new ContextMenu();
                menu.MenuItems.Add("Abrir configurações...", OnOpenConfig);
                menu.MenuItems.Add("Testar pop-up agora", OnTestPopup);
                menu.MenuItems.Add("Abrir pasta de dados (log)", OnOpenFolder);
                _menuUpdate = menu.MenuItems.Add("Atualizar…", OnUpdateNow);
                _menuUpdate.Visible = false;
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Sair", OnExit);
                _tray.ContextMenu = menu;
                _tray.DoubleClick += delegate { ShowConfig(); };
            }
            catch (Exception ex)
            {
                Program.LogErro("bandeja: " + ex.Message);
            }

            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) _forceDemo = true;
                if (string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase)) ShowConfig();
            }

            LoadLastCheckDay();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15000;                 // checa a cada 15 s
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // --------------------------- logica do lembrete -----------------------
        // O agendador inteiro delega para Cronologia.Avaliar (funcao pura).
        // O ciclo e derivado de (agora, uptime, config): so um boot novo o
        // encerra. Meia-noite nao cancela ciclo, snooze ou contadores.
        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                MaybeCheckUpdate();

                // DesativarAviso.txt e reavaliado a cada tick (criar/apagar vale sem reiniciar).
                bool nowDisabled = File.Exists(Path.Combine(Program.AppDir, "DesativarAviso.txt"));
                if (nowDisabled != _disabled)
                {
                    _disabled = nowDisabled;
                    if (_disabled)
                        Program.Log(Eventos.AvisosDesativados, "arquivo DesativarAviso.txt presente");
                    else
                        Program.Log(Eventos.AvisosReativados, "arquivo DesativarAviso.txt removido");
                }

                TimeSpan uptime = Cronologia.UptimeAgora();

                if (_openForm != null) { _uptimeAnterior = uptime; return; }   // ja tem aviso na tela
                if (_disabled) { _uptimeAnterior = uptime; return; }           // maquina isenta
                // flag de teste/desenvolvimento: dispara o pop-up na 1a checagem
                if (_forceDemo)
                {
                    _forceDemo = false;
                    ShowPopupTeste();
                    _uptimeAnterior = uptime;
                    return;
                }
                // Janela de 10 s do shutdown.exe: nao reabrir pop-up fantasma.
                if (DateTime.Now < _guardShutdownAte) { _uptimeAnterior = uptime; return; }

                EstadoTick st = new EstadoTick();
                st.Modo = _cfg.Modo;
                st.Agora = DateTime.Now;
                st.Uptime = uptime;
                st.UptimeAnterior = _uptimeAnterior;
                st.Horario = _cfg.RestartTime;
                st.Folga = TimeSpan.FromHours(Math.Max(1, Math.Min(48, _cfg.SatisfiedHours)));
                st.Limite = TimeSpan.FromHours(Math.Max(1, Math.Min(96, _cfg.UptimeHours)));
                st.AgendadoAte = _agendadoAte;
                st.OkCount = _okCount;
                st.ForceEnabled = _cfg.ForceEnabled;
                st.MaxOk = Math.Max(1, _cfg.MaxOkBeforeForce);
                st.OpenForm = (_openForm != null);
                st.Disabled = _disabled;

                _uptimeAnterior = uptime;
                ResultadoTick r = Cronologia.Avaliar(st);

                switch (r.Acao)
                {
                    case AcaoTick.Agendar45s:
                        _agendadoAte = r.NovoAgendadoAte;
                        break;
                    case AcaoTick.MostrarPopup:
                        _agendadoAte = null;
                        ShowPopup();
                        break;
                    case AcaoTick.MostrarCountdown:
                        _agendadoAte = null;
                        ShowCountdown();
                        break;
                    default:
                        _agendadoAte = r.NovoAgendadoAte;
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.LogErro("tick: " + ex.Message);
            }
        }

        private void ShowPopup()
        {
            if (_openForm != null) return;
            PopupForm f = new PopupForm(_cfg, false, _okCount);
            f.RestartRequested += DoRestartManual;
            f.SnoozeRequested += OnSnooze;
            f.AutoSnoozeRequested += OnAutoSnooze;
            f.CloseSnoozeRequested += OnCloseSnooze;
            _openForm = f;
            f.FormClosed += delegate { _openForm = null; };
            Program.Log(Eventos.AvisoExibido,
                (LogReader.CountToday(Eventos.AvisoExibido) + 1) + "º aviso do dia");
            f.Show();
        }

        // Pop-up de teste ("Testar agora" / --demo): isolado do agendador.
        // Nao inicia ciclo, nao agenda snooze, nao grava eventos de producao
        // e nunca provoca contagem regressiva.
        private void ShowPopupTeste()
        {
            if (_openForm != null) return;
            PopupForm f = new PopupForm(_cfg, true, 0);
            _openForm = f;
            f.FormClosed += delegate { _openForm = null; };
            Program.Log("Teste automático", "pop-up de teste exibido");
            f.Show();
        }

        private void ShowCountdown()
        {
            if (_openForm != null) return;
            CountdownForm f = new CountdownForm(_cfg);
            f.RestartRequested += DoRestartForced;
            f.SnoozeRequested += OnSnooze;
            f.CloseSnoozeRequested += OnCloseSnooze;
            _openForm = f;
            f.FormClosed += delegate { _openForm = null; };
            Program.Log(Eventos.ContagemRegressiva, "reinício automático em 60 s");
            f.Show();
        }

        private void DoRestartManual() { DoRestart(false); }
        private void DoRestartForced(bool forced) { DoRestart(forced); }

        private void ScheduleSnooze()
        {
            _agendadoAte = DateTime.Now.AddMinutes(Math.Max(1, _cfg.SnoozeMinutes));
        }

        private void OnSnooze()                      // botao OK / "Adiar" da contagem
        {
            _okCount++;
            ScheduleSnooze();
            Program.Log(Eventos.AdiadoOk, "próximo aviso em " + Math.Max(1, _cfg.SnoozeMinutes) + " min");
        }

        private void OnAutoSnooze()                  // timeout sem interacao (nao conta para a forca)
        {
            ScheduleSnooze();
        }

        private void OnCloseSnooze()                 // Alt+F4: o formulario ja registrou o log
        {
            _okCount++;
            ScheduleSnooze();
        }

        private void DoRestart(bool forced)
        {
            try
            {
                File.WriteAllText(Program.FlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                Program.LogErro("flag: " + ex.Message);
            }

            string origem = forced ? "automático" : "pelo operador";
            string msg = forced
                ? "Reinício automático programado (Aviso de Reinício)"
                : "Reinício solicitado pelo operador (Aviso de Reinício)";
            string args = "/r /t 10 " + (forced ? "/f " : "") + "/c \"" + msg + "\"";

            if (TryShutdown(args, origem, false))
                return;
            TryShutdown("/r /t 10", origem, true);
        }

        private bool TryShutdown(string args, string origem, bool fallback)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "shutdown.exe";
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                        throw new InvalidOperationException("Process.Start retornou null");
                    p.WaitForExit(5000);
                    if (!p.HasExited || p.ExitCode != 0)
                    {
                        string err = "";
                        try { err = p.StandardError.ReadToEnd(); } catch { }
                        string detalhe = "exit " + (p.HasExited ? p.ExitCode.ToString() : "timeout") +
                                         (err.Length > 0 ? " · " + err.Trim() : "");
                        if (fallback)
                        {
                            Program.LogErro("shutdown fallback: " + detalhe);
                            Program.Log(Eventos.FalhaAoReiniciar, detalhe);
                            return false;
                        }
                        Program.LogErro("shutdown: " + detalhe);
                        return false;
                    }
                }
                Program.Log(Eventos.ReinicioSolicitado, fallback ? origem + " (fallback)" : origem);
                // Evita pop-up fantasma na janela de 10 s do shutdown.exe.
                _guardShutdownAte = DateTime.Now.AddMinutes(3);
                _agendadoAte = null;
                return true;
            }
            catch (Exception ex)
            {
                if (fallback)
                {
                    Program.LogErro("shutdown fallback: " + ex.Message);
                    Program.Log(Eventos.FalhaAoReiniciar, ex.Message);
                }
                else
                {
                    Program.LogErro("shutdown: " + ex.Message);
                }
                return false;
            }
        }

        // ------------------------------ menus ---------------------------------
        private void LoadLastCheckDay()
        {
            try
            {
                if (!File.Exists(Program.LastCheckPath)) return;
                DateTime d;
                if (DateTime.TryParseExact(File.ReadAllText(Program.LastCheckPath).Trim(),
                    "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    _lastCheckDay = d;
            }
            catch { }
        }

        private void SaveLastCheckDay()
        {
            try
            {
                File.WriteAllText(Program.LastCheckPath, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            catch { }
        }

        private void MaybeCheckUpdate()
        {
            if (_checkDone)
            {
                _checkDone = false;
                _updateBusy = false;
                _lastCheckDay = DateTime.Today;
                SaveLastCheckDay();
                HandleUpdateResult(_pendingUpdate);
                return;
            }
            if (_updateBusy) return;
            if (_lastCheckDay.Date == DateTime.Today) return;
            _updateBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                try { info = Updater.CheckLatest(); }
                catch { }
                _pendingUpdate = info;
                _checkDone = true;
            });
        }

        private void HandleUpdateResult(UpdateInfo info)
        {
            if (info == null) return;
            try
            {
                if (_menuUpdate != null)
                {
                    _menuUpdate.Text = "Atualizar para a " + info.Tag;
                    _menuUpdate.Visible = true;
                }
                Program.Log(Eventos.AtualizacaoDisponivel, info.Tag);
                if (_tray != null)
                    _tray.ShowBalloonTip(10000, "Aviso de Reinício",
                        "Nova versão " + info.Tag + " disponível.", ToolTipIcon.Info);
            }
            catch { }

            // Auto-instala so com AutoUpdate=1 e logo apos um boot recente.
            // Equivalente a condicao antiga (SatisfiedToday && <30 min), que ja
            // era dominada pelos 30 minutos.
            if (_cfg.AutoUpdate && Cronologia.BootouRecentemente(
                    Cronologia.UptimeAgora(), TimeSpan.FromMinutes(30)))
                ApplyUpdate(info);
        }

        private void OnUpdateNow(object sender, EventArgs e)
        {
            if (_pendingUpdate != null) ApplyUpdate(_pendingUpdate);
        }

        private void ApplyUpdate(UpdateInfo info)
        {
            if (info == null) return;
            if (_updateBusy) return;
            _updateBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = Updater.DownloadAndVerify(info) && Updater.LaunchInstaller(info); }
                catch { }
                if (!ok)
                {
                    _updateBusy = false;
                    return;
                }
                try { Program.Log(Eventos.AtualizacaoAplicada, info.Tag); } catch { }
                try
                {
                    if (_timer != null) _timer.Stop();
                    if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                }
                catch { }
                Environment.Exit(0);
            });
        }

        private void OnOpenConfig(object sender, EventArgs e) { ShowConfig(); }

        private void OnTestPopup(object sender, EventArgs e) { ShowPopupTeste(); }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try { Process.Start("explorer.exe", "\"" + Program.AppDir + "\""); }
            catch { }
        }

        private void OnExit(object sender, EventArgs e)
        {
            if (_cfg.ProtegerSair && !PasswordPrompt.Ask(null, _cfg, "Sair do Aviso de Reinício"))
                return;
            try { _timer.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            ExitThread();
        }

        public void ShowConfig()
        {
            if (_configForm != null && !_configForm.IsDisposed)
            {
                _configForm.Activate();
                _configForm.BringToFront();
                return;
            }

            if (!PasswordPrompt.Ask(null, _cfg, "Abrir configurações"))
                return;

            ConfigForm f = new ConfigForm(_cfg);
            f.ConfigSaved += OnConfigSaved;
            _configForm = f;
            // Impede que o pop-up surja por cima da tela de configuracoes.
            if (_openForm == null) _openForm = f;
            f.FormClosed += delegate
            {
                if (_openForm == f) _openForm = null;
                if (_configForm == f) _configForm = null;
            };
            f.Show();
            f.Activate();
        }

        private void OnConfigSaved()
        {
            _cfg = ReminderConfig.Load();
            // Re-deriva o ciclo do zero com a configuracao nova (inclusive
            // troca de modo fixo <-> uptime).
            _agendadoAte = null;
            _uptimeAnterior = null;
            _okCount = 0;
        }
    }    // ------------------------- pop-up de reinicio ----------------------------
    public class PopupForm : Form
    {
        public event Action RestartRequested;
        public event Action SnoozeRequested;
        public event Action AutoSnoozeRequested;
        public event Action CloseSnoozeRequested;    // fechado pelo usuario sem decisao
        private bool _done;
        private bool _teste;
        private System.Windows.Forms.Timer _watch;
        private DateTime _shownAt;
        private int _timeoutMin;
        private int _snoozeMin;

        public PopupForm(ReminderConfig cfg, bool teste, int adiamentosCiclo)
        {
            _teste = teste;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;                      // so sai pelos botoes
            ShowInTaskbar = false;
            ClientSize = new Size(480, 250);
            Text = "Aviso de Reinício";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _timeoutMin = Math.Max(1, Math.Min(120, cfg.PopupTimeoutMinutes));
            _snoozeMin = Math.Max(1, cfg.SnoozeMinutes);
            _shownAt = DateTime.Now;

            DateTime boot = Program.LastBoot();

            Label titulo = new Label();
            titulo.Text = "Reinicialização necessária";
            titulo.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            titulo.ForeColor = Color.FromArgb(180, 30, 30);
            titulo.SetBounds(16, 12, 450, 30);

            Label corpo = new Label();
            corpo.Text =
                "Este computador deve ser reiniciado diariamente para manter o sistema do caixa estável.\n\n" +
                "Último reinício: " + Program.Fmt(boot) + "  (" + UptimeText(boot) + ")\n" +
                (cfg.Modo == ModoReinicio.Uptime
                    ? "Ciclo configurado: avisar após " + cfg.UptimeHours + " h ligado"
                    : "Aviso diário configurado às " + cfg.RestartTime.ToString(@"hh\:mm"));
            corpo.Font = new Font("Segoe UI", 10F);
            corpo.SetBounds(16, 48, 450, 100);

            Label adiamentos = new Label();
            adiamentos.Text = "Você já adiou " + adiamentosCiclo + " vez(es) neste ciclo.";
            adiamentos.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
            adiamentos.ForeColor = Color.Gray;
            adiamentos.SetBounds(16, 156, 450, 24);

            Button btnReiniciar = new Button();
            btnReiniciar.Text = "Reiniciar agora";
            btnReiniciar.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnReiniciar.BackColor = Color.FromArgb(31, 130, 60);
            btnReiniciar.ForeColor = Color.White;
            btnReiniciar.FlatStyle = FlatStyle.Flat;
            btnReiniciar.SetBounds(16, 196, 190, 38);
            btnReiniciar.Click += delegate
            {
                if (_teste)
                {
                    MessageBox.Show(this,
                        "Este é um pop-up de teste — o computador não será reiniciado.",
                        "Aviso de Reinício", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Finish(true);
            };

            Button btnOk = new Button();
            btnOk.Text = "OK, adiar por " + cfg.SnoozeMinutes + " min";
            btnOk.Font = new Font("Segoe UI", 10F);
            btnOk.SetBounds(222, 196, 242, 38);
            btnOk.Click += delegate { Finish(false); };

            Controls.Add(titulo);
            Controls.Add(corpo);
            Controls.Add(adiamentos);
            Controls.Add(btnReiniciar);
            Controls.Add(btnOk);

            CancelButton = btnOk;                    // Esc = adiar
            Shown += delegate
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                // Unica tentativa de foco; o Windows pode recusar. Depois disso
                // so reafirmamos a ordem-z, nunca mais o foco.
                Win32Janela.TentarFoco(Handle);
                Win32Janela.ReafirmarTopmost(Handle);
            };

            _watch = new System.Windows.Forms.Timer();
            _watch.Interval = 15000;
            _watch.Tick += OnWatchTick;
            _watch.Start();
            FormClosed += delegate
            {
                try { if (_watch != null) { _watch.Stop(); _watch.Dispose(); } } catch { }
            };
        }

        private void OnWatchTick(object sender, EventArgs e)
        {
            // Reafirma apenas a ordem-z (grupo topmost), sem roubar foco.
            if (IsHandleCreated) Win32Janela.ReafirmarTopmost(Handle);

            if ((DateTime.Now - _shownAt).TotalMinutes >= _timeoutMin)
                FinishAuto();
        }

        private static string UptimeText(DateTime boot)
        {
            TimeSpan u = DateTime.Now - boot;
            if (u.TotalHours < 1) return Math.Max(1, (int)u.TotalMinutes) + " min ligado";
            return u.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + " h ligado";
        }

        private void Finish(bool restart)
        {
            if (_done) return;
            _done = true;
            try { if (_watch != null) _watch.Stop(); } catch { }
            if (restart)
            {
                if (RestartRequested != null) RestartRequested();
            }
            else if (!_teste)
            {
                if (SnoozeRequested != null) SnoozeRequested();
            }
            Close();
        }

        private void FinishAuto()
        {
            if (_done) return;
            _done = true;
            try { if (_watch != null) _watch.Stop(); } catch { }
            if (!_teste)
            {
                Program.Log(Eventos.AdiadoAutomatico,
                    "sem interação por " + _timeoutMin + " min · próximo aviso em " + _snoozeMin + " min");
                if (AutoSnoozeRequested != null) AutoSnoozeRequested();
            }
            Close();
        }

        // Alt+F4 / fechamento pelo usuario sem decisao vira adiamento (exatamente
        // um log e um snooze, protegidos por _done). Fechamentos por shutdown do
        // Windows ou saida do aplicativo nao geram snooze. Sem Close() recursivo.
        // TaskManagerClosing entra no caso "usuario": e' o que o WinForms entrega
        // para WM_CLOSE externo (Alt+F4 simulado, "Finalizar tarefa" etc.).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_done)
            {
                _done = true;
                try { if (_watch != null) _watch.Stop(); } catch { }
                if (!_teste && (e.CloseReason == CloseReason.UserClosing ||
                                e.CloseReason == CloseReason.TaskManagerClosing))
                {
                    Program.Log(Eventos.AdiadoJanelaFechada, "pop-up fechado pelo usuário");
                    if (CloseSnoozeRequested != null) CloseSnoozeRequested();
                }
            }
            base.OnFormClosing(e);
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }
    }

    // ------------------------ contagem regressiva ----------------------------
    public class CountdownForm : Form
    {
        public event Action<bool> RestartRequested;   // bool = forcado
        public event Action SnoozeRequested;
        public event Action CloseSnoozeRequested;     // fechada pelo usuario sem decisao
        private System.Windows.Forms.Timer _t;
        private int _secondsLeft = 60;
        private bool _done;
        private Label _count;

        public CountdownForm(ReminderConfig cfg)
        {
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(500, 250);
            Text = "Reinício automático";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label titulo = new Label();
            titulo.Text = "Muitos adiamentos - reinício automático";
            titulo.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            titulo.ForeColor = Color.FromArgb(180, 30, 30);
            titulo.SetBounds(16, 12, 470, 30);

            Label corpo = new Label();
            corpo.Text =
                "O reinício foi adiado várias vezes. Para garantir a estabilidade do caixa, " +
                "o computador será reiniciado automaticamente.";
            corpo.Font = new Font("Segoe UI", 10F);
            corpo.SetBounds(16, 50, 470, 46);

            _count = new Label();
            _count.Text = "60 s";
            _count.Font = new Font("Segoe UI", 26F, FontStyle.Bold);
            _count.ForeColor = Color.FromArgb(180, 30, 30);
            _count.TextAlign = ContentAlignment.MiddleCenter;
            _count.SetBounds(16, 100, 470, 52);

            Button btnAgora = new Button();
            btnAgora.Text = "Reiniciar agora";
            btnAgora.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnAgora.BackColor = Color.FromArgb(31, 130, 60);
            btnAgora.ForeColor = Color.White;
            btnAgora.FlatStyle = FlatStyle.Flat;
            btnAgora.SetBounds(16, 196, 190, 38);
            btnAgora.Click += delegate { Finish(false); };   // clique manual = nao forcado

            Button btnAdiar = new Button();
            btnAdiar.Text = "Adiar " + cfg.SnoozeMinutes + " min";
            btnAdiar.Font = new Font("Segoe UI", 10F);
            btnAdiar.SetBounds(222, 196, 262, 38);
            btnAdiar.Click += delegate { FinishSnooze(); };

            Controls.Add(titulo);
            Controls.Add(corpo);
            Controls.Add(_count);
            Controls.Add(btnAgora);
            Controls.Add(btnAdiar);

            Shown += delegate
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                // Unica tentativa de foco; depois so ordem-z, nunca mais foco.
                Win32Janela.TentarFoco(Handle);
                Win32Janela.ReafirmarTopmost(Handle);
            };

            _t = new System.Windows.Forms.Timer();
            _t.Interval = 1000;
            _t.Tick += delegate
            {
                // Reafirma apenas a ordem-z (grupo topmost), sem roubar foco.
                if (IsHandleCreated) Win32Janela.ReafirmarTopmost(Handle);
                _secondsLeft--;
                if (_secondsLeft <= 0)
                {
                    Finish(true);                       // estourou o tempo = forcado
                    return;
                }
                _count.Text = _secondsLeft + " s";
            };
            _t.Start();
        }

        private void FinishSnooze()
        {
            if (_done) return;
            _done = true;
            try { _t.Stop(); } catch { }
            if (SnoozeRequested != null) SnoozeRequested();
            Close();
        }

        private void Finish(bool forced)
        {
            if (_done) return;
            _done = true;
            try { _t.Stop(); } catch { }
            if (RestartRequested != null) RestartRequested(forced);
            Close();
        }

        // Alt+F4 durante a contagem vira adiamento (uma unica vez), como no pop-up.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_done)
            {
                _done = true;
                try { _t.Stop(); } catch { }
                if (e.CloseReason == CloseReason.UserClosing ||
                    e.CloseReason == CloseReason.TaskManagerClosing)
                {
                    Program.Log(Eventos.AdiadoJanelaFechada, "contagem regressiva fechada pelo usuário");
                    if (CloseSnoozeRequested != null) CloseSnoozeRequested();
                }
            }
            base.OnFormClosing(e);
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }
    }    // ------------------------- tela de configuracao --------------------------
    public class ConfigForm : Form
    {
        public event Action ConfigSaved;

        private ReminderConfig _cfg;
        private RadioButton _radioFixo;
        private RadioButton _radioUptime;
        private DateTimePicker _timePicker;
        private NumericUpDown _folga;
        private NumericUpDown _uptimeHoras;
        private NumericUpDown _snooze;
        private CheckBox _forceChk;
        private NumericUpDown _maxOk;
        private CheckBox _autostartChk;
        private CheckBox _autoUpdateChk;
        private DataGridView _grid;
        private Label _lblStats;

        public ConfigForm(ReminderConfig cfg)
        {
            _cfg = cfg;
            Text = "Aviso de Reinício - Configurações";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(680, 720);
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 12;

            // --- quando avisar (modo) ---
            GroupBox gQuando = new GroupBox();
            gQuando.Text = " Quando avisar ";
            gQuando.SetBounds(12, y, 656, 104);
            _radioFixo = new RadioButton();
            _radioFixo.Text = "Em horário fixo, todo dia";
            _radioFixo.SetBounds(14, 22, 300, 22);
            _radioFixo.Checked = (cfg.Modo != ModoReinicio.Uptime);
            Label lt = new Label();
            lt.Text = "Às:";
            lt.SetBounds(36, 48, 30, 20);
            _timePicker = new DateTimePicker();
            _timePicker.Format = DateTimePickerFormat.Time;
            _timePicker.ShowUpDown = true;
            _timePicker.Value = DateTime.Today.Add(cfg.RestartTime);
            _timePicker.SetBounds(70, 45, 80, 24);
            Label lf = new Label();
            lf.Text = "— não avisar se reiniciado há menos de";
            lf.SetBounds(160, 48, 290, 20);
            _folga = new NumericUpDown();
            _folga.Minimum = 1;
            _folga.Maximum = 48;
            _folga.Value = Math.Max(1, Math.Min(48, cfg.SatisfiedHours));
            _folga.SetBounds(455, 45, 50, 24);
            Label lf2 = new Label();
            lf2.Text = "h";
            lf2.SetBounds(510, 48, 20, 20);
            _radioUptime = new RadioButton();
            _radioUptime.Text = "Quando estiver ligado há mais de";
            _radioUptime.SetBounds(14, 74, 280, 22);
            _uptimeHoras = new NumericUpDown();
            _uptimeHoras.Minimum = 1;
            _uptimeHoras.Maximum = 96;
            _uptimeHoras.Value = Math.Max(1, Math.Min(96, cfg.UptimeHours));
            _uptimeHoras.SetBounds(300, 71, 50, 24);
            Label lu = new Label();
            lu.Text = "horas, em qualquer horário do dia (ciclo)";
            lu.SetBounds(356, 74, 290, 22);
            EventHandler atualizarModo = delegate
            {
                _timePicker.Enabled = _radioFixo.Checked;
                _folga.Enabled = _radioFixo.Checked;
                _uptimeHoras.Enabled = _radioUptime.Checked;
            };
            _radioFixo.CheckedChanged += atualizarModo;
            gQuando.Controls.Add(_radioFixo);
            gQuando.Controls.Add(lt);
            gQuando.Controls.Add(_timePicker);
            gQuando.Controls.Add(lf);
            gQuando.Controls.Add(_folga);
            gQuando.Controls.Add(lf2);
            gQuando.Controls.Add(_radioUptime);
            gQuando.Controls.Add(_uptimeHoras);
            gQuando.Controls.Add(lu);
            Controls.Add(gQuando);
            atualizarModo(null, EventArgs.Empty);
            y += 116;

            // --- adiamento ---
            GroupBox gSnooze = new GroupBox();
            gSnooze.Text = " Adiamento (botão OK) ";
            gSnooze.SetBounds(12, y, 656, 62);
            Label l2 = new Label();
            l2.Text = "Reaparecer após (minutos):";
            l2.SetBounds(14, 26, 170, 20);
            _snooze = new NumericUpDown();
            _snooze.Minimum = 1;
            _snooze.Maximum = 120;
            _snooze.Value = Math.Max(1, Math.Min(120, cfg.SnoozeMinutes));
            _snooze.SetBounds(190, 22, 60, 24);
            gSnooze.Controls.Add(l2);
            gSnooze.Controls.Add(_snooze);
            Controls.Add(gSnooze);
            y += 74;

            // --- reinicio forcado ---
            GroupBox gForce = new GroupBox();
            gForce.Text = " Reinício forçado (opcional) ";
            gForce.SetBounds(12, y, 656, 62);
            _forceChk = new CheckBox();
            _forceChk.Text = "Forçar reinício automático após";
            _forceChk.SetBounds(14, 26, 220, 22);
            _forceChk.Checked = cfg.ForceEnabled;
            _maxOk = new NumericUpDown();
            _maxOk.Minimum = 1;
            _maxOk.Maximum = 50;
            _maxOk.Value = Math.Max(1, Math.Min(50, cfg.MaxOkBeforeForce));
            _maxOk.SetBounds(240, 24, 60, 24);
            Label l3 = new Label();
            l3.Text = "adiamentos (OK) no mesmo dia";
            l3.SetBounds(310, 26, 240, 20);
            _forceChk.CheckedChanged += delegate { _maxOk.Enabled = _forceChk.Checked; };
            _maxOk.Enabled = cfg.ForceEnabled;
            gForce.Controls.Add(_forceChk);
            gForce.Controls.Add(_maxOk);
            gForce.Controls.Add(l3);
            Controls.Add(gForce);
            y += 74;

            // --- inicializacao ---
            GroupBox gAuto = new GroupBox();
            gAuto.Text = " Inicialização ";
            gAuto.SetBounds(12, y, 656, 116);
            _autostartChk = new CheckBox();
            _autostartChk.Text = "Iniciar automaticamente quando o Windows ligar (recomendado)";
            _autostartChk.SetBounds(14, 22, 600, 24);
            _autostartChk.Checked = Autostart.IsEnabled();
            _autoUpdateChk = new CheckBox();
            _autoUpdateChk.Text = "Instalar atualizações sozinho após o reinício diário (desligado = só avisar)";
            _autoUpdateChk.SetBounds(14, 48, 620, 24);
            _autoUpdateChk.Checked = cfg.AutoUpdate;
            Button btnSenha = new Button();
            btnSenha.Text = Supervisor.IsEnabled(cfg)
                ? "Trocar senha de supervisor…"
                : "Definir senha de supervisor…";
            btnSenha.SetBounds(14, 76, 240, 28);
            btnSenha.Click += OnDefinirSenha;
            gAuto.Controls.Add(_autostartChk);
            gAuto.Controls.Add(_autoUpdateChk);
            gAuto.Controls.Add(btnSenha);
            Controls.Add(gAuto);
            y += 128;

            // --- estatisticas ---
            _lblStats = new Label();
            _lblStats.SetBounds(12, y, 656, 52);
            Controls.Add(_lblStats);
            y += 56;

            // --- botoes ---
            Button btnSalvar = new Button();
            btnSalvar.Text = "Salvar configurações";
            btnSalvar.SetBounds(12, y, 150, 32);
            btnSalvar.Click += OnSave;

            Button btnPasta = new Button();
            btnPasta.Text = "Abrir pasta de dados";
            btnPasta.SetBounds(172, y, 150, 32);
            btnPasta.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + Program.AppDir + "\""); } catch { }
            };

            Button btnLimpar = new Button();
            btnLimpar.Text = "Arquivar log";
            btnLimpar.SetBounds(332, y, 110, 32);
            btnLimpar.Click += OnClearLog;

            Button btnFechar = new Button();
            btnFechar.Text = "Fechar";
            btnFechar.SetBounds(452, y, 110, 32);
            btnFechar.Click += delegate { Close(); };

            Controls.Add(btnSalvar);
            Controls.Add(btnPasta);
            Controls.Add(btnLimpar);
            Controls.Add(btnFechar);
            y += 44;

            // --- log ---
            GroupBox gLog = new GroupBox();
            gLog.Text = " Log de eventos (últimos 500) ";
            gLog.SetBounds(12, y, 656, 720 - y - 12);

            _grid = new DataGridView();
            _grid.SetBounds(8, 22, 640, 720 - y - 12 - 30);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = Color.White;

            DataGridViewTextBoxColumn c1 = new DataGridViewTextBoxColumn();
            c1.HeaderText = "Data/Hora";
            c1.Width = 120;
            c1.ReadOnly = true;
            DataGridViewTextBoxColumn c2 = new DataGridViewTextBoxColumn();
            c2.HeaderText = "Evento";
            c2.Width = 150;
            c2.ReadOnly = true;
            DataGridViewTextBoxColumn c3 = new DataGridViewTextBoxColumn();
            c3.HeaderText = "Detalhe";
            c3.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            c3.ReadOnly = true;

            _grid.Columns.Add(c1);
            _grid.Columns.Add(c2);
            _grid.Columns.Add(c3);

            gLog.Controls.Add(_grid);
            Controls.Add(gLog);

            Shown += delegate { RefreshStats(); };
        }


        private static string UpText(DateTime boot)
        {
            TimeSpan u = DateTime.Now - boot;
            if (u.TotalHours < 1) return Math.Max(1, (int)u.TotalMinutes) + " min";
            return u.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + " h";
        }

        private void RefreshStats()
        {
            List<LogEntry> entries = LogReader.Read(5000);
            try
            {
                DateTime boot = Program.LastBoot();
                int pops = 0, oks = 0, totalOks = 0;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                foreach (LogEntry e in entries)
                {
                    if (e.Evento == Eventos.AvisoExibido && e.When.ToString("yyyy-MM-dd") == today) pops++;
                    if (e.Evento == Eventos.AdiadoOk)
                    {
                        totalOks++;
                        if (e.When.ToString("yyyy-MM-dd") == today) oks++;
                    }
                }

                _lblStats.Text =
                    "Hoje: " + pops + " avisos exibidos · " + oks + " adiamentos (OK) · Total de adiamentos: " + totalOks + "\n" +
                    "Último reinício: " + Program.Fmt(boot) + " · Tempo ligado: " + UpText(boot) + "\n" +
                    "Dados: " + Program.AppDir + "   |   Desenvolvido por Scursel";
            }
            catch { }

            try
            {
                _grid.Rows.Clear();
                int start = Math.Max(0, entries.Count - 500);
                for (int i = start; i < entries.Count; i++)
                {
                    LogEntry e = entries[i];
                    _grid.Rows.Add(new object[]
                    {
                        e.When.ToString("dd/MM/yyyy HH:mm:ss"),
                        e.Evento,
                        e.Detalhe
                    });
                }
                if (_grid.Rows.Count > 0)
                    _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
            }
            catch { }
        }

        private void OnSave(object sender, EventArgs e)
        {
            _cfg.Modo = _radioUptime.Checked ? ModoReinicio.Uptime : ModoReinicio.Fixo;
            _cfg.RestartTime = _timePicker.Value.TimeOfDay;
            _cfg.SatisfiedHours = (int)_folga.Value;
            _cfg.UptimeHours = (int)_uptimeHoras.Value;
            _cfg.SnoozeMinutes = (int)_snooze.Value;
            _cfg.ForceEnabled = _forceChk.Checked;
            _cfg.MaxOkBeforeForce = (int)_maxOk.Value;
            _cfg.AutoUpdate = _autoUpdateChk.Checked;
            _cfg.Save();

            Autostart.SetEnabled(_autostartChk.Checked);

            Program.Log(Eventos.ConfiguracoesAlteradas,
                (_cfg.Modo == ModoReinicio.Uptime
                    ? "ciclo de " + _cfg.UptimeHours + " h ligado"
                    : "fixo às " + _cfg.RestartTime.ToString(@"hh\:mm") + " (folga " + _cfg.SatisfiedHours + " h)") +
                " · adiamento " + _cfg.SnoozeMinutes + " min" +
                " · forçado: " + (_cfg.ForceEnabled ? "sim" : "não") +
                " · início automático: " + (_autostartChk.Checked ? "sim" : "não") +
                " · auto-update: " + (_cfg.AutoUpdate ? "sim" : "não"));

            if (ConfigSaved != null) ConfigSaved();
            RefreshStats();
            MessageBox.Show(this, "Configurações salvas.", "Aviso de Reinício",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnClearLog(object sender, EventArgs e)
        {
            if (_cfg.ProtegerLimparLog && !PasswordPrompt.Ask(this, _cfg, "Arquivar log"))
                return;
            DialogResult r = MessageBox.Show(this,
                "Arquivar o log atual e começar um arquivo novo?", "Aviso de Reinício",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            string destName;
            try
            {
                destName = Program.ArchiveCurrentLog();
            }
            catch (Exception ex)
            {
                Program.LogErro("arquivar log: " + ex.Message);
                MessageBox.Show(this, "Não foi possível arquivar o log.", "Aviso de Reinício",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Program.Log(Eventos.LogArquivado, destName);
            RefreshStats();
        }

        private void OnDefinirSenha(object sender, EventArgs e)
        {
            bool tem = Supervisor.IsEnabled(_cfg);
            using (Form f = new Form())
            {
                f.Text = tem ? "Trocar ou remover senha" : "Definir senha de supervisor";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

                int y = 12;
                TextBox atual = null;
                if (tem)
                {
                    Label la = new Label();
                    la.Text = "Senha atual:";
                    la.SetBounds(12, y, 330, 18);
                    atual = new TextBox();
                    atual.UseSystemPasswordChar = true;
                    atual.SetBounds(12, y + 18, 330, 24);
                    f.Controls.Add(la);
                    f.Controls.Add(atual);
                    y += 48;
                }

                Label ln = new Label();
                ln.Text = tem ? "Nova senha (vazio = remover):" : "Nova senha:";
                ln.SetBounds(12, y, 330, 18);
                TextBox nova = new TextBox();
                nova.UseSystemPasswordChar = true;
                nova.SetBounds(12, y + 18, 330, 24);
                f.Controls.Add(ln);
                f.Controls.Add(nova);
                y += 48;

                Label lc = new Label();
                lc.Text = "Confirmar nova senha:";
                lc.SetBounds(12, y, 330, 18);
                TextBox conf = new TextBox();
                conf.UseSystemPasswordChar = true;
                conf.SetBounds(12, y + 18, 330, 24);
                f.Controls.Add(lc);
                f.Controls.Add(conf);
                y += 50;

                Button ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(176, y, 80, 28);
                Button cancel = new Button();
                cancel.Text = "Cancelar";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(262, y, 80, 28);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.ClientSize = new Size(360, y + 44);

                if (f.ShowDialog(this) != DialogResult.OK) return;

                if (tem && !Supervisor.Verify(_cfg, atual.Text))
                {
                    Program.Log(Eventos.SenhaIncorreta, "trocar senha");
                    MessageBox.Show(this, "Senha atual incorreta.", "Aviso de Reinício",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (nova.Text.Length == 0)
                {
                    if (!tem)
                    {
                        MessageBox.Show(this, "Informe uma senha.", "Aviso de Reinício",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Supervisor.ClearPassword(_cfg);
                    _cfg.Save();
                    if (ConfigSaved != null) ConfigSaved();
                    MessageBox.Show(this, "Senha removida. A proteção ficou desligada.", "Aviso de Reinício",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (nova.Text != conf.Text)
                {
                    MessageBox.Show(this, "A confirmação não confere.", "Aviso de Reinício",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Supervisor.SetPassword(_cfg, nova.Text);
                _cfg.Save();
                if (ConfigSaved != null) ConfigSaved();
                MessageBox.Show(this, "Senha de supervisor definida.", "Aviso de Reinício",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}