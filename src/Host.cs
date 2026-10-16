// Markdown 编辑器 —— Windows 桌面宿主
// .NET Framework 4.8.1 + WinForms + WebView2，用 in-box csc.exe 编译为单文件 exe。
// 编译时禁用 C# 5 之后的语法（老 csc 编译器限制）。

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    internal static string AppDir;
    internal static string UiDir;

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        try
        {
            Bootstrap();
        }
        catch (Exception ex)
        {
            MessageBox.Show("初始化失败：\r\n\r\n" + ex.Message, "Markdown 编辑器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        RunApp(args);
    }

    // 单独一层调用，确保 AssemblyResolve 已经挂上，再触发对 WebView2 类型的加载
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：\r\n\r\n" + ex, "Markdown 编辑器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 释放内嵌资源，并预加载原生 WebView2Loader.dll
    private static void Bootstrap()
    {
        AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarkdownEditor");
        UiDir = Path.Combine(AppDir, "ui");
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(UiDir);

        WriteResource("index.html", Path.Combine(UiDir, "index.html"));

        string loader = Path.Combine(AppDir, "WebView2Loader.dll");
        WriteResource("WebView2Loader.dll", loader);

        SetDllDirectory(AppDir);
        LoadLibrary(loader);
    }

    private static void WriteResource(string name, string path)
    {
        byte[] data = ReadResource(name);
        if (data == null) throw new FileNotFoundException("缺少内嵌资源：" + name);
        File.WriteAllBytes(path, data);
    }

    private static byte[] ReadResource(string name)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using (Stream s = asm.GetManifestResourceStream(name))
        {
            if (s == null) return null;
            byte[] buf = new byte[s.Length];
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) break;
                off += n;
            }
            return buf;
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs e)
    {
        string simple = new AssemblyName(e.Name).Name;
        if (simple == "Microsoft.Web.WebView2.Core" || simple == "Microsoft.Web.WebView2.WinForms")
        {
            byte[] data = ReadResource(simple + ".dll");
            if (data != null) return Assembly.Load(data);
        }
        return null;
    }
}

internal sealed class MainForm : Form
{
    private readonly WebView2 web;
    private string currentPath;
    private readonly string startupPath;
    private bool ready;

