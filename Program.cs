using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace AutoSkillChiQuoc;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class SkillConfig
{
    public string WindowTitle { get; set; } = "Chiến Quốc";
    public string[] Keys { get; set; } = ["1", "2", "3"];
    public int[] Delays { get; set; } = [500, 500, 500];
}

public sealed class MainForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    private readonly TextBox titleBox = new() { Width = 210 };
    private readonly ComboBox[] keys = new ComboBox[3];
    private readonly NumericUpDown[] delays = new NumericUpDown[3];
    private readonly Label status = new() { AutoSize = true, Text = "Đang dừng" };
    private readonly System.Windows.Forms.Timer hotkeyTimer = new() { Interval = 80 };
    private CancellationTokenSource? cts;
    private bool f6Held, f7Held;
    private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoSkillChiQuoc", "config.json");

    public MainForm()
    {
        Text = "Auto Skill 3 - Chiến Quốc 1.01.92";
        Width = 420; Height = 355; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7, Padding = new Padding(16) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "Tên cửa sổ:", AutoSize = true }, 0, 0);
        panel.Controls.Add(titleBox, 1, 0); panel.SetColumnSpan(titleBox, 2);
        panel.Controls.Add(new Label { Text = "Kỹ năng", AutoSize = true }, 0, 1);
        panel.Controls.Add(new Label { Text = "Phím", AutoSize = true }, 1, 1);
        panel.Controls.Add(new Label { Text = "Chờ (ms)", AutoSize = true }, 2, 1);
        string[] choices = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "F1", "F2", "F3", "F4", "F5", "Q", "W", "E", "R", "A", "S", "D"];
        for (int i = 0; i < 3; i++)
        {
            keys[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
            keys[i].Items.AddRange(choices); keys[i].SelectedItem = (i + 1).ToString();
            delays[i] = new NumericUpDown { Minimum = 50, Maximum = 60000, Increment = 50, Value = 500, Width = 110 };
            panel.Controls.Add(new Label { Text = $"Skill {i + 1}", AutoSize = true }, 0, i + 2);
            panel.Controls.Add(keys[i], 1, i + 2);
            panel.Controls.Add(delays[i], 2, i + 2);
        }
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var start = new Button { Text = "Bắt đầu F6", AutoSize = true };
        var stop = new Button { Text = "Dừng F7", AutoSize = true };
        var save = new Button { Text = "Lưu", AutoSize = true };
        start.Click += (_, _) => StartAuto(); stop.Click += (_, _) => StopAuto(); save.Click += (_, _) => SaveConfig();
        buttons.Controls.AddRange([start, stop, save]); panel.Controls.Add(buttons, 0, 5); panel.SetColumnSpan(buttons, 3);
        panel.Controls.Add(status, 0, 6); panel.SetColumnSpan(status, 3);
        LoadConfig();
        hotkeyTimer.Tick += (_, _) =>
        {
            bool f6 = (GetAsyncKeyState((int)Keys.F6) & 0x8000) != 0;
            bool f7 = (GetAsyncKeyState((int)Keys.F7) & 0x8000) != 0;
            if (f6 && !f6Held) StartAuto();
            if (f7 && !f7Held) StopAuto();
            f6Held = f6; f7Held = f7;
        };
        hotkeyTimer.Start();
        FormClosing += (_, _) => { StopAuto(); hotkeyTimer.Stop(); };
    }

    private static string ForegroundTitle()
    {
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(GetForegroundWindow(), sb, sb.Capacity);
        return sb.ToString();
    }
    private static void PressKey(Keys key)
    {
        ushort code = (ushort)key;
        var inputs = new[] {
            new INPUT { type = INPUT_KEYBOARD, data = new InputUnion { ki = new KEYBDINPUT { wVk = code } } },
            new INPUT { type = INPUT_KEYBOARD, data = new InputUnion { ki = new KEYBDINPUT { wVk = code, dwFlags = KEYEVENTF_KEYUP } } }
        };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
    private void StartAuto()
    {
        if (cts != null) return;
        string title = titleBox.Text.Trim();
        if (title.Length == 0) { MessageBox.Show("Nhập một phần tên cửa sổ game."); return; }
        var chosen = keys.Select(x => { string k = x.SelectedItem!.ToString()!; return k.Length == 1 && char.IsDigit(k[0]) ? Keys.D0 + (k[0] - '0') : Enum.Parse<Keys>(k, true); }).ToArray();
        var wait = delays.Select(x => (int)x.Value).ToArray();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        status.Text = "Đã bật - chuyển sang cửa sổ game để sử dụng";
        _ = RunAsync(title, chosen, wait, token);
    }
    private async Task RunAsync(string title, Keys[] chosen, int[] wait, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (token.IsCancellationRequested) break;
                    if (ForegroundTitle().Contains(title, StringComparison.OrdinalIgnoreCase))
                        PressKey(chosen[i]);
                    await Task.Delay(wait[i], token);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { /* CancellationTokenSource is released by StopAuto. */ }
    }
    private void StopAuto()
    {
        var previous = cts;
        cts = null;
        previous?.Cancel();
        // Do not dispose immediately: the running async loop may still observe its token.
        status.Text = "Đang dừng";
    }
    private void SaveConfig()
    {
        try
        {
            var config = new SkillConfig { WindowTitle = titleBox.Text, Keys = keys.Select(x => x.Text).ToArray(), Delays = delays.Select(x => (int)x.Value).ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "Đã lưu cấu hình";
        }
        catch (Exception ex) { MessageBox.Show("Không lưu được: " + ex.Message); }
    }
    private void LoadConfig()
    {
        titleBox.Text = "Chiến Quốc";
        try
        {
            if (!File.Exists(configPath)) return;
            var config = JsonSerializer.Deserialize<SkillConfig>(File.ReadAllText(configPath));
            if (config == null) return;
            titleBox.Text = config.WindowTitle;
            for (int i = 0; i < 3; i++)
            {
                if (i < config.Keys.Length && keys[i].Items.Contains(config.Keys[i])) keys[i].SelectedItem = config.Keys[i];
                if (i < config.Delays.Length) delays[i].Value = Math.Clamp(config.Delays[i], 50, 60000);
            }
        }
        catch { status.Text = "Cấu hình cũ không hợp lệ, dùng mặc định"; }
    }
}
