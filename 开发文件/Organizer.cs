using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading.Tasks;

static class Program {
    public static string Home = AppDomain.CurrentDomain.BaseDirectory;
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--self-test")) { Tests.Run(); return; }
        try { Application.Run(new Organizer()); }
        catch (Exception e) { MessageBox.Show(e.Message, "整理助手"); }
    }
}
class MovePlan {
    public string Source, Target, Stamp;
}
class ConfigurationSaveException : IOException {
    public ConfigurationSaveException(string message,Exception inner):base(message,inner){}
}
static class ConfigurationStore {
    static readonly object gate=new object();
    public static void Save(XElement document,string path) {
        lock(gate) {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {document.Save(output);output.Flush(true);}
                for(int attempt=0;;attempt++) {
                    try {if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);return;}
                    catch(IOException){if(attempt>=7)throw;System.Threading.Thread.Sleep(100*(attempt+1));}
                }
            } catch(Exception e) {
                throw new ConfigurationSaveException("配置保存失败，已重试。请关闭其他整理助手窗口，并检查配置文件是否被占用。\n已完成的文件操作不会撤销；未处理项目已停止。\n配置："+path+"\n本次配置副本（如已写入）："+temp+"\n原因："+e.Message,e);
            }
        }
    }
}
static class Safety {
    public static bool Exists(string p) { return File.Exists(p) || Directory.Exists(p); }
    public static bool Under(string p, string parent) { return Path.GetFullPath(p).TrimEnd('\\').StartsWith(Path.GetFullPath(parent).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
    public static void NoLinks(string path) {
        for (string p = Path.GetFullPath(path); !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if (Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new Exception("暂不处理符号链接、联接目录或云占位文件：" + p);
    }
    public static string Destination(string root, string relative) {
        if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new Exception("分类必须是资料库内的相对路径。");
        foreach (string part in relative.Split('\\', '/')) {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new Exception("分类路径含有无效名称。");
            string n = part.Split('.')[0].ToUpperInvariant();
            if (new [] {"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(n)) throw new Exception("分类不能使用系统保留名称。");
        }
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!Under(result, root)) throw new Exception("目标超出资料库。");
        return result;
    }
    public static string Stamp(string path) {
        NoLinks(path);
        if (!Exists(path)) throw new Exception("源项目不存在：" + path);
        var info = new FileInfo(path);
        return File.GetAttributes(path).ToString() + "|" + File.GetCreationTimeUtc(path).Ticks + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + (Directory.Exists(path) ? "dir" : info.Length.ToString());
    }
    public static void Check(MovePlan p) {
        NoLinks(p.Source); NoLinks(p.Target);
        if (!Exists(p.Source)) throw new Exception("源项目不存在：" + p.Source);
        if (String.Equals(p.Source, p.Target, StringComparison.OrdinalIgnoreCase)) throw new Exception("项目已在目标位置。");
        if (Under(p.Target, p.Source)) throw new Exception("不能把文件夹移入自身。");
        if (Exists(p.Target)) throw new Exception("目标已存在，不会覆盖或合并：" + p.Target);
        if (p.Stamp != Stamp(p.Source)) throw new Exception("项目在标注后发生变化，请取消标注后重新选择：" + p.Source);
    }
    public static void Move(MovePlan p) {
        Check(p);
        Directory.CreateDirectory(Path.GetDirectoryName(p.Target));
        NoLinks(p.Target);
        if (CrossDisk(p)) CrossMove(p, null); else if (Directory.Exists(p.Source)) Directory.Move(p.Source, p.Target); else File.Move(p.Source, p.Target);
    }
    public static bool CrossDisk(MovePlan p) { return !String.Equals(Path.GetPathRoot(p.Source),Path.GetPathRoot(p.Target),StringComparison.OrdinalIgnoreCase); }
    public static List<string> Inventory(string path) {
        NoLinks(path); var result=new List<string>();
        if(!Directory.Exists(path)) { result.Add(path); return result; }
        foreach(string child in Directory.GetFileSystemEntries(path)) { NoLinks(child); if(Directory.Exists(child)) result.AddRange(Inventory(child)); else result.Add(child); }
        return result;
    }
    public static long Bytes(string path) { return Inventory(path).Sum(f=>new FileInfo(f).Length); }
    public static void Space(string target,long required) {
        string drive=Path.GetPathRoot(target);
        if(drive.StartsWith("\\\\")) return;
        if(new DriveInfo(drive).AvailableFreeSpace<required+16L*1024*1024) throw new Exception("目标盘剩余空间不足，需要容纳当前项目及少量工作空间。");
    }
    static string Hash(Stream stream) { stream.Position=0; using(var hash=SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream)); }
    static void CopyTree(string source,string target,Action<string> progress) {
        NoLinks(source); NoLinks(target);
        if(Directory.Exists(source)) {
            Directory.CreateDirectory(target);
            foreach(string p in Directory.GetFileSystemEntries(source)) CopyTree(p,Path.Combine(target,Path.GetFileName(p)),progress);
            Directory.SetLastWriteTimeUtc(target,Directory.GetLastWriteTimeUtc(source));
        } else {
            if(progress!=null) progress("复制并校验："+Path.GetFileName(source));
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                File.Copy(source,target,false);
                using(var output=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)) { output.Flush(true); if(Hash(input)!=Hash(output)) throw new Exception("复制校验失败："+source); }
            }
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [StructLayout(LayoutKind.Sequential)] struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref Disposition data,uint size);
    static void RemoveVerifiedFile(string source,string target) {
        NoLinks(source); NoLinks(target);
        using(var destination=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var handle=CreateFile(source,0x80010000,1,IntPtr.Zero,3,0x08000000,IntPtr.Zero)) {
            if(handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法锁定源文件，源文件保留："+source);
            using(var input=new FileStream(handle,FileAccess.Read)) {
                if(Hash(input)!=Hash(destination)) throw new Exception("源文件在复制后变化，已保留两端文件："+source);
                var disposition=new Disposition { Delete=true };
                if(!SetFileInformationByHandle(handle,4,ref disposition,1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"删除源文件失败，已保留目标副本："+source);
            }
        }
    }
    static void RemoveEmptyTree(string path) {
        NoLinks(path);
        foreach(string child in Directory.GetDirectories(path)) RemoveEmptyTree(child);
        Directory.Delete(path,false);
    }
    public static void CrossMove(MovePlan p,Action<string> progress) {
        Check(p); long bytes=Bytes(p.Source); Space(p.Target,bytes);
        bool directory=Directory.Exists(p.Source);
        string temp=p.Target+".归位临时-"+Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(p.Target)); NoLinks(p.Target);
        try {
            var before=Inventory(p.Source).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).ToList();
            CopyTree(p.Source,temp,progress);
            var after=Inventory(p.Source).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).ToList();
            if(!before.SequenceEqual(after,StringComparer.OrdinalIgnoreCase)) throw new Exception("源目录内容发生变化，源文件保留。");
            foreach(string src in before) {
                string dst=directory?Path.Combine(temp,src.Substring(p.Source.TrimEnd('\\').Length+1)):temp;
                NoLinks(src); using(var a=new FileStream(src,FileMode.Open,FileAccess.Read,FileShare.Read)) using(var b=new FileStream(dst,FileMode.Open,FileAccess.Read,FileShare.Read)) if(Hash(a)!=Hash(b)) throw new Exception("源文件在复制后发生变化："+src);
            }
            Check(p);
            if(directory) Directory.Move(temp,p.Target); else File.Move(temp,p.Target);
            if(progress!=null) progress("已校验，正在移除源项目："+Path.GetFileName(p.Source));
            foreach(string src in before) RemoveVerifiedFile(src,directory?Path.Combine(p.Target,src.Substring(p.Source.TrimEnd('\\').Length+1)):p.Target);
            if(directory) RemoveEmptyTree(p.Source);
        } catch(Exception e) { throw new Exception(e.Message+"\n为避免误删，未自动清理残留。请检查源项目、目标及临时路径：\n"+temp,e); }
    }
}
class Organizer : Form {
    string root, folder;
    string state = Path.Combine(Program.Home, "整理助手配置.xml");
    List<string> categories = new List<string>();
    List<string> favorites = new List<string>();
    FlowLayoutPanel quickSources = new FlowLayoutPanel();
    Dictionary<string, MovePlan> queue = new Dictionary<string, MovePlan>(StringComparer.OrdinalIgnoreCase);
    ListView files = new ListView();
    FlowLayoutPanel categoryPanel = new FlowLayoutPanel();
    TextBox address = new TextBox(), search = new TextBox();
    Label status = new Label(), libraryLabel = new Label();
    ComboBox layout = new ComboBox();
    IProgress<string> transferProgress;
    bool moving;
    public Organizer() {
        Text = "归位 · 本地整理助手 0.2"; Width = 1280; Height = 820; MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(245,247,251);
        root = Path.Combine(Program.Home, "资料库"); folder = Program.Home;
        LoadState();
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(16) };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); outer.RowStyles.Add(new RowStyle(SizeType.Percent,100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        Controls.Add(outer);
        var heading = new FlowLayoutPanel { Dock=DockStyle.Fill };
        heading.Controls.Add(new Label { Text="归位", Font=new Font(Font.FontFamily,23,FontStyle.Bold), AutoSize=true, Margin=new Padding(0,0,24,0) });
        heading.Controls.Add(new Label { Text="① 框选项目     ② 单击右侧分类     ③ 预览并批量移动", AutoSize=true, Margin=new Padding(0,17,0,0) }); outer.Controls.Add(heading,0,0);
        var nav = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=5 };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,75)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
        nav.Controls.Add(Button("选择来源", delegate { ChooseFolder(false); }),0,0);
        nav.Controls.Add(Button("上一级", delegate { if (Directory.GetParent(folder)!=null) Navigate(Directory.GetParent(folder).FullName); }),1,0);
        address.Dock=DockStyle.Fill; address.KeyDown += delegate(object s,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { Navigate(address.Text); e.SuppressKeyPress=true; } }; nav.Controls.Add(address,2,0);
        nav.Controls.Add(Button("刷新", delegate { Navigate(folder); }),3,0);
        layout.DropDownStyle=ComboBoxStyle.DropDownList; layout.Items.AddRange(new object[]{"图标框选","详细列表"}); layout.SelectedIndex=0; layout.SelectedIndexChanged += delegate { files.View=layout.SelectedIndex==0 ? View.LargeIcon : View.Details; }; nav.Controls.Add(layout,4,0); outer.Controls.Add(nav,0,1);
        quickSources.Dock=DockStyle.Fill; quickSources.AutoScroll=true; quickSources.WrapContents=false; outer.Controls.Add(quickSources,0,2); RenderFavorites();
        var split=new SplitContainer { Size=new Size(1200,600), Dock=DockStyle.Fill, FixedPanel=FixedPanel.Panel2, Panel2MinSize=300 }; split.SplitterDistance=760; outer.Controls.Add(split,0,3);
        files.Dock=DockStyle.Fill; files.View=View.LargeIcon; files.MultiSelect=true; files.HideSelection=false; files.FullRowSelect=true; files.GridLines=false; files.BackColor=Color.White; files.BorderStyle=BorderStyle.None;
        files.Columns.Add("名称",280); files.Columns.Add("类型",80); files.Columns.Add("已标注分类",330);
        var images=new ImageList { ImageSize=new Size(48,48), ColorDepth=ColorDepth.Depth32Bit };
        images.Images.Add("folder", MakeIcon(true)); images.Images.Add("file",MakeIcon(false)); files.LargeImageList=images; files.SmallImageList=images;
        files.ItemActivate += delegate { if(files.SelectedItems.Count==1) { string p=(string)files.SelectedItems[0].Tag; if(Directory.Exists(p)) Navigate(p); } };
        files.SelectedIndexChanged += delegate { UpdateStatus(); };
        files.KeyDown += delegate(object s,KeyEventArgs e) { if(e.Control && e.KeyCode==Keys.A) { foreach(ListViewItem i in files.Items) i.Selected=true; e.SuppressKeyPress=true; } if(e.KeyCode==Keys.Delete) { Unmark(); e.SuppressKeyPress=true; } };
        split.Panel1.Controls.Add(files);
        var right=new TableLayoutPanel { Dock=DockStyle.Fill, RowCount=4, ColumnCount=1, Padding=new Padding(12,0,0,0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); right.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); right.RowStyles.Add(new RowStyle(SizeType.Percent,100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute,80)); split.Panel2.Controls.Add(right);
        libraryLabel.Dock=DockStyle.Fill; libraryLabel.AutoEllipsis=true; right.Controls.Add(libraryLabel,0,0);
        search.Dock=DockStyle.Fill; search.TextChanged+=delegate { RenderCategories(); }; right.Controls.Add(search,0,1);
        categoryPanel.Dock=DockStyle.Fill; categoryPanel.AutoScroll=true; categoryPanel.FlowDirection=FlowDirection.TopDown; categoryPanel.WrapContents=false; right.Controls.Add(categoryPanel,0,2);
        var manage=new FlowLayoutPanel { Dock=DockStyle.Fill }; manage.Controls.Add(Button("编辑分类",delegate { EditCategories(); })); manage.Controls.Add(Button("选择 / 新建资料库",delegate { ChooseFolder(true); })); manage.Controls.Add(Button("创建全部框架目录",delegate { try { CreateFramework(root,categories); MessageBox.Show("完整框架已创建，已有文件和目录保持不变。\n"+root); } catch(Exception e) { MessageBox.Show(e.Message); } })); right.Controls.Add(manage,0,3);
        var bottom=new FlowLayoutPanel { Dock=DockStyle.Fill }; bottom.Controls.Add(Button("预览 / 批量移动",delegate { Preview(); })); bottom.Controls.Add(Button("取消所选标注",delegate { Unmark(); })); bottom.Controls.Add(Button("清空标注",delegate { if(MessageBox.Show("清空全部待移动标注？原文件不会改变。","清空",MessageBoxButtons.OKCancel)==DialogResult.OK) { queue.Clear(); Save(); Navigate(folder); } })); status.AutoSize=true; status.Margin=new Padding(16,12,0,0); bottom.Controls.Add(status); outer.Controls.Add(bottom,0,4);
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(moving) { e.Cancel=true; return; } Save(); };
        RenderCategories(); Navigate(Directory.Exists(folder)?folder:Program.Home);
    }
    static Bitmap MakeIcon(bool directory) {
        var b=new Bitmap(48,48); using(var g=Graphics.FromImage(b)) { g.Clear(Color.Transparent); using(var brush=new SolidBrush(directory?Color.FromArgb(240,184,65):Color.FromArgb(89,136,215))) { g.FillRectangle(brush,5, directory?15:5,38,directory?27:38); if(directory) g.FillRectangle(brush,5,9,18,10); } } return b;
    }
    Button Button(string title, EventHandler action) { var b=new Button { Text=title, AutoSize=true, Height=34, FlatStyle=FlatStyle.Flat, BackColor=Color.White, Margin=new Padding(3) }; b.Click+=action; return b; }
    void LoadState() {
        if(File.Exists(state)) {
            try { var x=XElement.Load(state); root=(string)x.Attribute("root"); folder=(string)x.Attribute("folder"); categories=x.Elements("category").Select(e=>e.Value).ToList(); favorites=x.Elements("favorite").Select(e=>e.Value).ToList(); foreach(var e in x.Elements("move")) { var p=new MovePlan { Source=(string)e.Attribute("source"),Target=(string)e.Attribute("target"),Stamp=(string)e.Attribute("stamp") }; queue[p.Source]=p; } return; }
            catch(Exception e) { throw new Exception("配置读取失败。请先保留配置文件再处理："+e.Message); }
        }
        if(Directory.Exists(root)) foreach(var d in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderBy(p=>p)) if(Directory.GetDirectories(d).Length==0) categories.Add(d.Substring(root.Length+1));
        if(categories.Count==0) categories.AddRange(DefaultCategories());
        foreach(string path in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}) if(Directory.Exists(path)) favorites.Add(path);
    }
    void Save() {
        var x=new XElement("organizer",new XAttribute("root",root),new XAttribute("folder",folder));
        foreach(string c in categories) x.Add(new XElement("category",c));
        foreach(string f in favorites) x.Add(new XElement("favorite",f));
        foreach(var p in queue.Values) x.Add(new XElement("move",new XAttribute("source",p.Source),new XAttribute("target",p.Target),new XAttribute("stamp",p.Stamp)));
        ConfigurationStore.Save(x,state);
    }
    void ChooseFolder(bool target) {
        if(target) { LibraryDialog(); return; }
        using(var dialog=new FolderBrowserDialog { Description="选择待整理目录", SelectedPath=folder }) if(dialog.ShowDialog()==DialogResult.OK) {
            Navigate(dialog.SelectedPath);
        }
    }
    public static IEnumerable<string> DefaultCategories() {
        return new[]{"待整理","文档","图片\\风景","视频\\片段","其他"};
    }
    public static void CreateFramework(string location,IEnumerable<string> categories) {
        var paths=categories.Select(c=>Safety.Destination(location,c)).ToList();
        Safety.NoLinks(location); foreach(string path in paths) { Safety.NoLinks(path); if(File.Exists(path)) throw new Exception("分类路径被文件占用："+path); }
        Directory.CreateDirectory(location); foreach(string path in paths) Directory.CreateDirectory(path);
    }
    void LibraryDialog() {
        if(queue.Count>0) { MessageBox.Show("请先执行或清空标注，再更换资料库；避免改变已经标注的目标。"); return; }
        using(var dialog=new Form { Text="选择或新建资料库",Width=730,Height=290,StartPosition=FormStartPosition.CenterParent,Font=Font }) {
            var panel=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(16),WrapContents=false };
            var path=new TextBox { Width=660,Text=root };
            var sub=new CheckBox { AutoSize=true,Text="在所选位置下新建“资料库”子文件夹" };
            var create=new CheckBox { AutoSize=true,Text="同时创建当前框架的全部空目录（已有内容不覆盖）",Checked=true };
            panel.Controls.Add(new Label { AutoSize=true,Text="输入位置或浏览选择（可以填写尚未存在的完整路径）：" }); panel.Controls.Add(path);
            panel.Controls.Add(Button("浏览…",delegate { using(var picker=new FolderBrowserDialog { SelectedPath=Directory.Exists(path.Text)?path.Text:root }) if(picker.ShowDialog()==DialogResult.OK) path.Text=picker.SelectedPath; }));
            panel.Controls.Add(sub); panel.Controls.Add(create);
            panel.Controls.Add(Button("使用此资料库",delegate { try { if(!Path.IsPathRooted(path.Text)) throw new Exception("请输入完整绝对路径。"); string selected=Path.GetFullPath(path.Text); if(sub.Checked) selected=Path.Combine(selected,"资料库"); Safety.NoLinks(selected); if(create.Checked) CreateFramework(selected,categories); else Directory.CreateDirectory(selected); root=selected; Save(); RenderCategories(); dialog.Close(); } catch(Exception e) { MessageBox.Show(e.Message,"未能设置资料库"); } }));
            dialog.Controls.Add(panel); dialog.ShowDialog(this);
        }
    }
    void RenderFavorites() {
        quickSources.Controls.Clear();
        quickSources.Controls.Add(Button("＋收藏当前来源",delegate { if(!favorites.Contains(folder,StringComparer.OrdinalIgnoreCase)) favorites.Add(folder); Save(); RenderFavorites(); }));
        quickSources.Controls.Add(Button("管理快捷目录",delegate { EditFavorites(); }));
        foreach(string f in favorites) { string p=f; var button=Button(Path.GetFileName(p.TrimEnd('\\'))+"  ["+Path.GetPathRoot(p).TrimEnd('\\')+"]",delegate { Navigate(p); }); new ToolTip().SetToolTip(button,p); quickSources.Controls.Add(button); }
    }
    void EditFavorites() {
        using(var dialog=new Form { Text="来源快捷目录",Width=720,Height=460,Font=Font,StartPosition=FormStartPosition.CenterParent }) {
            var text=new TextBox { Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Text=String.Join(Environment.NewLine,favorites) };
            var note=new Label { Dock=DockStyle.Top,Height=60,Text="每行一个来源文件夹的完整路径，可增删、修改、调整顺序。\n保存后显示为顶部按钮，下次打开仍保留；启动时恢复上次浏览目录。" };
            var save=Button("保存快捷目录",delegate { try { var paths=text.Lines.Select(v=>v.Trim()).Where(v=>v.Length>0).ToList(); if(paths.Any(v=>!Path.IsPathRooted(v))) throw new Exception("请填写完整绝对路径。"); favorites=paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Save(); RenderFavorites(); dialog.Close(); } catch(Exception e) { MessageBox.Show(e.Message); } }); save.Dock=DockStyle.Bottom;
            dialog.Controls.Add(text); dialog.Controls.Add(note); dialog.Controls.Add(save); dialog.ShowDialog(this);
        }
    }
    void Navigate(string path) {
        try {
            Safety.NoLinks(path); var entries=new DirectoryInfo(path).GetFileSystemInfos().OrderByDescending(i=>(i.Attributes&FileAttributes.Directory)!=0).ThenBy(i=>i.Name).ToArray();
            folder=Path.GetFullPath(path); address.Text=folder; files.BeginUpdate(); files.Items.Clear();
            foreach(var f in entries) { bool dir=(f.Attributes&FileAttributes.Directory)!=0; MovePlan p; string mark=queue.TryGetValue(f.FullName,out p)?RelativeCategory(p.Target):""; var item=new ListViewItem((mark.Length>0?"✓ ":"")+f.Name,dir?"folder":"file") { Tag=f.FullName,ToolTipText=f.FullName+(mark.Length>0?"\n→ "+mark:"") }; item.SubItems.Add(dir?"文件夹":f.Extension); item.SubItems.Add(mark); if(mark.Length>0) item.ForeColor=Color.FromArgb(24,125,93); files.Items.Add(item); }
            files.ShowItemToolTips=true; files.EndUpdate(); UpdateStatus();
        } catch(Exception e) { files.EndUpdate(); MessageBox.Show(e.Message,"无法打开目录"); }
    }
    string RelativeCategory(string target) { string d=Path.GetDirectoryName(target); return Safety.Under(d,root)?d.Substring(root.TrimEnd('\\').Length+1):d; }
    void UpdateStatus() { status.Text="已选 "+files.SelectedItems.Count+" 项 · 待移动 "+queue.Count+" 项（文件夹整体计算）"; }
    void RenderCategories() {
        libraryLabel.Text="单击分类即标注 · 搜索分类↓\n资料库："+root;
        categoryPanel.SuspendLayout(); categoryPanel.Controls.Clear();
        foreach(string c in categories.Where(c=>c.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0)) { string category=c; var b=Button(c.Replace("\\","  ›  "),delegate { Mark(category); }); b.AutoSize=false; b.Width=Math.Max(290,categoryPanel.ClientSize.Width-26); b.Height=43; b.TextAlign=ContentAlignment.MiddleLeft; categoryPanel.Controls.Add(b); }
        categoryPanel.ResumeLayout();
    }
    void Mark(string category) {
        if(files.SelectedItems.Count==0) { status.Text="先在左侧单选、Ctrl 多选或拖动框选项目。"; return; }
        try {
            var additions=new List<MovePlan>();
            foreach(ListViewItem item in files.SelectedItems) {
                string source=(string)item.Tag;
                if(String.Equals(source,root,StringComparison.OrdinalIgnoreCase)||Safety.Under(root,source)) throw new Exception("不能标注资料库本身或包含资料库的目录。");
                if(queue.Keys.Any(k=>Safety.Under(k,source)||Safety.Under(source,k))) throw new Exception("父文件夹和内部项目不能同时标注，请先取消其中一项。");
                var p=new MovePlan { Source=source,Target=Path.Combine(Safety.Destination(root,category),Path.GetFileName(source)),Stamp=Safety.Stamp(source) }; additions.Add(p);
            }
            foreach(var p in additions) queue[p.Source]=p; Save();
            foreach(ListViewItem item in files.SelectedItems) { item.Text="✓ "+Path.GetFileName((string)item.Tag); item.ForeColor=Color.FromArgb(24,125,93); item.SubItems[2].Text=category; item.ToolTipText=(string)item.Tag+"\n→ "+category; }
            UpdateStatus();
        } catch(Exception e) { MessageBox.Show(e.Message,"未完成标注"); }
    }
    void Unmark() { foreach(ListViewItem i in files.SelectedItems) queue.Remove((string)i.Tag); Save(); Navigate(folder); }
    void EditCategories() {
        using(var dialog=new Form { Text="编辑分类框架",Width=760,Height=640,StartPosition=FormStartPosition.CenterParent,Font=Font }) {
            var text=new TextBox { Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,Text=String.Join(Environment.NewLine,categories) };
            var note=new Label { Dock=DockStyle.Top,Height=78,Text="每行一个目标分类，用 \\ 分隔层级。直接增加、删除或修改行。\n保存修改分类入口，执行移动时才创建目标目录。不会移动或删除旧目录及内容。\n已有标注保持原目标；修改后需要重新标注才能改变它们的目标。" };
            var ok=Button("保存框架",delegate {
                try { var values=text.Lines.Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); foreach(string c in values) Safety.Destination(root,c); if(values.Count==0) throw new Exception("至少保留一个分类。"); categories=values; Save(); RenderCategories(); dialog.Close(); } catch(Exception e) { MessageBox.Show(e.Message); }
            }); ok.Dock=DockStyle.Bottom; dialog.Controls.Add(text); dialog.Controls.Add(note); dialog.Controls.Add(ok); dialog.ShowDialog(this);
        }
    }
    void Preview() {
        if(queue.Count==0) { MessageBox.Show("先选择项目并单击一个分类。"); return; }
        var plans=queue.Values.ToList();
        using(var dialog=new Form { Text="移动预览 · 确认后才操作原文件",Width=1100,Height=650,StartPosition=FormStartPosition.CenterParent,Font=Font }) {
            var grid=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false };
            grid.Columns.Add("source","原位置"); grid.Columns.Add("target","目标位置"); grid.Columns.Add("check","检查结果");
            bool valid=true; var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in plans) { string mode=Safety.CrossDisk(p)?"跨盘：复制 → SHA-256校验 → 删除源":"同盘：直接移动"; string error=mode; try { Safety.Check(p); if(!destinations.Add(p.Target)) throw new Exception("多个项目指向同一目标"); if(plans.Any(q=>q!=p&&(Safety.Under(p.Source,q.Source)||Safety.Under(p.Target,q.Source)||Safety.Under(q.Target,p.Target)||Safety.Under(p.Target,q.Target)))) throw new Exception("计划包含相互依赖目录，请分批处理"); } catch(Exception e) { error=e.Message; valid=false; } int row=grid.Rows.Add(p.Source,p.Target,error); if(error!=mode) grid.Rows[row].DefaultCellStyle.ForeColor=Color.Firebrick; }
            var bar=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=85 };
            var progressLabel=new Label { AutoSize=true,MaximumSize=new Size(850,0),Text=valid?"跨盘时目标盘需容纳当前整个项目；空间不足会停止并保留源文件。":"存在冲突：请返回取消或修改相关标注后重新预览。",Margin=new Padding(12) };
            dialog.FormClosing+=delegate(object s,FormClosingEventArgs e) { if(moving) e.Cancel=true; };
            var execute=Button("确认移动 "+plans.Count+" 项",async delegate {
                if(MessageBox.Show("将执行上表中的 "+plans.Count+" 项移动。文件夹内部不拆分，同名不覆盖。\n跨盘项目复制并校验后才删除源文件。是否继续？","确认批量移动",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK) return;
                dialog.Enabled=false; moving=true; transferProgress=new Progress<string>(text=>{ if(!progressLabel.IsDisposed) progressLabel.Text=text; });
                string report;
                try { report=await Task.Run(()=>Execute(plans)); } catch(Exception e) { report=e.Message; }
                finally { moving=false; transferProgress=null; dialog.Enabled=true; }
                dialog.Close(); Navigate(folder); MessageBox.Show(report,"执行结果");
            }); execute.Enabled=valid; bar.Controls.Add(execute); bar.Controls.Add(progressLabel); dialog.Controls.Add(grid); dialog.Controls.Add(bar); dialog.ShowDialog(this);
        }
    }
    string Execute(List<MovePlan> plans) {
        int done=0; string error=""; string log=Path.Combine(Program.Home,"移动日志-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".xml"); var journal=new XElement("batch",new XAttribute("created",DateTime.Now.ToString("o")));
        try {
            Save(); foreach(var p in plans) Safety.Check(p);
            journal.Save(log);
            foreach(var p in plans) {
                var entry=new XElement("move",new XAttribute("source",p.Source),new XAttribute("target",p.Target),new XAttribute("status","准备移动")); journal.Add(entry); journal.Save(log);
                if(transferProgress!=null) transferProgress.Report("正在处理 "+(done+1)+" / "+plans.Count+"："+Path.GetFileName(p.Source));
                if(Safety.CrossDisk(p)) Safety.CrossMove(p,text=>{if(transferProgress!=null) transferProgress.Report(text);}); else Safety.Move(p);
                done++; queue.Remove(p.Source); entry.SetAttributeValue("status","已完成"); journal.Save(log); Save();
            }
        } catch(Exception e) { error="\n已停止后续操作："+e.Message; try { journal.SetAttributeValue("error",e.Message); journal.Save(log); } catch { } }
        return "已移动 "+done+" / "+plans.Count+" 项。"+error+"\n日志："+log+"\n未完成项目保留标注。";
    }
}
static class Tests {
    public static void Run() {
        string root=Path.Combine(Program.Home,"test-work-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); int passed=0;
        Action<bool,string> assert=(ok,message)=>{if(!ok) throw new Exception(message); passed++;};
        Action<Action,string> reject=(action,message)=>{bool rejected=false;try{action();}catch{rejected=true;}assert(rejected,message);};
        try {
            string config=Path.Combine(root,"config.xml");ConfigurationStore.Save(new XElement("config","old"),config);ConfigurationStore.Save(new XElement("config","new"),config);
            assert(XElement.Load(config).Value=="new"&&XElement.Load(config+".bak").Value=="old","atomic configuration and backup");
            var blocker=new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read);
            var release=Task.Run(()=>{System.Threading.Thread.Sleep(350);blocker.Dispose();});
            ConfigurationStore.Save(new XElement("config","retried"),config);release.Wait();assert(XElement.Load(config).Value=="retried","configuration retries transient lock");
            using(var locked=new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read)) {reject(()=>ConfigurationStore.Save(new XElement("config","pending"),config),"persistent lock must stop save");}
            assert(XElement.Load(config).Value=="retried","failed save preserves configuration");
            assert(Directory.GetFiles(root,"config.xml.*.tmp").Any(f=>XElement.Load(f).Value=="pending"),"failed save retains recovery copy");
            string source=Path.Combine(root,"source");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"001.txt"),"page1");Directory.CreateDirectory(Path.Combine(source,"chapter"));File.WriteAllText(Path.Combine(source,"chapter","002.txt"),"page2");
            var p=new MovePlan { Source=source,Target=Path.Combine(root,"library","comic","source"),Stamp=Safety.Stamp(source) };Safety.Move(p);assert(File.ReadAllText(Path.Combine(p.Target,"chapter","002.txt"))=="page2","whole folder contents");assert(!Directory.Exists(source),"source moved");
            string file=Path.Combine(root,"a.txt");File.WriteAllText(file,"hello");var q=new MovePlan { Source=file,Target=Path.Combine(root,"b.txt"),Stamp=Safety.Stamp(file) };File.WriteAllText(q.Target,"existing");reject(()=>Safety.Move(q),"collision must reject");assert(File.ReadAllText(q.Target)=="existing"&&File.Exists(file),"collision preserves files");
            q.Target=Path.Combine(root,"c.txt");File.AppendAllText(file,"changed");reject(()=>Safety.Move(q),"changed source must reject");
            reject(()=>Safety.Destination(root,"..\\outside"),"path traversal");reject(()=>Safety.Destination(root,"CON"),"reserved name");
            var nested=new MovePlan { Source=p.Target,Target=Path.Combine(p.Target,"child","source"),Stamp=Safety.Stamp(p.Target) };reject(()=>Safety.Move(nested),"self descendant");
            q.Stamp=Safety.Stamp(file);Safety.Move(q);assert(File.Exists(q.Target)&&!File.Exists(file),"file move");
            string crossBase=Environment.GetEnvironmentVariable("ORGANIZER_TEST_TARGET");
            if(String.IsNullOrEmpty(crossBase)) crossBase=Path.Combine(root,"cross-destination");
            crossBase=Path.Combine(crossBase,"organizer-test-"+Guid.NewGuid().ToString("N"));
            string crossSource=Path.Combine(root,"cross-source"); Directory.CreateDirectory(Path.Combine(crossSource,"empty")); Directory.CreateDirectory(Path.Combine(crossSource,"chapter")); File.WriteAllBytes(Path.Combine(crossSource,"chapter","data.bin"),Enumerable.Range(0,20000).Select(i=>(byte)(i%251)).ToArray());
            var cross=new MovePlan { Source=crossSource,Target=Path.Combine(crossBase,"whole-folder"),Stamp=Safety.Stamp(crossSource) }; Safety.CrossMove(cross,null);
            assert(!Directory.Exists(crossSource),"cross source removed");assert(Directory.Exists(Path.Combine(cross.Target,"empty")),"cross empty folder preserved");assert(File.ReadAllBytes(Path.Combine(cross.Target,"chapter","data.bin")).Length==20000,"cross nested bytes preserved");
            string changing=Path.Combine(root,"changing.txt");File.WriteAllText(changing,"original");var changed=new MovePlan { Source=changing,Target=Path.Combine(crossBase,"changed.txt"),Stamp=Safety.Stamp(changing) };
            reject(()=>Safety.CrossMove(changed,text=>{if(text.StartsWith("已校验")) File.AppendAllText(changing,"new-content");}),"change before deletion rejected");assert(File.ReadAllText(changing)=="originalnew-content","changed source kept");assert(File.ReadAllText(changed.Target)=="original","verified target kept");
            string failSource=Path.Combine(root,"failure.txt");File.WriteAllText(failSource,"keep-me");var failure=new MovePlan { Source=failSource,Target=Path.Combine(crossBase,"failure.txt"),Stamp=Safety.Stamp(failSource) };
            reject(()=>Safety.CrossMove(failure,text=>{if(text.StartsWith("复制")) throw new IOException("injected copy error");}),"copy failure propagated");assert(File.ReadAllText(failSource)=="keep-me"&&!File.Exists(failure.Target),"copy failure keeps source");
            reject(()=>Safety.Space(crossBase,Int64.MaxValue/2),"insufficient space rejected");
            string framework=Path.Combine(root,"fresh-library");Organizer.CreateFramework(framework,Organizer.DefaultCategories());assert(Directory.GetDirectories(framework,"*",SearchOption.AllDirectories).Length==7,"all framework directories");File.WriteAllText(Path.Combine(framework,"04_漫画","existing.txt"),"keep");Organizer.CreateFramework(framework,Organizer.DefaultCategories());assert(File.ReadAllText(Path.Combine(framework,"04_漫画","existing.txt"))=="keep","framework idempotent");
            File.WriteAllText(Path.Combine(Program.Home,"test-results.txt"),"PASS: "+passed+" safety checks\r\nFixtures: "+root);
        }catch(Exception e){File.WriteAllText(Path.Combine(Program.Home,"test-results.txt"),"FAIL: "+e);Environment.ExitCode=1;}
    }
}