    internal MainForm(string[] args)
    {
        Text = "Markdown 编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(1260, 840);
        BackColor = Color.FromArgb(30, 32, 38);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { }

        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;
                if (a.Length > 1 && a[0] == '-') continue;
                startupPath = a;
                break;
            }
        }

        web = new WebView2();
        web.Dock = DockStyle.Fill;
        Controls.Add(web);
        Load += OnLoad;
        FormClosing += OnFormClosing;
    }

    private async void OnLoad(object sender, EventArgs e)
    {
        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.AppDir, "eb"), null);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "无法启动 WebView2 内核：\r\n\r\n" + ex.Message +
                "\r\n\r\n请先安装「Microsoft Edge WebView2 Runtime」后重试。",
                "Markdown 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        CoreWebView2 core = web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            "md.local", Program.UiDir, CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += OnNewWindow;
        ready = true;
        core.Navigate("https://md.local/index.html");
    }

    private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(url); }
        catch { }
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        try { if (web != null) web.Dispose(); }
        catch { }
    }

    /* =======================================================================
     *  JS ↔ C# 消息协议
     * ===================================================================== */

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.WebMessageAsJson;
        if (string.IsNullOrEmpty(json)) return;

        string type = Json.Get(json, "type");
        string id = Json.Get(json, "id");
        if (type == null) return;

        switch (type)
        {
            case "init": HandleInit(id); break;
            case "open": HandleOpen(id); break;
            case "save": HandleSave(id, json); break;
            case "associate": HandleAssociate(id); break;
            case "openWithDialog": OpenWithDialog(); break;
            case "title":
                {
                    string t = Json.Get(json, "title");
                    if (!string.IsNullOrEmpty(t)) Text = t;
                }
                break;
            case "openUrl": OpenExternal(Json.Get(json, "url")); break;
        }
    }

    private void Post(string json)
    {
        if (!ready) return;
        try { web.CoreWebView2.PostWebMessageAsJson(json); }
        catch { }
    }

    private void Reply(string id, string body)
    {
        string prefix = string.IsNullOrEmpty(id) ? "{" : "{\"id\":" + Json.Str(id) + ",";
        Post(prefix + body + "}");
    }

    private void HandleInit(string id)
    {
        string path = null;
        string name = null;
        string text = null;

        if (!string.IsNullOrEmpty(startupPath) && File.Exists(startupPath) && IsMarkdownFile(startupPath))
        {
            path = Path.GetFullPath(startupPath);
            name = Path.GetFileName(path);
            text = ReadTextFile(path);
            currentPath = path;
        }

        Reply(id, "\"type\":\"init\",\"path\":" + Json.Str(path) +
                  ",\"name\":" + Json.Str(name) + ",\"text\":" + Json.Str(text));
    }

    private void HandleOpen(string id)
    {
        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "打开 Markdown 文档";
            dlg.Filter = "Markdown 文档 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            dlg.Multiselect = false;
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                Reply(id, "\"type\":\"cancel\"");
                return;
            }

            try
            {
                currentPath = Path.GetFullPath(dlg.FileName);
                string text = ReadTextFile(currentPath);
                Reply(id, "\"type\":\"opened\",\"path\":" + Json.Str(currentPath) +
                          ",\"name\":" + Json.Str(Path.GetFileName(currentPath)) +
                          ",\"text\":" + Json.Str(text));
            }
            catch (Exception ex)
            {
                Reply(id, "\"type\":\"error\",\"message\":" + Json.Str("打开失败：" + ex.Message));
            }
        }
    }

    private void HandleSave(string id, string json)
    {
        string text = Json.Get(json, "text");
        string path = Json.Get(json, "path");
        bool forceDialog = Json.Get(json, "forceDialog") == "true";

        if (string.IsNullOrEmpty(path) || forceDialog)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "保存 Markdown 文档";
                dlg.Filter = "Markdown 文档 (*.md)|*.md|Markdown (*.markdown)|*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                dlg.DefaultExt = "md";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.FileName = string.IsNullOrEmpty(currentPath) ? "未命名.md" : Path.GetFileName(currentPath);
                if (!string.IsNullOrEmpty(currentPath)) dlg.InitialDirectory = Path.GetDirectoryName(currentPath);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    Reply(id, "\"type\":\"cancel\"");
                    return;
                }
                path = dlg.FileName;
            }
        }

        try
        {
            currentPath = Path.GetFullPath(path);
            File.WriteAllText(currentPath, text == null ? "" : text, new UTF8Encoding(false));
            Reply(id, "\"type\":\"saved\",\"path\":" + Json.Str(currentPath) +
                      ",\"name\":" + Json.Str(Path.GetFileName(currentPath)));
        }
        catch (Exception ex)
        {
            Reply(id, "\"type\":\"error\",\"message\":" + Json.Str("保存失败：" + ex.Message));
        }
    }

    private void HandleAssociate(string id)
    {
        string exe = Application.ExecutablePath;
        try
        {
            RegisterAssociation(exe);
            OpenWithDialog();
            Reply(id, "\"type\":\"associated\",\"ok\":true,\"exe\":" + Json.Str(exe));
        }
        catch (Exception ex)
        {
            Reply(id, "\"type\":\"error\",\"message\":" + Json.Str("注册打开方式失败：" + ex.Message));
        }
    }

    /* =======================================================================
     *  文件关联（HKCU，无需管理员）
     * ===================================================================== */

    private const string ProgId = "MarkdownEditor.md";

    private static void RegisterAssociation(string exe)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            k.SetValue(null, "Markdown 文档");
            using (RegistryKey ic = k.CreateSubKey("DefaultIcon")) ic.SetValue(null, exe + ",0");
            using (RegistryKey cmd = k.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(null, "\"" + exe + "\" \"%1\"");
        }

        string[] exts = new string[] { ".md", ".markdown" };
        for (int i = 0; i < exts.Length; i++)
        {
            using (RegistryKey k1 = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + exts[i] + @"\OpenWithProgids"))
                k1.SetValue(ProgId, new byte[0], RegistryValueKind.None);
            using (RegistryKey k2 = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + exts[i] + @"\OpenWithProgids"))
                k2.SetValue(ProgId, new byte[0], RegistryValueKind.None);
        }

        using (RegistryKey k3 = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\Applications\" + Path.GetFileName(exe)))
        {
            k3.SetValue("FriendlyAppName", "Markdown 编辑器");
            using (RegistryKey cmd = k3.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(null, "\"" + exe + "\" \"%1\"");
        }
    }

    private void OpenWithDialog()
    {
        try
        {
            string sample = Path.Combine(Program.AppDir, "示例文档.md");
            if (!File.Exists(sample))
            {
                File.WriteAllText(sample,
                    "# 示例文档\r\n\r\n这是用于设置「打开方式」的示例文件。\r\n\r\n选好之后双击任意 .md 就会用本程序打开。\r\n",
                    new UTF8Encoding(false));
            }
            Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL \"" + sample + "\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法打开「打开方式」窗口：\r\n\r\n" + ex.Message,
                "Markdown 编辑器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsMarkdownFile(string p)
    {
        string e = Path.GetExtension(p);
        if (string.IsNullOrEmpty(e)) return false;
        e = e.ToLowerInvariant();
        return e == ".md" || e == ".markdown" || e == ".txt";
    }

    private static string ReadTextFile(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        return Encoding.UTF8.GetString(b);
    }
}

// 极简 JSON 读写（消息都是扁平对象，够用且无需额外依赖）
internal static class Json
{
    public static string Str(string s)
    {
        if (s == null) return "null";
        return "\"" + Escape(s) + "\"";
    }

    public static string Escape(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.Length + 16);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ' || c == '\u2028' || c == '\u2029')
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // 读取一个顶层字段（字符串返回解码后的内容，布尔/数字返回原始字面量）
    public static string Get(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string pat = "\"" + key + "\"";
        int i = 0;
        while (true)
        {
            i = json.IndexOf(pat, i, StringComparison.Ordinal);
            if (i < 0) return null;

            int before = i - 1;
            while (before >= 0 && char.IsWhiteSpace(json[before])) before--;

            int j = i + pat.Length;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

            if (before >= 0 && (json[before] == '{' || json[before] == ',') &&
                j < json.Length && json[j] == ':')
            {
                j++;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j >= json.Length) return null;
                if (json[j] == '"') return ReadString(json, j);
                if (json[j] == 'n' && j + 3 < json.Length && json.Substring(j, 4) == "null") return null;
                int st = j;
                while (j < json.Length && json[j] != ',' && json[j] != '}' && json[j] != ']') j++;
                return json.Substring(st, j - st).Trim();
            }
            i = i + pat.Length;
        }
    }

    private static string ReadString(string json, int quoteIndex)
    {
        StringBuilder sb = new StringBuilder();
        int i = quoteIndex + 1;
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '\\')
            {
                i++;
                if (i >= json.Length) break;
                char d = json[i];
                switch (d)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < json.Length)
                        {
                            try { sb.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16)); }
                            catch { }
                            i += 4;
                        }
                        break;
                    default: sb.Append(d); break;
                }
                i++;
                continue;
            }
            if (c == '"') break;
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
