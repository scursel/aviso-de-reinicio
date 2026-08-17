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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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

        [STAThread]
        private static void Main(string[] args)
        {
            // Modo de autoteste (usado na construcao): grava um log e sai.
            if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                InitPaths();
                ReminderConfig cfg = ReminderConfig.Load();
                Log("Teste automático", "selftest ok");
                Environment.Exit(0);
            }

            // Uma unica instancia por usuario.
            bool createdNew;
            using (Mutex mtx = new Mutex(true, @"Local\AvisoDeReinicio_SingleInstance", out createdNew))
            {
                if (!createdNew) return;

                InitPaths();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }

        public static void InitPaths()
        {
            AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvisoDeReinicio");
            Directory.CreateDirectory(AppDir);
            ConfigPath = Path.Combine(AppDir, "config.ini");
            LogPath = Path.Combine(AppDir, "log.csv");
            FlagPath = Path.Combine(AppDir, "reinicio_pendente.flag");
            LastBootPath = Path.Combine(AppDir, "ultimo_boot.txt");
        }

        // Momento do ultimo boot (relogio - tempo ligado). GetTickCount64
        // inclui o tempo em suspensao/hibernacao, entao o calculo e confiavel.
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

    // -------------------- eventos do log (nomes simples) ---------------------
    public static class Eventos
    {
        public const string AvisoExibido = "Aviso exibido";
        public const string AdiadoOk = "Adiado (OK)";
        public const string AdiadoAutomatico = "Adiado (automático)";
        public const string ReinicioSolicitado = "Reinício solicitado";
        public const string FalhaAoReiniciar = "Falha ao reiniciar";
        public const string ComputadorReiniciado = "Computador reiniciado";
        public const string ContagemRegressiva = "Contagem regressiva";
        public const string ConfiguracoesAlteradas = "Configurações alteradas";
        public const string AvisosDesativados = "Avisos desativados";
        public const string LogLimpo = "Log limpo";
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
                string[] lines = File.ReadAllLines(Program.LogPath);
                int start = Math.Max(0, lines.Length - maxLines);
                for (int i = start; i < lines.Length; i++)
                {
                    string l = (lines[i] == null ? "" : lines[i]).Trim();
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
        public int SnoozeMinutes = 5;                          // pop-up volta apos X min
        public bool ForceEnabled = false;                      // forca reinicio automatico
        public int MaxOkBeforeForce = 10;                      // apos X "OK" no mesmo dia
        public int PopupTimeoutMinutes = 15;                   // sem clique, adia sozinho

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
                sb.AppendLine("SnoozeMinutes=" + SnoozeMinutes);
                sb.AppendLine("ForceEnabled=" + (ForceEnabled ? "1" : "0"));
                sb.AppendLine("MaxOkBeforeForce=" + MaxOkBeforeForce);
                sb.AppendLine("PopupTimeoutMinutes=" + PopupTimeoutMinutes);
                File.WriteAllText(Program.ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
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
    }    // ------------------------- app da bandeja (nucleo) -----------------------
    public class TrayApp : ApplicationContext
    {
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _timer;
        private ReminderConfig _cfg;
        private DateTime _nextPopup;
        private DateTime _lastDay;
        private Form _openForm;              // pop-up/countdown/config aberto
        private ConfigForm _configForm;      // uma unica tela de configuracoes
        private bool _forceDemo;
        private bool _disabled;

        public TrayApp()
        {
            _cfg = ReminderConfig.Load();

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
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Sair", OnExit);
                _tray.ContextMenu = menu;
                _tray.DoubleClick += delegate { ShowConfig(); };
            }
            catch (Exception ex)
            {
                Program.LogErro("bandeja: " + ex.Message);
            }

            RecomputeNextPopup();
            _lastDay = DateTime.Today;
            if (Environment.GetCommandLineArgs().Length > 1 &&
                string.Equals(Environment.GetCommandLineArgs()[1], "--demo", StringComparison.OrdinalIgnoreCase))
            {
                _forceDemo = true;
            }

            // Maquina isenta? Arquivo "DesativarAviso.txt" na pasta de dados
            // desliga os pop-ups sem desinstalar o programa.
            _disabled = File.Exists(Path.Combine(Program.AppDir, "DesativarAviso.txt"));
            // Flag de desenvolvimento: abre direto a tela de configuracoes
            if (Environment.GetCommandLineArgs().Length > 1 &&
                string.Equals(Environment.GetCommandLineArgs()[1], "--config", StringComparison.OrdinalIgnoreCase))
            {
                ShowConfig();
            }
            if (_disabled) Program.Log(Eventos.AvisosDesativados, "arquivo DesativarAviso.txt presente");

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15000;                 // checa a cada 15 s
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // --------------------------- logica do lembrete -----------------------
        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (_openForm != null) return;       // ja tem aviso na tela
                if (_disabled) return;               // maquina isenta
                // flag de teste/desenvolvimento: dispara o pop-up na 1a checagem
                if (_forceDemo)
                {
                    _forceDemo = false;
                    ShowPopup(true);
                    return;
                }

                if (DateTime.Today != _lastDay)
                {
                    _lastDay = DateTime.Today;
                    RecomputeNextPopup();
                }

                if (DateTime.Now >= _nextPopup && !SatisfiedToday())
                {
                    int oks = LogReader.CountToday(Eventos.AdiadoOk);
                    if (_cfg.ForceEnabled && oks >= _cfg.MaxOkBeforeForce)
                        ShowCountdown();
                    else
                        ShowPopup(false);
                }
            }
            catch (Exception ex)
            {
                Program.LogErro("tick: " + ex.Message);
            }
        }

        // Considera "ja reiniciado hoje" se o ultimo boot aconteceu a partir de
        // 2 horas antes do horario preferencial (aceita quem reiniciou um pouco
        // antes da hora marcada).
        private bool SatisfiedToday()
        {
            DateTime limite = DateTime.Today.Add(_cfg.RestartTime).Subtract(TimeSpan.FromHours(2));
            return Program.LastBoot() >= limite;
        }

        private void RecomputeNextPopup()
        {
            DateTime sched = DateTime.Today.Add(_cfg.RestartTime);
            if (DateTime.Now >= sched && !SatisfiedToday())
                _nextPopup = DateTime.Now.AddSeconds(45);   // PC ligou depois da hora: avisa em seguida
            else
                _nextPopup = sched;
        }

        private void ShowPopup(bool isTest)
        {
            if (_openForm != null) return;
            PopupForm f = new PopupForm(_cfg);
            f.RestartRequested += DoRestartManual;
            f.SnoozeRequested += OnSnooze;
            f.AutoSnoozeRequested += OnAutoSnooze;
            _openForm = f;
            f.FormClosed += delegate { _openForm = null; };
            string detPopup = isTest
                ? "aviso de teste"
                : (LogReader.CountToday(Eventos.AvisoExibido) + 1) + "º aviso do dia";
            Program.Log(Eventos.AvisoExibido, detPopup);
            f.Show();
            f.Activate();
        }

        private void ShowCountdown()
        {
            if (_openForm != null) return;
            CountdownForm f = new CountdownForm(_cfg);
            f.RestartRequested += DoRestartForced;
            f.SnoozeRequested += OnSnooze;
            _openForm = f;
            f.FormClosed += delegate { _openForm = null; };
            Program.Log(Eventos.ContagemRegressiva, "reinício automático em 60 s");
            f.Show();
            f.Activate();
        }

        private void DoRestartManual() { DoRestart(false); }
        private void DoRestartForced(bool forced) { DoRestart(forced); }

        private int ScheduleNextPopup()
        {
            int sn = Math.Max(1, _cfg.SnoozeMinutes);
            _nextPopup = DateTime.Now.AddMinutes(sn);
            return sn;
        }

        private void OnSnooze()
        {
            int sn = ScheduleNextPopup();
            Program.Log(Eventos.AdiadoOk, "próximo aviso em " + sn + " min");
        }

        private void OnAutoSnooze()
        {
            ScheduleNextPopup();
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
                _nextPopup = DateTime.Now.AddMinutes(3);
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
        private void OnOpenConfig(object sender, EventArgs e) { ShowConfig(); }

        private void OnTestPopup(object sender, EventArgs e) { ShowPopup(true); }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try { Process.Start("explorer.exe", "\"" + Program.AppDir + "\""); }
            catch { }
        }

        private void OnExit(object sender, EventArgs e)
        {
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
            RecomputeNextPopup();
        }
    }    // ------------------------- pop-up de reinicio ----------------------------
    public class PopupForm : Form
    {
        public event Action RestartRequested;
        public event Action SnoozeRequested;
        public event Action AutoSnoozeRequested;
        private bool _done;
        private System.Windows.Forms.Timer _watch;
        private DateTime _shownAt;
        private int _timeoutMin;
        private int _snoozeMin;

        public PopupForm(ReminderConfig cfg)
        {
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
                "Horário preferencial configurado: " + cfg.RestartTime.ToString(@"hh\:mm");
            corpo.Font = new Font("Segoe UI", 10F);
            corpo.SetBounds(16, 48, 450, 100);

            Label adiamentos = new Label();
            adiamentos.Text = "Você já adiou " + LogReader.CountToday(Eventos.AdiadoOk) + " vez(es) hoje.";
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
            btnReiniciar.Click += delegate { Finish(true); };

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
                Activate();
                BringToFront();
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
            try
            {
                TopMost = true;
                Activate();
                BringToFront();
            }
            catch { }

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
            else
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
            Program.Log(Eventos.AdiadoAutomatico,
                "sem interação por " + _timeoutMin + " min · próximo aviso em " + _snoozeMin + " min");
            if (AutoSnoozeRequested != null) AutoSnoozeRequested();
            Close();
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
                Activate();
                BringToFront();
            };

            _t = new System.Windows.Forms.Timer();
            _t.Interval = 1000;
            _t.Tick += delegate
            {
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

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }
    }    // ------------------------- tela de configuracao --------------------------
    public class ConfigForm : Form
    {
        public event Action ConfigSaved;

        private ReminderConfig _cfg;
        private DateTimePicker _timePicker;
        private NumericUpDown _snooze;
        private CheckBox _forceChk;
        private NumericUpDown _maxOk;
        private CheckBox _autostartChk;
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
            ClientSize = new Size(680, 640);
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 12;

            // --- horario preferencial ---
            GroupBox gHorario = new GroupBox();
            gHorario.Text = " Horário preferencial de reinício ";
            gHorario.SetBounds(12, y, 320, 62);
            Label l1 = new Label();
            l1.Text = "Avisar diariamente às:";
            l1.SetBounds(14, 26, 140, 20);
            _timePicker = new DateTimePicker();
            _timePicker.Format = DateTimePickerFormat.Time;
            _timePicker.ShowUpDown = true;
            _timePicker.Value = DateTime.Today.Add(cfg.RestartTime);
            _timePicker.SetBounds(160, 22, 90, 24);
            gHorario.Controls.Add(l1);
            gHorario.Controls.Add(_timePicker);
            Controls.Add(gHorario);

            // --- adiamento ---
            GroupBox gSnooze = new GroupBox();
            gSnooze.Text = " Adiamento (botão OK) ";
            gSnooze.SetBounds(348, y, 320, 62);
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
            gAuto.SetBounds(12, y, 656, 56);
            _autostartChk = new CheckBox();
            _autostartChk.Text = "Iniciar automaticamente quando o Windows ligar (recomendado)";
            _autostartChk.SetBounds(14, 22, 600, 24);
            _autostartChk.Checked = Autostart.IsEnabled();
            gAuto.Controls.Add(_autostartChk);
            Controls.Add(gAuto);
            y += 68;

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
            btnLimpar.Text = "Limpar log";
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
            gLog.SetBounds(12, y, 656, 640 - y - 12);

            _grid = new DataGridView();
            _grid.SetBounds(8, 22, 640, 640 - y - 12 - 30);
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
            try
            {
                DateTime boot = Program.LastBoot();
                int pops = LogReader.CountToday(Eventos.AvisoExibido);
                int oks = LogReader.CountToday(Eventos.AdiadoOk);
                int totalOks = LogReader.CountAll(Eventos.AdiadoOk);

                _lblStats.Text =
                    "Hoje: " + pops + " avisos exibidos · " + oks + " adiamentos (OK) · Total de adiamentos: " + totalOks + "\n" +
                    "Último reinício: " + Program.Fmt(boot) + " · Tempo ligado: " + UpText(boot) + "\n" +
                    "Dados: " + Program.AppDir + "   |   Desenvolvido por Scursel";
            }
            catch { }

            try
            {
                _grid.Rows.Clear();
                foreach (LogEntry e in LogReader.Read(500))
                {
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
            _cfg.RestartTime = _timePicker.Value.TimeOfDay;
            _cfg.SnoozeMinutes = (int)_snooze.Value;
            _cfg.ForceEnabled = _forceChk.Checked;
            _cfg.MaxOkBeforeForce = (int)_maxOk.Value;
            _cfg.Save();

            Autostart.SetEnabled(_autostartChk.Checked);

            Program.Log(Eventos.ConfiguracoesAlteradas,
                "horário " + _cfg.RestartTime.ToString(@"hh\:mm") +
                " · adiamento " + _cfg.SnoozeMinutes + " min" +
                " · forçado: " + (_cfg.ForceEnabled ? "sim" : "não") +
                " · início automático: " + (_autostartChk.Checked ? "sim" : "não"));

            if (ConfigSaved != null) ConfigSaved();
            RefreshStats();
            MessageBox.Show(this, "Configurações salvas.", "Aviso de Reinício",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnClearLog(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(this,
                "Apagar todo o histórico do log?", "Aviso de Reinício",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            try { File.WriteAllText(Program.LogPath, ""); } catch { }
            Program.Log(Eventos.LogLimpo, "");
            RefreshStats();
        }
    }
}