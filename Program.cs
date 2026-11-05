using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// FastPrtSc：双击即截屏，无 GUI，截图完成后立即退出。
// 保存位置：exe 所在目录，文件名带时间戳。

// 声明为 DPI 感知，拿到真实物理分辨率（否则 125% 缩放下只截到 2048x1152）
Native.SetProcessDPIAware();

try
{
    // 主屏幕尺寸
    Screen? primary = Screen.PrimaryScreen;
    int width = primary?.Bounds.Width ?? 0;
    int height = primary?.Bounds.Height ?? 0;

    // 立刻截图
    if (width == 0 || height == 0)
    {
        Console.Error.WriteLine("找不到主屏幕。");
        Environment.ExitCode = 1;
        return;
    }

    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.CopyFromScreen(primary!.Bounds.Location, Point.Empty,
            new Size(width, height), CopyPixelOperation.SourceCopy);
    }

    // 读取 config.json（随程序一起发布，可修改保存路径和文件名前缀）
    var (dir, prefix) = LoadConfig(AppContext.BaseDirectory);
    if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

    string path = Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
    bitmap.Save(path, ImageFormat.Png);

    // 打印路径（控制台窗口会一闪而过，仅当从命令行运行时可见）
    Console.WriteLine(path);

    // 右下角弹出自绘通知窗口（不依赖系统通知设置，必现），淡出后退出
    ShowNotification(path);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"截图失败: {ex.Message}");
    Environment.ExitCode = 1;
}

// 程序到此自然退出

// 在屏幕右下角自绘一个系统通知样式的窗口：深色圆角卡片，
// 显示 1 秒后淡出，然后进程退出。不依赖 Windows 通知系统。
static void ShowNotification(string path)
{
    var screen = Screen.PrimaryScreen!.Bounds;
    const int w = 380, h = 100, margin = 20;

    var form = new System.Windows.Forms.Form
    {
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
        StartPosition = System.Windows.Forms.FormStartPosition.Manual,
        TopMost = true,
        ShowInTaskbar = false,
        BackColor = System.Drawing.Color.FromArgb(36, 36, 36),
        Size = new System.Drawing.Size(w, h),
        Location = new System.Drawing.Point(screen.Right - w - margin, screen.Bottom - h - margin),
    };

    // 圆角
    const int r = 12;
    var gp = new GraphicsPath();
    gp.AddArc(0, 0, r, r, 180, 90);
    gp.AddArc(w - r, 0, r, r, 270, 90);
    gp.AddArc(w - r, h - r, r, r, 0, 90);
    gp.AddArc(0, h - r, r, r, 90, 90);
    gp.CloseFigure();
    form.Region = new Region(gp);

    var title = new Label
    {
        Text = "截图完成",
        Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        AutoSize = true,
        Location = new Point(16, 16),
    };
    var text = new Label
    {
        Text = $"已保存到 {path}",
        Font = new Font("Segoe UI", 9F),
        ForeColor = System.Drawing.Color.FromArgb(200, 200, 200),
        BackColor = Color.Transparent,
        AutoSize = false,
        Size = new Size(w - 32, 22),
        Location = new Point(16, 54),
        AutoEllipsis = true,
    };
    form.Controls.Add(title);
    form.Controls.Add(text);

    // 1 秒后开始淡出
    var shownAt = DateTime.Now;
    var fade = new System.Windows.Forms.Timer { Interval = 50 };
    fade.Tick += (_, _) =>
    {
        if (DateTime.Now - shownAt < TimeSpan.FromSeconds(1)) return;
        form.Opacity -= 0.05;
        if (form.Opacity <= 0) form.Close();
    };
    fade.Start();

    form.Show();
    Application.Run(form);
}

// 读取 exe 同目录的 config.json；文件缺失或损坏时回退到默认值。
// SavePath 为空 = exe 所在目录；相对路径基于 exe 目录解析；支持 ~ 表示用户主目录。
static (string Dir, string Prefix) LoadConfig(string baseDir)
{
    string dir = baseDir;
    string prefix = "Screen";
    try
    {
        string cfgPath = Path.Combine(baseDir, "config.json");
        if (File.Exists(cfgPath))
        {
            string raw = File.ReadAllText(cfgPath);
            Config? cfg = null;
            try
            {
                cfg = System.Text.Json.JsonSerializer.Deserialize<Config>(raw);
            }
            catch (System.Text.Json.JsonException)
            {
                // 宽容处理：允许路径里写单反斜杠（如 D:\AAA），自动补成合法 JSON 再解析
                string fixedRaw = System.Text.RegularExpressions.Regex.Replace(
                    raw, @"\\(?![\\/bfnur""])", "\\\\");
                cfg = System.Text.Json.JsonSerializer.Deserialize<Config>(fixedRaw);
            }
            if (cfg is not null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.FileNamePrefix))
                    prefix = cfg.FileNamePrefix.Trim();
                if (!string.IsNullOrWhiteSpace(cfg.SavePath))
                {
                    string sp = cfg.SavePath.Trim().Trim('"');
                    if (sp.StartsWith("~"))
                        sp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            sp[1..].TrimStart('\\', '/'));
                    if (!Path.IsPathRooted(sp))
                        sp = Path.Combine(baseDir, sp);
                    dir = sp;
                }
            }
        }
    }
    catch
    {
        // 配置解析失败时静默使用默认值
    }
    return (dir, prefix);
}

internal sealed class Config
{
    public string? SavePath { get; set; }
    public string? FileNamePrefix { get; set; }
}

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDPIAware();
}
