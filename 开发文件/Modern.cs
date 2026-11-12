using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Forms=System.Windows.Forms;

public class CardItem : INotifyPropertyChanged {
 public string Name {get;set;} public string FullPath {get;set;} public string Kind {get;set;} public string Subtitle {get;set;} public string Glyph {get;set;} public string TileColor {get;set;} public string Accent {get;set;} public bool IsDirectory {get;set;}
 public string IconData {get{return IsDirectory?"M 2,9 L 19,9 L 24,15 L 47,15 L 47,40 L 2,40 Z":"M 8,2 L 30,2 L 42,14 L 42,46 L 8,46 Z M 30,2 L 30,14 L 42,14 M 16,26 L 34,26 M 16,34 L 30,34";}}
 string assignment="未标注",badge="○"; ImageSource thumbnail;
 public string Assignment {get{return assignment;}set{assignment=value;Changed("Assignment");}}
 public string SelectionBadge {get{return badge;}set{badge=value;Changed("SelectionBadge");}}
 public ImageSource Thumbnail {get{return thumbnail;}set{thumbnail=value;Changed("Thumbnail");}}
 public event PropertyChangedEventHandler PropertyChanged;
 void Changed(string property){if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(property));}
}
public class CategoryPreference {
 public string Path {get;set;}
 public string Alias {get;set;}
 public bool Pinned {get;set;}
 public int Order {get;set;}
}
class RecycleResult {public bool Success;public string Message;public override string ToString(){return Message;}}
public class CompactGroupPanel : Panel {
 class Row {public double Width,Height;public List<UIElement> Items=new List<UIElement>();}
 List<Row> Layout(double width){var rows=new List<Row>();foreach(UIElement child in Children){child.Measure(new Size(width,double.PositiveInfinity));Size size=child.DesiredSize;var row=rows.FirstOrDefault(r=>r.Width+size.Width<=width+0.5&&Math.Abs(r.Height-size.Height)<3);if(row==null){row=new Row{Height=size.Height};rows.Add(row);}row.Items.Add(child);row.Width+=size.Width;}return rows;}
 protected override Size MeasureOverride(Size constraint){double width=Double.IsInfinity(constraint.Width)?640:constraint.Width;var rows=Layout(width);return new Size(width,rows.Sum(r=>r.Height));}
 protected override Size ArrangeOverride(Size size){double y=0;foreach(var row in Layout(size.Width)){double x=0;foreach(var child in row.Items){child.Arrange(new Rect(x,y,child.DesiredSize.Width,row.Height));x+=child.DesiredSize.Width;}y+=row.Height;}return size;}
}
static class ModernProgram {
 [STAThread] static void Main(string[] args) {
  try {
   if(args.Contains("--self-test")){Tests.Run();return;}
   bool test=args.Contains("--ui-test")||args.Contains("--layout-test");
   if(test) Program.Home=Path.Combine(Program.Home,"开发文件","modern-test-"+Guid.NewGuid().ToString("N"))+Path.DirectorySeparatorChar;
   Directory.CreateDirectory(Program.Home);
   var app=new Application(); var ui=new ModernWindow();
   if(test) { ui.Window.WindowStartupLocation=WindowStartupLocation.Manual; ui.Window.Left=-3000; ui.Window.Top=-3000; ui.Window.Loaded+=async delegate { try {await ui.SmokeTest(!args.Contains("--layout-test"));} catch(Exception e){File.WriteAllText(Path.Combine(Program.Home,"modern-test-results.txt"),"FAIL: "+e);Environment.ExitCode=1;} app.Shutdown(); }; }
   app.Run(ui.Window);
  }catch(Exception e){if(args.Contains("--ui-test")||args.Contains("--layout-test")){File.WriteAllText(Path.Combine(Program.Home,"modern-test-results.txt"),"FAIL: "+e);Environment.ExitCode=1;}else MessageBox.Show(e.Message,"归位");}
 }
}
partial class ModernWindow {
 public Window Window;
 string root,folder,state,group="",sourceFolder="",libraryFolder="";
 List<string> categories=new List<string>(),favorites=new List<string>();
 List<string> renamePrefixes=new List<string>(); int hiddenPending;
 Dictionary<string,string> categoryAliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 List<string> pinnedCategories=new List<string>();
 Dictionary<string,MovePlan> queue=new Dictionary<string,MovePlan>(StringComparer.OrdinalIgnoreCase);
 List<CardItem> items=new List<CardItem>();
 ListBox cards; int generation; bool moving,dragging; Point dragStart; HashSet<CardItem> previousSelection; bool dragAdd;
 System.Windows.Threading.DispatcherTimer marqueeTimer; ScrollViewer marqueeScroll; double dragOffset; Task thumbnails=Task.FromResult(0);
 T E<T>(string name) where T:FrameworkElement {return (T)Window.FindName(name);}
 public ModernWindow(){
  using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Modern.xaml")) Window=(Window)XamlReader.Load(stream);
  state=Path.Combine(Program.Home,"整理助手配置.xml");root=Path.Combine(Program.Home,"资料库");folder=Program.Home;
  Load();if(String.IsNullOrEmpty(sourceFolder))sourceFolder=InLibrary(folder)?Program.Home:folder;cards=E<ListBox>("Cards");
  Click("SourceButton",()=>ChooseSource());Click("FavoriteButton",()=>AddFavorite());Click("ManageSourcesButton",()=>EditFavorites());
  Click("LibraryButton",()=>LibraryDialog());Click("CreateFrameworkButton",()=>{Organizer.CreateFramework(root,categories);Toast("已补齐全部框架目录，已有内容保持不变。");});Click("EditCategoriesButton",()=>EditCategories());
  Click("CategoryPreferencesButton",()=>EditCategoryPreferences());Click("RefreshCategoryFoldersButton",()=>{RenderSidebar();RenderCategories();Toast("已重新读取资料库目录，展开箭头可逐层查看。");});
  Click("UpButton",()=>{var p=Directory.GetParent(folder);if(p!=null)Navigate(p.FullName);});Click("RefreshButton",()=>Navigate(folder));Click("CurrentButton",()=>BrowseSource());Click("BrowseLibraryButton",()=>BrowseLibrary());
  Click("PreviewButton",()=>Preview());Click("QueueButton",()=>Preview());Click("UnmarkButton",()=>Unmark());Click("ClearButton",()=>{if(Confirm("清空全部待移动标注？不会移动或删除原文件。")){queue.Clear();Save();RefreshAssignments();}});
  Click("OpenButton",()=>OpenSelected());Click("OpenFolderButton",()=>OpenSelectedFolders());Click("ExplorerButton",()=>OpenCurrentFolder());
  Click("DeleteButton",()=>PrepareRecycle());Click("ToolbarDeleteButton",()=>PrepareRecycle());Click("RenameButton",()=>RenameSelected());
  E<TextBox>("Address").KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Navigate(E<TextBox>("Address").Text);e.Handled=true;}};
  E<TextBox>("FileSearch").TextChanged+=delegate{Filter();};E<TextBox>("CategorySearch").TextChanged+=delegate{if(E<TextBox>("CategorySearch").Text.Length>0)group="";RenderCategories();};
  cards.SelectionChanged+=delegate{UpdateSelection();};
  cards.MouseDoubleClick+=delegate(object sender,MouseButtonEventArgs e){var element=FindItem(e.OriginalSource as DependencyObject);if(element!=null){var item=(CardItem)element.DataContext;if(item.IsDirectory)Navigate(item.FullPath);}};
  cards.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.A&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){cards.SelectAll();e.Handled=true;}if(e.Key==Key.Delete){Unmark();e.Handled=true;}};
  var viewport=E<Grid>("CardsViewport");viewport.PreviewMouseLeftButtonDown+=BeginMarquee;viewport.PreviewMouseMove+=MoveMarquee;viewport.PreviewMouseLeftButtonUp+=EndMarquee; marqueeTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};marqueeTimer.Tick+=delegate{ScrollMarquee();};viewport.LostMouseCapture+=delegate{StopMarquee();};
  Window.Closing+=delegate(object sender,CancelEventArgs e){if(moving){e.Cancel=true;return;}Save();OpenActions.ReleasePins();};
  Window.Loaded+=delegate{RenderSidebar();RenderCategories();Navigate(Directory.Exists(folder)?folder:Program.Home);};
 }
 static string Clean(string s){return s.Length>3&&s[2]=='_'?s.Substring(3):s;}
 static string DisplayCategory(string s){return String.Join(" / ",s.Split('\\').Select(Clean));}
 void Toast(string text){E<TextBlock>("Toast").Text=text;}
 void Click(string name,Action action){E<Button>(name).Click+=delegate{try{action();}catch(Exception e){MessageBox.Show(Window,e.Message,"归位");}};}
 bool Confirm(string message){return MessageBox.Show(Window,message,"归位",MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.OK;}
 Button B(string text,Action action,bool soft=false){var b=new Button{Content=text,Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip=text};if(soft)b.Style=(Style)Window.Resources["Soft"];b.Click+=delegate{try{action();}catch(Exception e){MessageBox.Show(Window,e.Message,"归位");}};return b;}
 void Load(){
  if(File.Exists(state)){var x=XElement.Load(state);root=(string)x.Attribute("root")??root;folder=(string)x.Attribute("folder")??folder;sourceFolder=(string)x.Attribute("sourceFolder")??"";libraryFolder=(string)x.Attribute("libraryFolder")??"";if(x.Element("renamePrefixes")!=null)renamePrefixes=x.Element("renamePrefixes").Elements("prefix").Select(p=>p.Value).Where(p=>!String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(x.Element("categoryUsage")!=null&&(string)x.Element("categoryUsage").Attribute("mode")=="successful-batches")foreach(var choice in x.Element("categoryUsage").Elements("choice")){long count;string path=(string)choice.Attribute("path");if(!String.IsNullOrEmpty(path)&&Int64.TryParse((string)choice.Attribute("count"),out count)&&count>0)categoryChoiceCounts[path]=count;}categories=x.Elements("category").Select(v=>v.Value).ToList();favorites=x.Elements("favorite").Select(v=>v.Value).ToList();foreach(var v in x.Elements("alias")){string path=(string)v.Attribute("path");if(path!=null)categoryAliases[path]=v.Value;}pinnedCategories=x.Elements("pinnedCategory").Select(v=>v.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();foreach(var v in x.Elements("move")){var p=new MovePlan{Source=(string)v.Attribute("source"),Target=(string)v.Attribute("target"),Stamp=(string)v.Attribute("stamp")};queue[p.Source]=p;}}
  else{categories=Organizer.DefaultCategories().ToList();foreach(string p in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)})if(Directory.Exists(p))favorites.Add(p);}
  foreach(string path in queue.Keys.Where(p=>!Directory.Exists(p)&&IsAuxiliaryName(Path.GetFileName(p))).ToList()){queue.Remove(path);hiddenPending++;}
 }
 void Save(){var x=new XElement("organizer",new XAttribute("root",root),new XAttribute("folder",folder),new XAttribute("sourceFolder",sourceFolder),new XAttribute("libraryFolder",libraryFolder));foreach(string c in categories)x.Add(new XElement("category",c));foreach(string f in favorites)x.Add(new XElement("favorite",f));foreach(var alias in categoryAliases)x.Add(new XElement("alias",new XAttribute("path",alias.Key),alias.Value));foreach(string c in pinnedCategories)x.Add(new XElement("pinnedCategory",c));foreach(var p in queue.Values)x.Add(new XElement("move",new XAttribute("source",p.Source),new XAttribute("target",p.Target),new XAttribute("stamp",p.Stamp)));x.Add(new XElement("renamePrefixes",renamePrefixes.Select(p=>new XElement("prefix",p))));x.Add(new XElement("categoryUsage",new XAttribute("mode","successful-batches"),categoryChoiceCounts.Select(p=>new XElement("choice",new XAttribute("path",p.Key),new XAttribute("count",p.Value)))));ConfigurationStore.Save(x,state);}
 void RenderSidebar(){
  UpdateBrowseButtons();E<TextBlock>("LibraryPath").Text=root;E<TextBlock>("LibraryPath").ToolTip=root;
  var fav=E<StackPanel>("FavoritesPanel");fav.Children.Clear();foreach(string p in favorites){string path=p;string name=Path.GetFileName(path.TrimEnd('\\'));if(name.Length==0)name=path;var b=B("▱   "+name,()=>Navigate(path));b.ToolTip=path;fav.Children.Add(b);}
  var groups=E<StackPanel>("GroupsPanel");groups.Children.Clear();groups.Children.Add(B("全部分类",()=>{group="";E<TextBox>("CategorySearch").Clear();RenderCategories();},group.Length==0));
  foreach(string c in CategoryRoots()){string g=c;groups.Children.Add(B(Clean(g),()=>{E<TextBox>("CategorySearch").Clear();group=g;RenderCategories();},group==g));}
 }
 string ShortCategory(string path){string alias;if(categoryAliases.TryGetValue(path,out alias)&&!String.IsNullOrWhiteSpace(alias))return alias;var parts=path.Split('\\');return parts.Length==1?Clean(path):String.Join(" · ",parts.Skip(1).Select(Clean));}
 bool CategoryMatches(string path,string query){return path.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||ShortCategory(path).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;}
 Button CategoryChip(string path,bool pinned,bool grouped=false){
  string category=path;string label=grouped||categoryAliases.ContainsKey(category)?ShortCategory(category):DisplayCategory(category);var b=B(label,()=>Mark(category),pinned);b.Tag=category;b.FontSize=12;b.Padding=new Thickness(9,6,9,6);b.Margin=new Thickness(0,0,5,5);b.MaxWidth=330;b.MinHeight=29;
  if(!pinned)b.Background=(Brush)new BrushConverter().ConvertFromString("#F4F4FA");
  b.Content=new TextBlock{Text=label,TextTrimming=TextTrimming.CharacterEllipsis};b.ToolTip=label+"\n完整分类："+DisplayCategory(category)+"\n目标："+Safety.Destination(root,category)+"\n左键标注 · 右键修改显示";
  var menu=new ContextMenu();var pin=new MenuItem{Header=pinned?"取消置顶":"★ 置顶此分类"};pin.Click+=delegate{TogglePin(category);};menu.Items.Add(pin);
  if(pinned){var first=new MenuItem{Header="移到置顶区最前面"};first.Click+=delegate{pinnedCategories.RemoveAll(c=>String.Equals(c,category,StringComparison.OrdinalIgnoreCase));pinnedCategories.Insert(0,category);Save();RenderCategories();};menu.Items.Add(first);}
  var aliasItem=new MenuItem{Header="设置显示简称…"};aliasItem.Click+=delegate{EditCategoryAlias(category);};menu.Items.Add(aliasItem);
  if(categoryAliases.ContainsKey(category)){var reset=new MenuItem{Header="恢复默认名称"};reset.Click+=delegate{SetCategoryAlias(category,"");};menu.Items.Add(reset);}
  b.ContextMenu=menu;return b;
 }
 void TogglePin(string category){if(pinnedCategories.Contains(category,StringComparer.OrdinalIgnoreCase))pinnedCategories.RemoveAll(c=>String.Equals(c,category,StringComparison.OrdinalIgnoreCase));else pinnedCategories.Add(category);Save();RenderCategories();Toast("置顶设置已保存，不影响分类目标。");}
 void SetCategoryAlias(string category,string alias){alias=alias.Trim();if(alias.IndexOfAny(new[]{'\r','\n','\t'})>=0)throw new Exception("简称不能包含换行或制表符。");if(alias.Length==0)categoryAliases.Remove(category);else categoryAliases[category]=alias;Save();RenderCategories();Toast("显示简称已保存，文件夹名称和移动目标保持不变。");}
 void EditCategoryAlias(string category){
  string alias;categoryAliases.TryGetValue(category,out alias);var d=Dialog("设置显示简称",560,240);var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text=DisplayCategory(category),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),Foreground=Brushes.SlateGray});var input=new TextBox{Text=alias??"",Tag="例如：日剧、3A、游戏图（留空恢复默认）",Margin=new Thickness(0,0,0,12)};panel.Children.Add(input);panel.Children.Add(B("保存简称",()=>{SetCategoryAlias(category,input.Text);d.Close();},true));d.Content=panel;d.Loaded+=delegate{input.Focus();input.SelectAll();};d.ShowDialog();
 }
 void ApplyCategoryPreferences(List<CategoryPreference> settings){
  var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var s in settings){string alias=(s.Alias??"").Trim();if(alias.IndexOfAny(new[]{'\r','\n','\t'})>=0)throw new Exception("简称不能包含换行或制表符。");if(alias.Length>0)aliases[s.Path]=alias;}
  categoryAliases=aliases;pinnedCategories=settings.Where(s=>s.Pinned).OrderBy(s=>s.Order).Select(s=>s.Path).ToList();Save();RenderCategories();
 }
 void EditCategoryPreferences(){
  var settings=AvailableCategoryPaths().Select(c=>{string alias;categoryAliases.TryGetValue(c,out alias);int order=pinnedCategories.FindIndex(p=>String.Equals(p,c,StringComparison.OrdinalIgnoreCase));return new CategoryPreference{Path=c,Alias=alias??"",Pinned=order>=0,Order=order>=0?order+1:99};}).ToList();
  var d=Dialog("分类显示 · 简称与置顶",850,630);var panel=new DockPanel{Margin=new Thickness(24)};var note=new TextBlock{Text="直接填写简称，勾选高频分类置顶；顺序数字越小越靠前。\n这些设置只改变按钮显示，不会重命名目录或改变已经标注的移动目标。",Foreground=Brushes.SlateGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);
  var grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,RowHeaderWidth=0,Background=Brushes.White,BorderThickness=new Thickness(0),ItemsSource=settings,RowHeight=36};
  grid.Columns.Add(new DataGridTextColumn{Header="完整分类（只读）",Binding=new System.Windows.Data.Binding("Path"),IsReadOnly=true,Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="显示简称",Binding=new System.Windows.Data.Binding("Alias"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridCheckBoxColumn{Header="置顶",Binding=new System.Windows.Data.Binding("Pinned"),Width=65});
  grid.Columns.Add(new DataGridTextColumn{Header="顺序",Binding=new System.Windows.Data.Binding("Order"){ValidatesOnExceptions=true},Width=65});
  var save=B("保存显示设置",()=>{if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))throw new Exception("请检查输入，顺序需要填写整数。");ApplyCategoryPreferences(settings);d.Close();Toast("分类显示设置已保存。");},true);save.Margin=new Thickness(0,16,0,0);DockPanel.SetDock(save,Dock.Bottom);panel.Children.Add(save);panel.Children.Add(grid);d.Content=panel;d.ShowDialog();
 }
 bool InLibrary(string path){return !String.IsNullOrEmpty(path)&&(String.Equals(Path.GetFullPath(path).TrimEnd('\\'),Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||Safety.Under(path,root));}
 void UpdateBrowseButtons(){
  bool library=InLibrary(folder);E<TextBlock>("CurrentNavText").Text=library?"返回来源":"当前来源";
  E<Button>("CurrentButton").Style=library?(Style)Window.Resources[typeof(Button)]:(Style)Window.Resources["Soft"];
  E<Button>("BrowseLibraryButton").Style=library?(Style)Window.Resources["Soft"]:(Style)Window.Resources[typeof(Button)];
  E<Button>("CurrentButton").ToolTip=String.IsNullOrEmpty(sourceFolder)?"返回上次来源目录":sourceFolder;
  E<Button>("BrowseLibraryButton").ToolTip=!String.IsNullOrEmpty(libraryFolder)&&InLibrary(libraryFolder)&&Directory.Exists(libraryFolder)?libraryFolder:root;
 }
 async void BrowseSource(){await BrowseSourceAsync();}
 async Task BrowseSourceAsync(){if(moving)return;if(!Directory.Exists(sourceFolder)){Toast("上次来源目录已不存在，请点击“选择来源”重新选择。");return;}await NavigateAsync(sourceFolder);}
 async void BrowseLibrary(){await BrowseLibraryAsync();}
 async Task BrowseLibraryAsync(){if(moving)return;string target=!String.IsNullOrEmpty(libraryFolder)&&InLibrary(libraryFolder)&&Directory.Exists(libraryFolder)?libraryFolder:root;if(!Directory.Exists(target)){Toast("资料库目录不存在，请先选择或创建资料库。");return;}await NavigateAsync(target);}
 async void Navigate(string path){await NavigateAsync(path);}
 async Task NavigateAsync(string path){
  int version=++generation;try{
   string absolute=Path.GetFullPath(path);Safety.NoLinks(absolute);Toast("正在读取目录…");
   var entries=await Task.Run(()=>new DirectoryInfo(absolute).GetFileSystemInfos().Where(f=>(f.Attributes&FileAttributes.Directory)!=0||!IsAuxiliaryName(f.Name)).OrderByDescending(f=>(f.Attributes&FileAttributes.Directory)!=0).ThenBy(f=>f.Name).ToArray());
   if(version!=generation)return;folder=absolute;if(InLibrary(folder))libraryFolder=folder;else sourceFolder=folder;UpdateBrowseButtons();E<TextBox>("Address").Text=folder;E<TextBlock>("PageTitle").Text=InLibrary(folder)?"查看资料库":"整理文件";E<TextBox>("FileSearch").Clear();
   items=entries.Select(f=>{bool dir=(f.Attributes&FileAttributes.Directory)!=0;string ext=f.Extension.ToLowerInvariant();bool image=IsImage(ext),video=new[]{".mp4",".mkv",".mov",".avi",".webm"}.Contains(ext);return new CardItem{Name=f.Name,FullPath=f.FullName,IsDirectory=dir,Kind=dir?"文件夹":image?"图片":video?"视频":ext.TrimStart('.').ToUpperInvariant()+" 文件",Subtitle=dir?"文件夹 · 整体移动":Size(((FileInfo)f).Length)+" · "+f.LastWriteTime.ToString("yyyy.MM.dd"),Glyph=dir?"\uE8B7":image?"\uEB9F":video?"\uE714":"\uE8A5",TileColor=dir?"#EEEAFB":image?"#E9F2F3":video?"#E9EDFA":"#F2EEF5",Accent=dir?"#AD96D7":image?"#8FBBBE":video?"#94A2D3":"#B2A1BC"};}).ToList();
   RefreshAssignments();Save();Toast(hiddenPending>0?"已隐藏辅助文件，并取消其 "+hiddenPending+" 项旧标注；原文件未改动。":"已隐藏常见辅助文件 · 单击分类即可标注。");hiddenPending=0;thumbnails=LoadThumbnails(items.Take(400).ToList(),version);
  }catch(Exception e){if(version==generation){Toast("读取失败："+e.Message);E<TextBox>("Address").Text=folder;}}
 }
 static string Size(long bytes){if(bytes<1024)return bytes+" B";if(bytes<1048576)return(bytes/1024d).ToString("0.#")+" KB";if(bytes<1073741824)return(bytes/1048576d).ToString("0.#")+" MB";return(bytes/1073741824d).ToString("0.##")+" GB";}
 static bool IsAuxiliaryName(string name){return name.StartsWith("~$",StringComparison.OrdinalIgnoreCase)||new[]{"desktop.ini","thumbs.db","ehthumbs.db",".ds_store"}.Contains(name,StringComparer.OrdinalIgnoreCase);}
 static bool IsImage(string ext){return new[]{".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff"}.Contains(ext.ToLowerInvariant());}
 async Task LoadThumbnails(List<CardItem> current,int version){
  foreach(var item in current){if(version!=generation)return;try{var image=await Task.Run(()=>ReadThumbnail(item));if(version!=generation)return;item.Thumbnail=image;}catch{}}
 }
 static ImageSource ReadThumbnail(CardItem item){
  string path=item.FullPath;Safety.NoLinks(path);
  if(item.IsDirectory){path=Directory.EnumerateFiles(path).Take(150).Where(f=>IsImage(Path.GetExtension(f))).OrderBy(f=>new[]{"cover","folder","poster"}.Contains(Path.GetFileNameWithoutExtension(f).ToLowerInvariant())?0:1).FirstOrDefault();}
  if(path==null||!IsImage(Path.GetExtension(path)))return null;Safety.NoLinks(path);
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=360;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
 }
 void Filter(){if(cards==null)return;string q=E<TextBox>("FileSearch").Text;cards.ItemsSource=items.Where(i=>i.Name.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToList();E<TextBlock>("CountLabel").Text=cards.Items.Count+" 个项目 · "+Path.GetFileName(folder.TrimEnd('\\'));E<StackPanel>("EmptyState").Visibility=cards.Items.Count==0?Visibility.Visible:Visibility.Collapsed;E<TextBlock>("EmptyText").Text=q.Length==0?"这里还没有文件":"没有匹配的项目";UpdateSelection();}
 void RefreshAssignments(){foreach(var item in items){MovePlan p;item.Assignment=queue.TryGetValue(item.FullPath,out p)?"✓ "+DisplayCategory(RelativeCategory(p.Target)):"未标注";}Filter();}
 string RelativeCategory(string target){string parent=Path.GetDirectoryName(target);return Safety.Under(parent,root)?parent.Substring(root.TrimEnd('\\').Length+1):parent;}
 void UpdateSelection(){
  var selected=cards.SelectedItems.Cast<CardItem>().ToList();foreach(var item in items)item.SelectionBadge=selected.Contains(item)?"✓":"○";
  E<TextBlock>("Status").Text="已选 "+selected.Count+" 项   ·   待移动 "+queue.Count+" 项";E<TextBlock>("QueueNavText").Text="待移动清单   "+queue.Count;
  E<TextBlock>("DetailName").Text=selected.Count==0?"选择文件或文件夹":selected.Count==1?selected[0].Name:"已选择 "+selected.Count+" 个项目";
  E<TextBlock>("DetailInfo").Text=selected.Count==0?"点击卡片查看信息，框选后可批量标注。":selected.Count==1?selected[0].Subtitle+"\n"+selected[0].Assignment:"点击下方分类，将同时标注这些项目。文件夹保持完整。";
  E<Button>("OpenButton").IsEnabled=selected.Count>0;E<Button>("OpenFolderButton").IsEnabled=selected.Count>0;
  E<Button>("DeleteButton").IsEnabled=selected.Count>0;E<Button>("ToolbarDeleteButton").IsEnabled=selected.Count>0;E<Button>("RenameButton").IsEnabled=cards.Items.Count>0;
 }
 void Mark(string category){
  var selected=cards.SelectedItems.Cast<CardItem>().ToList();if(selected.Count==0){Toast("请先在中间选择或框选项目。");return;}
  var additions=new List<MovePlan>();foreach(var item in selected){string src=item.FullPath;if(String.Equals(src,root,StringComparison.OrdinalIgnoreCase)||Safety.Under(root,src))throw new Exception("不能移动资料库本身或其父目录。");if(queue.Keys.Any(k=>Safety.Under(k,src)||Safety.Under(src,k)))throw new Exception("父文件夹和内部项目不能同时标注。");additions.Add(new MovePlan{Source=src,Target=Path.Combine(Safety.Destination(root,category),Path.GetFileName(src)),Stamp=Safety.Stamp(src)});}
  foreach(var p in additions)queue[p.Source]=p;Save();foreach(var item in selected)item.Assignment="✓ "+DisplayCategory(category);UpdateSelection();Toast("已标注 "+selected.Count+" 项 → "+DisplayCategory(category));
 }
 void Unmark(){foreach(CardItem item in cards.SelectedItems) {queue.Remove(item.FullPath);item.Assignment="未标注";}Save();UpdateSelection();Toast("已取消所选标注，原文件未改变。");}
 void OpenSelected(){var selected=cards.SelectedItems.Cast<CardItem>().ToList();if(selected.Count==0)return;if(selected.Count>10&&!Confirm("将使用默认应用打开 "+selected.Count+" 个项目，是否继续？"))return;int done=0;var errors=new List<string>();foreach(var item in selected){try{using(var process=System.Diagnostics.Process.Start(OpenActions.DefaultApplication(item.FullPath))){}done++;}catch(Exception e){errors.Add(item.Name+"："+e.Message);}}Toast("已请求用默认应用打开 "+done+" 个项目。");if(errors.Count>0)MessageBox.Show(Window,String.Join("\n",errors),"部分项目未打开");}
 async void OpenCurrentFolder(){
  string path=folder;var button=E<Button>("ExplorerButton");button.IsEnabled=false;
  try{if(!Directory.Exists(path))throw new DirectoryNotFoundException("当前目录已不存在，请刷新或重新选择来源。");await OpenActions.OpenFolder(path);Toast("已在资源管理器打开当前目录："+path);}
  catch(Exception e){MessageBox.Show(Window,e.Message,"无法打开当前目录");}
  finally{button.IsEnabled=true;}
 }
 async void OpenSelectedFolders(){try{var paths=OpenActions.Folders(cards.SelectedItems.Cast<CardItem>().Select(i=>i.FullPath));if(paths.Length==0)return;if(paths.Length>10&&!Confirm("将打开并置顶 "+paths.Length+" 个文件夹窗口，是否继续？"))return;E<Button>("OpenFolderButton").IsEnabled=false;int done=0;var errors=new List<string>();foreach(string path in paths){try{await OpenActions.OpenFolder(path);done++;}catch(Exception e){errors.Add(e.Message);}}Toast("已打开并置顶 "+done+" 个文件夹窗口；退出整理助手时解除本次置顶。");if(errors.Count>0)MessageBox.Show(Window,String.Join("\n",errors),"文件夹打开结果");}catch(Exception e){MessageBox.Show(Window,e.Message,"无法打开文件夹");}finally{if(Window.IsLoaded)UpdateSelection();}}
 async void PrepareRecycle(){
  var selection=cards.SelectedItems.Cast<CardItem>().Select(i=>i.FullPath).ToList();if(selection.Count==0||moving)return;
  try{moving=true;((UIElement)Window.Content).IsEnabled=false;Toast("正在核对待删除项目及文件夹内容…");var plans=await Task.Run(()=>selection.Select(p=>RecycleSafety.Capture(p,root,state)).ToList());moving=false;((UIElement)Window.Content).IsEnabled=true;RecycleDialog(plans).ShowDialog();}
  catch(Exception e){MessageBox.Show(Window,e.Message,"无法准备删除");}
  finally{moving=false;((UIElement)Window.Content).IsEnabled=true;}
 }
 Window RecycleDialog(List<RecyclePlan> plans){
  var d=Dialog("删除预览 · 移到回收站",950,610);var panel=new DockPanel{Margin=new Thickness(24)};
  var heading=new StackPanel{Margin=new Thickness(0,0,0,18)};heading.Children.Add(new TextBlock{Text="核对后再删除",FontSize=24,FontWeight=FontWeights.Bold});heading.Children.Add(new TextBlock{Text="共 "+plans.Count+" 个所选项目，其中 "+plans.Count(p=>p.IsDirectory)+" 个文件夹。文件夹内部内容将一起进入系统回收站。\n不提供永久删除；Windows 无法回收时停止。可在系统回收站中还原。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,10,0,0)});DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
  var bottom=new StackPanel{Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);
  var progress=new TextBlock{Text="受影响的待移动标注会在回收成功后取消。",Foreground=Brushes.SlateGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};bottom.Children.Add(progress);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=B("取消",()=>d.Close());cancel.IsCancel=true;cancel.IsDefault=true;buttons.Children.Add(cancel);
  var execute=new Button{Name="RecycleConfirm",Content="确认",IsEnabled=true,IsDefault=false,Background=(Brush)new BrushConverter().ConvertFromString("#B95062"),Foreground=Brushes.White};buttons.Children.Add(execute);bottom.Children.Add(buttons);panel.Children.Add(bottom);
  var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,RowHeaderWidth=0,Background=Brushes.White,BorderThickness=new Thickness(0),ItemsSource=plans,MinRowHeight=44,ColumnHeaderHeight=34,FontSize=13};
  foreach(var col in new[]{new[]{"完整路径","Path"},new[]{"类型","Kind"},new[]{"包含内容","Summary"}}){var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(6,7,6,7)));textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding(col[1])));grid.Columns.Add(new DataGridTextColumn{Header=col[0],Binding=new System.Windows.Data.Binding(col[1]),Width=new DataGridLength(col[1]=="Path"?480:col[1]=="Kind"?195:175),ElementStyle=textStyle});}
  panel.Children.Add(grid);d.Content=new System.Windows.Controls.Border{Background=d.Background,Child=panel};
  d.Loaded+=delegate{cancel.Focus();};d.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter)e.Handled=true;};d.Closing+=delegate(object sender,CancelEventArgs e){if(moving)e.Cancel=true;};
  execute.Click+=async delegate{if(moving)return;moving=true;execute.IsEnabled=false;cancel.IsEnabled=false;RecycleResult result;var update=new Progress<string>(text=>progress.Text=text);try{result=await RecycleSafety.Sta(()=>ExecuteRecycle(plans,update));}catch(Exception e){result=new RecycleResult{Success=false,Message=e.Message};}finally{moving=false;}d.Close();await NavigateAsync(folder);if(result.Success)Toast("已将 "+plans.Count+" 项移到回收站。");else MessageBox.Show(Window,result.Message,result.Message.Contains("配置保存失败")?"删除结果 · 配置保存失败":"删除未全部完成");};
  return d;
 }
 RecycleResult ExecuteRecycle(List<RecyclePlan> plans,IProgress<string> progress){
  int done=0;string error="";string log=Path.Combine(Program.Home,"回收日志-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".xml");var journal=new XElement("recycleBatch");
  try{Save();foreach(var p in plans)RecycleSafety.Check(p,root,state);journal.Save(log);foreach(var p in plans){var entry=new XElement("item",new XAttribute("source",p.Path),new XAttribute("status","准备回收"));journal.Add(entry);journal.Save(log);if(progress!=null)progress.Report("正在移到回收站 "+(done+1)+" / "+plans.Count+"："+Path.GetFileName(p.Path));string recycled=RecycleNative.Send(p,root,state);done++;entry.SetAttributeValue("status","已移到回收站");entry.SetAttributeValue("recycledPath",recycled);journal.Save(log);foreach(string k in queue.Keys.Where(k=>String.Equals(k,p.Path,StringComparison.OrdinalIgnoreCase)||Safety.Under(k,p.Path)||Safety.Under(p.Path,k)).ToList())queue.Remove(k);Save();}}
  catch(Exception e){error=(e is ConfigurationSaveException?"\n文件操作已完成 "+done+" 项，但保存配置失败：":"\n后续操作已停止：")+e.Message;try{journal.SetAttributeValue("error",e.Message);journal.Save(log);}catch{}}
  return new RecycleResult{Success=done==plans.Count&&error.Length==0,Message="已移到回收站 "+done+" / "+plans.Count+" 项。"+error+"\n需要恢复时，请打开 Windows 回收站还原。\n日志："+log};
 }
 static ListBoxItem FindItem(DependencyObject d){while(d!=null){if(d is ListBoxItem)return(ListBoxItem)d;d=d is Visual||d is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):null;}return null;}
 void BeginMarquee(object sender,MouseButtonEventArgs e){
  if(FindItem(e.OriginalSource as DependencyObject)!=null)return;
  for(var p=e.OriginalSource as DependencyObject;p!=null;p= p is Visual?VisualTreeHelper.GetParent(p):null)if(p is System.Windows.Controls.Primitives.ScrollBar)return;
  var viewport=E<Grid>("CardsViewport");dragStart=e.GetPosition(viewport);dragging=true;dragAdd=(Keyboard.Modifiers&ModifierKeys.Control)!=0;previousSelection=new HashSet<CardItem>(cards.SelectedItems.Cast<CardItem>());if(!dragAdd)cards.SelectedItems.Clear();marqueeScroll=FindScroll(cards);dragOffset=marqueeScroll==null?0:marqueeScroll.VerticalOffset;viewport.CaptureMouse();marqueeTimer.Start();e.Handled=true;
 }
 void MoveMarquee(object sender,MouseEventArgs e){
  if(!dragging)return;UpdateMarquee();
 }
 void UpdateMarquee(){
  var viewport=E<Grid>("CardsViewport");Point end=Mouse.GetPosition(viewport);end.X=Math.Max(0,Math.Min(viewport.ActualWidth,end.X));end.Y=Math.Max(0,Math.Min(viewport.ActualHeight,end.Y));var start=dragStart;if(marqueeScroll!=null)start.Y-=marqueeScroll.VerticalOffset-dragOffset;var rect=new Rect(start,end);var marquee=E<System.Windows.Controls.Border>("Marquee");marquee.Visibility=Visibility.Visible;Canvas.SetLeft(marquee,rect.X);Canvas.SetTop(marquee,rect.Y);marquee.Width=rect.Width;marquee.Height=rect.Height;
  SelectRectangle(rect,dragAdd,previousSelection);
 }
 void SelectRectangle(Rect rect,bool add,HashSet<CardItem> prior){var viewport=E<Grid>("CardsViewport");foreach(CardItem item in cards.Items){var element=cards.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;if(element==null)continue;Rect bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(),element.RenderSize));bool hit=rect.IntersectsWith(bounds)||(add&&prior.Contains(item));if(hit&&!cards.SelectedItems.Contains(item))cards.SelectedItems.Add(item);else if(!hit&&cards.SelectedItems.Contains(item))cards.SelectedItems.Remove(item);}}
 static ScrollViewer FindScroll(DependencyObject parent){if(parent is ScrollViewer)return (ScrollViewer)parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=FindScroll(VisualTreeHelper.GetChild(parent,i));if(found!=null)return found;}return null;}
 void ScrollMarquee(){
  if(!dragging)return;if(Mouse.LeftButton!=MouseButtonState.Pressed){StopMarquee();return;}
  var viewport=E<Grid>("CardsViewport");var position=Mouse.GetPosition(viewport);
  if(marqueeScroll!=null){double delta=position.Y<48?-Math.Min(32,4+(48-position.Y)*0.45):position.Y>viewport.ActualHeight-48?Math.Min(32,4+(position.Y-viewport.ActualHeight+48)*0.45):0;if(delta!=0){marqueeScroll.ScrollToVerticalOffset(marqueeScroll.VerticalOffset+delta);cards.UpdateLayout();}}
  UpdateMarquee();
 }
 void StopMarquee(){dragging=false;marqueeTimer.Stop();E<System.Windows.Controls.Border>("Marquee").Visibility=Visibility.Collapsed;var viewport=E<Grid>("CardsViewport");if(viewport.IsMouseCaptured)viewport.ReleaseMouseCapture();}
 void EndMarquee(object sender,MouseButtonEventArgs e){if(!dragging)return;UpdateMarquee();StopMarquee();e.Handled=true;}
 void ChooseSource(){using(var d=new Forms.FolderBrowserDialog{SelectedPath=folder,Description="选择待整理的文件夹"})if(d.ShowDialog()==Forms.DialogResult.OK)Navigate(d.SelectedPath);}
 void AddFavorite(){if(!favorites.Contains(folder,StringComparer.OrdinalIgnoreCase))favorites.Add(folder);Save();RenderSidebar();Toast("已收藏当前来源，下次可以直接打开。");}
 Window Dialog(string title,double width=660,double height=440){return new Window{Title=title,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=width,Height=height,Background=(Brush)new BrushConverter().ConvertFromString("#F5F6FA"),FontFamily=Window.FontFamily,FontSize=13,Foreground=Window.Foreground,Resources=Window.Resources};}
 void Editor(string title,string note,string value,Action<string> save){var d=Dialog(title);var panel=new DockPanel{Margin=new Thickness(24)};var hint=new TextBlock{Text=note,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,15),Foreground=Brushes.SlateGray};DockPanel.SetDock(hint,Dock.Top);panel.Children.Add(hint);var input=new TextBox{Text=value,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};var button=B("保存",()=>{save(input.Text);d.Close();},true);DockPanel.SetDock(button,Dock.Bottom);button.Margin=new Thickness(0,16,0,0);panel.Children.Add(button);panel.Children.Add(input);d.Content=panel;d.ShowDialog();}
 void EditFavorites(){Editor("管理快捷来源","每行一个完整目录路径，可添加、删除或调整顺序。只修改快捷入口。",String.Join(Environment.NewLine,favorites),text=>{var values=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(v=>v.Trim()).ToList();if(values.Any(v=>!Path.IsPathRooted(v)))throw new Exception("请输入完整绝对路径。");favorites=values.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();Save();RenderSidebar();});}
 void EditCategories(){Editor("编辑分类框架","每行一条分类路径，用 \\ 分隔层级。修改入口不会移动或删除旧目录；已有标注保留原目标。",String.Join(Environment.NewLine,categories),text=>{var values=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(v=>v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(values.Count==0)throw new Exception("至少保留一个分类。");foreach(string c in values)Safety.Destination(root,c);categories=values;group="";Save();RenderSidebar();RenderCategories();});}
 void LibraryDialog(){
  if(queue.Count>0){MessageBox.Show(Window,"请先执行或清空标注，再更换资料库。","归位");return;}
  var d=Dialog("选择 / 新建资料库",690,360);var panel=new StackPanel{Margin=new Thickness(26)};panel.Children.Add(new TextBlock{Text="选择位置，或输入尚未存在的完整路径",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,17)});var input=new TextBox{Text=root,Margin=new Thickness(0,0,0,12)};panel.Children.Add(input);panel.Children.Add(B("浏览文件夹",()=>{using(var picker=new Forms.FolderBrowserDialog{SelectedPath=Directory.Exists(input.Text)?input.Text:root})if(picker.ShowDialog()==Forms.DialogResult.OK)input.Text=picker.SelectedPath;}));var sub=new CheckBox{Content="在此位置下创建“资料库”子文件夹",Margin=new Thickness(0,12,0,10)};var all=new CheckBox{Content="创建全部框架空目录（已有内容不覆盖）",IsChecked=true,Margin=new Thickness(0,0,0,18)};panel.Children.Add(sub);panel.Children.Add(all);panel.Children.Add(B("使用此资料库",()=>{if(!Path.IsPathRooted(input.Text))throw new Exception("请输入完整绝对路径。");string selected=Path.GetFullPath(input.Text);if(sub.IsChecked==true)selected=Path.Combine(selected,"资料库");Safety.NoLinks(selected);if(all.IsChecked==true)Organizer.CreateFramework(selected,categories);else Directory.CreateDirectory(selected);root=selected;Save();RenderSidebar();d.Close();Toast("资料库已设置。");},true));d.Content=panel;d.ShowDialog();
 }
 List<string> Validate(List<MovePlan> plans){var result=new List<string>();var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in plans){try{Safety.Check(p);if(!targets.Add(p.Target))throw new Exception("多个项目指向同名目标");if(plans.Any(q=>q!=p&&(Safety.Under(p.Source,q.Source)||Safety.Under(p.Target,q.Source)||Safety.Under(q.Target,p.Target)||Safety.Under(p.Target,q.Target))))throw new Exception("项目存在嵌套或目标依赖，请分批移动");result.Add(null);}catch(Exception e){result.Add(e.Message);}}return result;}
 void Preview(){
  if(queue.Count==0){Toast("先选择项目并单击分类，清单会出现在这里。");return;}
  var plans=queue.Values.ToList();var checks=Validate(plans);var d=Dialog("预览移动清单",1040,650);var panel=new DockPanel{Margin=new Thickness(25)};var title=new TextBlock{Text="确认后，文件才会移动",FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
  var bottom=new StackPanel{Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);var progress=new TextBlock{Text="跨盘逐项复制、校验后删除源文件；同名不覆盖。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};bottom.Children.Add(progress);var execute=new Button{Content="确认移动 "+plans.Count+" 项",Style=(Style)Window.Resources["Primary"],HorizontalAlignment=HorizontalAlignment.Right,IsEnabled=checks.All(c=>c==null)};bottom.Children.Add(execute);panel.Children.Add(bottom);
  var grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Background=Brushes.White,BorderThickness=new Thickness(0),RowHeaderWidth=0,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,HorizontalGridLinesBrush=Brushes.WhiteSmoke,ColumnWidth=new DataGridLength(1,DataGridLengthUnitType.Star)};
  foreach(var column in new[]{new[]{"原位置","Source"},new[]{"目标位置","Target"},new[]{"检查结果","Result"}})grid.Columns.Add(new DataGridTextColumn{Header=column[0],Binding=new System.Windows.Data.Binding(column[1])});
  grid.ItemsSource=plans.Select((p,i)=>new{p.Source,p.Target,Result=checks[i]??(Safety.CrossDisk(p)?"跨盘 · 复制并校验":"同盘 · 直接移动")}).ToList();panel.Children.Add(grid);d.Content=panel;
  d.Closing+=delegate(object s,CancelEventArgs e){if(moving)e.Cancel=true;};
  execute.Click+=async delegate{if(moving)return;moving=true;execute.IsEnabled=false;grid.IsEnabled=false;var update=new Progress<string>(text=>progress.Text=text);string report;bool succeeded=false;try{report=await Task.Run(()=>Execute(plans,update,out succeeded));}catch(Exception e){report=e.Message;}finally{moving=false;}d.Close();await NavigateAsync(folder);RenderCategories();if(succeeded)Toast("已移动 "+plans.Count+" 项，移动日志已自动保存。");else MessageBox.Show(Window,report,"移动未全部完成");};d.ShowDialog();
 }
 string Execute(List<MovePlan> plans,IProgress<string> progress){bool succeeded;return Execute(plans,progress,out succeeded);}
 string Execute(List<MovePlan> plans,IProgress<string> progress,out bool succeeded){int done=0;string error="";string log=Path.Combine(Program.Home,"移动日志-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".xml");var journal=new XElement("batch");var countedDestinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);try{Save();var failures=Validate(plans);if(failures.Any(f=>f!=null))throw new Exception(String.Join("\n",failures.Where(f=>f!=null)));journal.Save(log);foreach(var p in plans){var entry=new XElement("move",new XAttribute("source",p.Source),new XAttribute("target",p.Target),new XAttribute("status","准备移动"));journal.Add(entry);journal.Save(log);if(progress!=null)progress.Report("处理 "+(done+1)+" / "+plans.Count+"："+Path.GetFileName(p.Source));if(Safety.CrossDisk(p))Safety.CrossMove(p,text=>{if(progress!=null)progress.Report(text);});else Safety.Move(p);done++;queue.Remove(p.Source);string destination=Path.GetFullPath(Path.GetDirectoryName(p.Target));if(countedDestinations.Add(destination))RecordSuccessfulCategoryMove(destination);entry.SetAttributeValue("status","已完成");journal.Save(log);Save();}}catch(Exception e){error=(e is ConfigurationSaveException?"\n文件操作已完成 "+done+" 项，但保存配置失败：":"\n后续操作已停止：")+e.Message;try{journal.SetAttributeValue("error",e.Message);journal.Save(log);}catch{}}succeeded=error.Length==0;return"已移动 "+done+" / "+plans.Count+" 项。"+error+"\n日志："+log;}
 public async Task SmokeTest(bool includeRecycle=true){
  string source=Path.Combine(Program.Home,"示例来源");Directory.CreateDirectory(source);
  string[] names={"山间旅行","海边日落","城市散步","旅行相册","截图收藏","夏日片段","阅读资料","游戏录屏"};
  for(int i=0;i<names.Length;i++){string path=Path.Combine(source,names[i]);Directory.CreateDirectory(path);if(i<5)using(var bitmap=new System.Drawing.Bitmap(360,440))using(var g=System.Drawing.Graphics.FromImage(bitmap)){g.Clear(System.Drawing.Color.FromArgb(225-i*8,228-i*7,245-i*5));using(var sun=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(248,224,177)))g.FillEllipse(sun,230,55,78,78);using(var mountain=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(143-i*6,152-i*3,188-i*6)))g.FillPolygon(mountain,new[]{new System.Drawing.Point(0,320),new System.Drawing.Point(115,155),new System.Drawing.Point(290,440),new System.Drawing.Point(0,440)});using(var mountain=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(172-i*6,180-i*3,207-i*4)))g.FillPolygon(mountain,new[]{new System.Drawing.Point(150,440),new System.Drawing.Point(300,230),new System.Drawing.Point(360,300),new System.Drawing.Point(360,440)});bitmap.Save(Path.Combine(path,"cover.png"),System.Drawing.Imaging.ImageFormat.Png);}}
  await NavigateAsync(source);await thumbnails;Window.UpdateLayout();
  var viewport=E<Grid>("CardsViewport");var first=(ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(items[0]);var second=(ListBoxItem)cards.ItemContainerGenerator.ContainerFromItem(items[1]);var bounds=first.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(),first.RenderSize));bounds.Union(second.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(),second.RenderSize)));SelectRectangle(bounds,false,new HashSet<CardItem>());if(cards.SelectedItems.Count!=2)throw new Exception("Marquee selection failed");
  string photoCategory="图片\\风景",shotCategory="视频\\片段";
  var preferences=categories.Select(c=>new CategoryPreference{Path=c,Alias=c==photoCategory?"风景图":c==shotCategory?"游戏图":"",Pinned=c==photoCategory||c==shotCategory,Order=c==shotCategory?1:2}).ToList();ApplyCategoryPreferences(preferences);
  if(pinnedCategories[0]!=shotCategory||ShortCategory(photoCategory)!="风景图")throw new Exception("Alias or pin ordering failed");
  E<TextBox>("CategorySearch").Text="风景图";if(E<WrapPanel>("PinnedPanel").Children.Count!=1)throw new Exception("Alias search failed");E<TextBox>("CategorySearch").Clear();
  group="02_动画";RenderCategories();if(E<WrapPanel>("PinnedPanel").Children.Count!=2)throw new Exception("Pins must remain visible across groups");group="";RenderCategories();
  var pinnedChip=E<WrapPanel>("PinnedPanel").Children.Cast<Button>().First(b=>(string)b.Tag==photoCategory);pinnedChip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  if(queue.Values.Any(p=>p.Target!=Path.Combine(root,photoCategory,Path.GetFileName(p.Source))))throw new Exception("Alias changed move target");
  TogglePin(photoCategory);if(pinnedCategories.Contains(photoCategory))throw new Exception("Unpin failed");TogglePin(photoCategory);AddFavorite();
  if(queue.Count!=2||!Directory.Exists(items[0].FullPath)||items.Count(i=>i.Thumbnail!=null)!=5)throw new Exception("Selection / thumbnails / marking failed");
  string openFixture=Path.Combine(source,"打开测试.txt");File.WriteAllText(openFixture,"default-app-test");var launch=OpenActions.DefaultApplication(openFixture);if(!launch.UseShellExecute||launch.FileName!=openFixture)throw new Exception("Default app routing failed");if(OpenActions.Folders(new[]{openFixture,openFixture,source}).Length!=1)throw new Exception("Folder deduplication failed");if(!E<Button>("OpenButton").IsEnabled||!E<Button>("OpenFolderButton").IsEnabled||!E<Button>("DeleteButton").IsEnabled)throw new Exception("Selected action buttons unavailable");
  string browseChild=Path.Combine(root,"浏览切换测试");Directory.CreateDirectory(browseChild);var queuedBeforeBrowse=queue.Values.Select(p=>p.Source+"|"+p.Target).OrderBy(p=>p).ToArray();
  await BrowseLibraryAsync();if(folder!=root||sourceFolder!=source||E<TextBlock>("CurrentNavText").Text!="返回来源")throw new Exception("Library quick navigation failed");
  await NavigateAsync(browseChild);await BrowseSourceAsync();if(folder!=source)throw new Exception("Return source failed");
  await BrowseLibraryAsync();if(folder!=browseChild)throw new Exception("Library subfolder was not remembered");
  await BrowseSourceAsync();Directory.Delete(browseChild);await BrowseLibraryAsync();if(folder!=root)throw new Exception("Missing library subfolder fallback failed");
  await BrowseSourceAsync();if(folder!=source||E<TextBlock>("CurrentNavText").Text!="当前来源"||!queue.Values.Select(p=>p.Source+"|"+p.Target).OrderBy(p=>p).SequenceEqual(queuedBeforeBrowse))throw new Exception("Browsing changed source or queued moves");
  cards.SelectedItems.Add(items[0]);cards.SelectedItems.Add(items[1]);await thumbnails;Window.UpdateLayout();
  var deletePreview=RecycleDialog(new List<RecyclePlan>{RecycleSafety.Capture(openFixture,root,state)});var deleteConfirm=(Button)FindNamed(deletePreview,"RecycleConfirm");if(!deleteConfirm.IsEnabled||(string)deleteConfirm.Content!="确认"||FindNamed(deletePreview,"RecycleAcknowledge")!=null)throw new Exception("Two-step delete preview failed");deletePreview.Close();if(!File.Exists(openFixture))throw new Exception("Delete preview changed source");
  var handle=new System.Windows.Interop.WindowInteropHelper(Window).Handle;OpenActions.Pin(handle);if(!OpenActions.IsTopmost(handle))throw new Exception("Topmost failed");OpenActions.ReleasePins();if(OpenActions.IsTopmost(handle))throw new Exception("Topmost cleanup failed");
  E<Expander>("DetailsExpander").IsExpanded=true;
  Window.UpdateLayout();var visual=(FrameworkElement)Window.Content;var image=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(Program.Home,"现代版界面.png")))encoder.Save(output);
  var plans=queue.Values.ToList();if(Validate(plans).Any(e=>e!=null))throw new Exception("Preview validation failed");string report=await Task.Run(()=>Execute(plans,null));if(queue.Count!=0||plans.Any(p=>!Directory.Exists(p.Target)||Directory.Exists(p.Source)))throw new Exception("Move failed: "+report);
  Save();var restored=new ModernWindow();if(!restored.favorites.Contains(source)||restored.folder!=source)throw new Exception("State migration failed");if(restored.ShortCategory(photoCategory)!="风景图"||!restored.pinnedCategories.SequenceEqual(pinnedCategories))throw new Exception("Category preferences not restored");restored.Window.Close();
  if(restored.sourceFolder!=source||restored.libraryFolder!=root)throw new Exception("Quick navigation preferences not restored");
  await CategoryTreeSmokeTest();
  await RenameSmokeTest();
  if(includeRecycle)await RecycleTests(source);
  File.WriteAllText(Path.Combine(Program.Home,"modern-test-results.txt"),"PASS: WPF layout; thumbnails; marquee; aliases; alias search; pinned order; pins remain visible across groups; pin/unpin; alias does not change target; batch move; favorites and category preferences restored; default app launch configuration; folder deduplication; three action buttons; native topmost and cleanup."+(includeRecycle?" Delete confirmation and real recycling verified.":" Layout-only run: recycling not repeated. Rename rules, preview, collisions, file/folder renames and journal verified; date numbering: 10005 items, million boundary, existing names, extensions, repeated batches and overflow verified; recursive prefixes: scope on/off, cancellation, child-first renames and final paths verified; problem filtering, row location, full-path reports, edit transactions and skip actions verified."));
 }
 static FrameworkElement FindNamed(DependencyObject root,string name){var element=root as FrameworkElement;if(element!=null&&element.Name==name)return element;foreach(object child in LogicalTreeHelper.GetChildren(root)){var dependency=child as DependencyObject;if(dependency==null)continue;var result=FindNamed(dependency,name);if(result!=null)return result;}return null;}
 async Task RecycleTests(string source){
  Action<Action,string> reject=(action,label)=>{bool blocked=false;try{action();}catch{blocked=true;}if(!blocked)throw new Exception(label);};
  string file=Path.Combine(source,"回收测试-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(file,"recoverable-test");string directory=Path.Combine(source,"回收文件夹-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(directory,"子目录"));File.WriteAllText(Path.Combine(directory,"子目录","保留内容.txt"),"nested-test");
  var stale=RecycleSafety.Capture(file,root,state);File.AppendAllText(file,"-changed");reject(()=>RecycleSafety.Check(stale,root,state),"Changed file accepted for recycle");reject(()=>RecycleSafety.Capture(Program.Home,root,state),"Program root not protected");reject(()=>RecycleSafety.Capture(root,root,state),"Library not protected");
  var guard=new RecycleGuard();if(guard.PreDeleteItem(0,IntPtr.Zero)>=0)throw new Exception("Permanent delete not vetoed");
  var plans=new List<RecyclePlan>{RecycleSafety.Capture(file,root,state),RecycleSafety.Capture(directory,root,state)};
  var dialog=RecycleDialog(plans);var confirm=(Button)FindNamed(dialog,"RecycleConfirm");if(!confirm.IsEnabled||confirm.IsDefault||FindNamed(dialog,"RecycleAcknowledge")!=null)throw new Exception("Two-step delete confirmation failed");
  dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();dialog.UpdateLayout();var visual=(FrameworkElement)dialog.Content;var image=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"删除确认预览.png")))encoder.Save(stream);dialog.Close();if(!File.Exists(file)||!Directory.Exists(directory))throw new Exception("Preview/cancel modified originals");
  queue[file]=new MovePlan{Source=file,Target=Path.Combine(root,"图片","风景",Path.GetFileName(file)),Stamp=Safety.Stamp(file)};Save();var report=await RecycleSafety.Sta(()=>ExecuteRecycle(plans,null));
  if(!report.Success||File.Exists(file)||Directory.Exists(directory)||queue.ContainsKey(file))throw new Exception("Native recycling failed: "+report);
  var log=XElement.Load(Directory.GetFiles(Program.Home,"回收日志-*.xml").Single());var paths=log.Elements("item").Select(e=>(string)e.Attribute("recycledPath")).ToList();if(paths.Count!=2||paths.Any(p=>p==null))throw new Exception("Missing recycle confirmation");
  if(File.ReadAllText(paths[0])!="recoverable-test-changed"||File.ReadAllText(Path.Combine(paths[1],"子目录","保留内容.txt"))!="nested-test")throw new Exception("Recycle contents mismatch");
 }
}
