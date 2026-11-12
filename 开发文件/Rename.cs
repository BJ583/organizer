using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using System.Globalization;
using System.Windows.Input;

class RenameOptions {
 public bool CleanSeparators=true,RemoveSite=true,NormalizeEpisode=true,NumberAll;
 public string Title="";
 public List<string> CustomPrefixes=new List<string>();
}
class NameAssessment {
 public bool Suspect;public string Reason;
 public string Description {get{return (Suspect?"疑似乱码：":"保留：")+Reason;}}
}
static class NameDetection {
 static NameAssessment Result(bool suspect,string reason){return new NameAssessment{Suspect=suspect,Reason=reason};}
 public static NameAssessment Assess(string original){
  if(original.IndexOf('\uFFFD')>=0||original.Contains("锟斤拷"))return Result(true,"存在损坏字符");
  string name=Regex.Replace(original,@"^\s*[\[【(（]\s*(?:https?://)?(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/[^\]】)）]*)?\s*[\]】)）][\s_\-]*","",RegexOptions.IgnoreCase).Trim();
  bool siteRemoved=name!=original.Trim();
  if(!name.Any(Char.IsLetterOrDigit))return Result(true,"只有符号或空格");
  if(name.Any(c=>c>127&&Char.IsLetter(c)))return Result(false,"包含中文或其他语言文字");
  if(Regex.IsMatch(name,@"^\d{4}[-._]\d{2}[-._]\d{2}(?:[ _-]\d+)?$"))return Result(false,"已有日期编号");
  if(Regex.IsMatch(name,@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",RegexOptions.IgnoreCase)||Regex.IsMatch(name,@"^[0-9a-f]{16,}$",RegexOptions.IgnoreCase))return Result(true,"随机标识或哈希串");
  if(Regex.IsMatch(name,@"^(?:IMG|VID|DSC|DSCN|PXL|MVIMG|Screenshot|ScreenShot)[ _.-]*\d",RegexOptions.IgnoreCase)||Regex.IsMatch(name,@"(?<![a-zA-Z])(?:EP\s*\d+|S\d{1,2}E\d{1,3})(?!\d)",RegexOptions.IgnoreCase))return Result(false,"相机编号或集数格式");
  var tokens=Regex.Matches(name,@"[a-zA-Z0-9]+").Cast<Match>().Select(m=>m.Value).ToList();
  if(tokens.Any(t=>t.Length>=4&&Regex.IsMatch(t,@"^(?:[a-z]+|[A-Z][a-z]+|[A-Z]+)$")&&Regex.IsMatch(t,"[aeiouy]",RegexOptions.IgnoreCase)))return Result(false,"有可读英文词，保留完整名称");
  if(Regex.IsMatch(name,@"^[\d _.-]+$"))return siteRemoved?Result(true,"网址前缀后只有编号"):Result(false,"纯编号，含义不确定");
  string compact=Regex.Replace(name,@"[\s_\-.]","");
  if(Regex.IsMatch(compact,@"^[a-zA-Z0-9]+$")&&compact.Any(Char.IsLetter)&&compact.Any(Char.IsDigit)){
   if(Regex.IsMatch(compact,@"^(?:[a-zA-Z]{3,}\d{1,4}|[a-zA-Z]{1,3}\d{2,4}|\d{1,4}[a-zA-Z]{3,})$"))return Result(false,"可能是版本号或作品名");
   if(compact.Length>=3)return Result(true,"字母数字交错的短代码");
  }
  int transitions=0;for(int i=1;i<compact.Length;i++)if(Char.IsLetter(compact[i-1])&&Char.IsLetter(compact[i])&&Char.IsUpper(compact[i-1])!=Char.IsUpper(compact[i]))transitions++;
  if(Regex.IsMatch(compact,@"^[a-zA-Z]+$")&&((compact.Length>=6&&transitions>=4)||(Regex.IsMatch(original,@"^[\s_\-]+")&&compact.Length<=4&&transitions>0)))return Result(true,"不规则大小写随机串");
  return Result(false,"无法确定，先保留");
 }
 public static int SelectSuspects(List<RenameRow> rows){int count=0;foreach(var row in rows){row.ManualOverride=false;row.Include=String.IsNullOrEmpty(row.BlockedReason)&&Assess(row.OriginalStem).Suspect;if(row.Include)count++;else row.NewName=row.OriginalStem;}return count;}
}
class RenameRow : INotifyPropertyChanged {
 public string Source,Stamp,BlockedReason; public bool IsDirectory;
 string prefixMatch;
 public string PrefixMatch {get{return prefixMatch;}set{prefixMatch=value;Changed("Detection");}}
 public string Original {get{return Path.GetFileName(Source);}}
 public string Extension {get{return IsDirectory?"":Path.GetExtension(Source);}}
 public string LocationBase;
 public string FullLocation {get{return Path.GetDirectoryName(Source);}}
 public string Location {get{string path=FullLocation;if(String.IsNullOrEmpty(LocationBase))return path;if(String.Equals(path,LocationBase,StringComparison.OrdinalIgnoreCase))return ".";return Safety.Under(path,LocationBase)?path.Substring(LocationBase.TrimEnd('\\').Length+1):path;}}
 public string OriginalStem {get{return IsDirectory?Original:Path.GetFileNameWithoutExtension(Source);}}
 bool manualOverride;
 public bool ManualOverride {get{return manualOverride;}set{manualOverride=value;Changed("Detection");}}
 bool emptyNameFallback;
 public bool EmptyNameFallback {get{return emptyNameFallback;}set{emptyNameFallback=value;Changed("Detection");}}
 bool duplicateSuffixAdded;
 public bool DuplicateSuffixAdded {get{return duplicateSuffixAdded;}set{duplicateSuffixAdded=value;Changed("Detection");}}
 public string Detection {get{string reason=!String.IsNullOrEmpty(BlockedReason)?"跳过："+BlockedReason:ManualOverride?"手动指定：我要改名":!String.IsNullOrEmpty(PrefixMatch)?"匹配前缀："+PrefixMatch:NameDetection.Assess(OriginalStem).Description;return (DuplicateSuffixAdded?"重名已自动加编号；":"")+(EmptyNameFallback?"去前缀后为空，已补日期编号；":"")+reason;}}
 string name,status;bool include=true;
 public string NewName {get{return name;}set{name=value;Changed("NewName");}}
 public bool Include {get{return include;}set{include=value;Changed("Include");Changed("IsProblem");}}
 public string Status {get{return status;}set{status=value;Changed("Status");Changed("IsProblem");}}
 public bool IsProblem {get{return Include&&Status!=null&&Status!="可改名";}}
 public string Target {get{return Path.Combine(Path.GetDirectoryName(Source),NewName+Extension);}}
 public event PropertyChangedEventHandler PropertyChanged;
 void Changed(string property){if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(property));}
}
static class RenameRules {
 const string WebsitePattern=@"^\s*[\[【(（]\s*(?:https?://)?(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/[^\]】)）]*)?\s*[\]】)）][\s_\-]*";
 public static List<string> ParsePrefixes(string text){return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Trim()).Where(p=>p.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
 public static string MatchPrefix(string name,IEnumerable<string> prefixes){return prefixes.Where(p=>!String.IsNullOrEmpty(p)).OrderByDescending(p=>p.Length).FirstOrDefault(p=>name.StartsWith(p,StringComparison.OrdinalIgnoreCase));}
 public static string StripPrefix(string name,IEnumerable<string> prefixes){string prefix=MatchPrefix(name,prefixes);return prefix==null?name:name.Substring(prefix.Length);}
 public static string MatchWebsitePrefix(string name,IEnumerable<string> prefixes){string custom=MatchPrefix(name,prefixes);if(custom!=null)return custom;var match=Regex.Match(name,WebsitePattern,RegexOptions.IgnoreCase);return match.Success?match.Value:null;}
 public static string StripWebsitePrefix(string name,IEnumerable<string> prefixes){return Regex.Replace(StripPrefix(name,prefixes),WebsitePattern,"",RegexOptions.IgnoreCase);}
 public static DateTime Today(){return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"China Standard Time").Date;}
 public static List<string> DateNames(List<RenameRow> rows,DateTime date,IEnumerable<RenameRow> reservations=null){
  string prefix=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"_";
  var result=rows.Select(r=>r.NewName).ToList();
  var pattern=new Regex("^"+Regex.Escape(prefix)+@"(\d+)(?:\..*)?$",RegexOptions.IgnoreCase);
  foreach(var group in rows.Select((row,index)=>new {Row=row,Index=index}).Where(r=>r.Row.Include).GroupBy(r=>Path.GetDirectoryName(r.Row.Source),StringComparer.OrdinalIgnoreCase)){
   long last=0;
   var occupied=Directory.EnumerateFileSystemEntries(group.Key).Concat((reservations??Enumerable.Empty<RenameRow>()).Where(r=>r.Include&&String.Equals(Path.GetDirectoryName(r.Source),group.Key,StringComparison.OrdinalIgnoreCase)).Select(r=>r.NewName+r.Extension));
   foreach(string entry in occupied){
    var match=pattern.Match(Path.GetFileName(entry));long number;
    if(match.Success){if(!Int64.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out number))throw new Exception("目录中已有超长日期编号，请先检查："+Path.GetFileName(entry));last=Math.Max(last,number);}
   }
   long final;try{final=checked(last+group.LongCount());}catch(OverflowException){throw new Exception("日期编号已超出可用范围，请调整已有名称后重新预览。");}
   int width=Math.Max(6,final.ToString(CultureInfo.InvariantCulture).Length);
   foreach(var item in group){last++;result[item.Index]=prefix+last.ToString("D"+width,CultureInfo.InvariantCulture);}
  }
  return result;
 }
 public static List<string> FillEmptyNames(List<RenameRow> rows,List<string> proposals,DateTime date,IEnumerable<RenameRow> reservations=null){
  var result=proposals.ToList();var copies=rows.Select((r,i)=>new RenameRow{Source=r.Source,IsDirectory=r.IsDirectory,Include=r.Include,NewName=proposals[i]}).ToList();
  var indices=Enumerable.Range(0,rows.Count).Where(i=>rows[i].Include&&String.IsNullOrWhiteSpace(proposals[i])).ToList();if(indices.Count==0)return result;
  var empty=indices.Select(i=>copies[i]).ToList();var occupied=copies.Where(r=>r.Include&&!String.IsNullOrWhiteSpace(r.NewName)).Concat(reservations??Enumerable.Empty<RenameRow>());
  var names=DateNames(empty,date,occupied);for(int i=0;i<indices.Count;i++)result[indices[i]]=names[i];return result;
 }
 public static List<string> NumberPrefixCollisions(List<RenameRow> rows,List<string> proposals,IEnumerable<string> prefixes,bool includeWebsite=true,IEnumerable<RenameRow> reservations=null){
  var result=proposals.ToList();var desired=new Dictionary<int,string>();var requested=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var assigned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var existing=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);var counters=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
  for(int i=0;i<rows.Count;i++)if(rows[i].Include&&String.IsNullOrEmpty(rows[i].BlockedReason)){
   string name=proposals[i];if(String.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.EndsWith(".")||name.EndsWith(" "))continue;
   try{string path=Safety.Destination(Path.GetDirectoryName(rows[i].Source),name+rows[i].Extension);desired[i]=path;requested.Add(path);}catch{}
  }
  foreach(var row in reservations??Enumerable.Empty<RenameRow>())if(row.Include){try{assigned.Add(Safety.Destination(Path.GetDirectoryName(row.Source),row.NewName+row.Extension));}catch{}}
  for(int i=0;i<rows.Count;i++){
   string target;if(!desired.TryGetValue(i,out target))continue;var row=rows[i];
   bool prefixed=MatchPrefix(row.OriginalStem,prefixes)!=null||(includeWebsite&&MatchWebsitePrefix(row.OriginalStem,Enumerable.Empty<string>())!=null);
   if(!prefixed||String.Equals(target,row.Source,StringComparison.OrdinalIgnoreCase)){assigned.Add(target);continue;}
   string directory=Path.GetDirectoryName(row.Source);HashSet<string> present;
   if(!existing.TryGetValue(directory,out present)){present=new HashSet<string>(Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase);existing[directory]=present;}
   if(present.Contains(target)||assigned.Contains(target)){
    long number;if(!counters.TryGetValue(target,out number))number=1;string candidate,name;
    do{name=proposals[i]+" ("+number.ToString(CultureInfo.InvariantCulture)+")";candidate=Path.Combine(directory,name+row.Extension);number=checked(number+1);}while(present.Contains(candidate)||assigned.Contains(candidate)||requested.Contains(candidate));
    result[i]=name;target=candidate;counters[desired[i]]=number;
   }
   assigned.Add(target);
  }
  return result;
 }
 public static string Suggest(string original,RenameOptions options,int index){
  if(options.NumberAll){if(String.IsNullOrWhiteSpace(options.Title))throw new Exception("统一编号时，请先填写统一名称。");return options.Title.Trim()+" "+index.ToString("D3");}
  string name=StripPrefix(original,options.CustomPrefixes);
  if(options.RemoveSite)name=Regex.Replace(name,WebsitePattern,"",RegexOptions.IgnoreCase);
  if(options.CleanSeparators){name=name.Replace('_',' ');name=Regex.Replace(name,@"《([^》]+)》","$1");name=Regex.Replace(name,@"\s+"," ").Trim(' ','-');}
  if(options.NormalizeEpisode){var episode=Regex.Match(name,@"(?<![a-zA-Z])EP[\s._-]*(\d{1,4})(?!\d)",RegexOptions.IgnoreCase);
   if(episode.Success){string title=String.IsNullOrWhiteSpace(options.Title)?name.Substring(0,episode.Index).Trim(' ','_','-','.'):options.Title.Trim();string tail=name.Substring(episode.Index+episode.Length).Trim(' ','_','-');name=(title+" EP"+Int32.Parse(episode.Groups[1].Value).ToString("D2")+(tail.Length>0?" "+tail:"")).Trim();}}
  return name;
 }
 public static List<RenameRow> Capture(IEnumerable<string> sources){return sources.Select(p=>new RenameRow{Source=p,IsDirectory=Directory.Exists(p),Stamp=Safety.Stamp(p),NewName=Directory.Exists(p)?Path.GetFileName(p):Path.GetFileNameWithoutExtension(p)}).ToList();}
 public static string CheckName(RenameRow row){
  try{if(!String.IsNullOrEmpty(row.BlockedReason))return row.BlockedReason;if(String.IsNullOrWhiteSpace(row.NewName))return "新名称为空，请输入名称或重新生成";
   var invalid=row.NewName.Where(c=>Path.GetInvalidFileNameChars().Contains(c)).Distinct().ToArray();if(invalid.Length>0)return "新名称含有无效字符："+String.Join(" ",invalid.Select(c=>Char.IsControl(c)?"U+"+((int)c).ToString("X4"):c.ToString()));
   if(row.NewName.EndsWith("."))return "新名称末尾有句点，请去掉末尾的点";if(row.NewName.EndsWith(" "))return "新名称末尾有空格，请去掉末尾空格";
   Safety.Destination(Path.GetDirectoryName(row.Source),row.NewName+row.Extension);
   if(String.Equals(row.Source,row.Target,StringComparison.Ordinal))return "无需修改";
   if(String.Equals(row.Source,row.Target,StringComparison.OrdinalIgnoreCase))return "仅大小写不同，请手动调整名称";
   if(Safety.Exists(row.Target))return "同名目标已存在";
   return null;
  }catch(Exception e){return e.Message;}
 }
 public static bool Validate(List<RenameRow> rows){
  var duplicate=new HashSet<RenameRow>(rows.Where(r=>r.Include&&CheckName(r)==null).GroupBy(r=>r.Target,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).SelectMany(g=>g));bool valid=true;
  foreach(var row in rows){if(!row.Include){row.Status="保留原名";continue;}string error=CheckName(row);row.Status=duplicate.Contains(row)?"多个项目的新名称相同":error=="无需修改"?"待设置新名称":error??"可改名";if(row.Status!="可改名")valid=false;}
  return valid;
 }
 public static string ProblemSummary(List<RenameRow> rows){var failed=rows.Where(r=>r.Include&&r.Status!="可改名").ToList();if(failed.Count==0)return "当前没有待改名项目。";string name=failed[0].Original;if(name.Length>90)name=name.Substring(0,87)+"…";return "有 "+failed.Count+" 项未通过检查。首项："+name+"\n原因："+failed[0].Status+"。请修改新名称或取消勾选该行。";}
 public static string ProblemDetails(RenameRow row){string target;try{target=row.Target;}catch(Exception e){target="无法组成目标路径："+e.Message;}return "原文件："+row.Source+"\n新名称："+row.NewName+row.Extension+"\n目标路径："+target+"\n原因："+row.Status;}
 public static string ProblemReport(IEnumerable<RenameRow> rows){var problems=rows.Where(r=>r.IsProblem).ToList();return "共 "+problems.Count+" 项改名检查未通过。\n\n"+String.Join("\n\n",problems.Select((r,i)=>(i+1)+". "+ProblemDetails(r)));}
 public static void Move(RenameRow row){string error=CheckName(row);if(error!=null)throw new Exception("无法改名："+row.Source+"\n原因："+error);if(Safety.Stamp(row.Source)!=row.Stamp)throw new Exception("项目在预览后发生变化，请重新预览："+row.Source);Safety.NoLinks(row.Target);if(row.IsDirectory)Directory.Move(row.Source,row.Target);else File.Move(row.Source,row.Target);}
}
partial class ModernWindow {
 Window RenameProblemsDialog(Window owner,Func<List<RenameRow>> getProblems,Action<RenameRow> locate){
  var d=Dialog("改名问题清单",1100,650);d.Owner=owner;d.MinWidth=850;d.MinHeight=480;
  var panel=new DockPanel();var title=new TextBlock{FontSize=21,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
  var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
  var detail=new TextBox{Name="RenameProblemDetail",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,0,0,10)};bottom.Children.Add(detail);
  var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(actions);
  var copy=new Button{Name="RenameCopyProblems",Content="复制全部问题"};actions.Children.Add(copy);
  var skip=new Button{Name="RenameSkipSelectedProblem",Content="跳过这一项"};actions.Children.Add(skip);
  var locateButton=new Button{Name="RenameLocateSelectedProblem",Content="定位到预览",Style=(Style)Window.Resources["Soft"]};actions.Children.Add(locateButton);
  actions.Children.Add(new Button{Content="关闭",IsCancel=true});
  var list=new DataGrid{Name="RenameProblemsGrid",AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,RowHeaderWidth=0,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,MinRowHeight=42,Background=Brushes.White,BorderThickness=new Thickness(0)};
  foreach(var spec in new[]{new[]{"文件名","Original"},new[]{"所在目录","FullLocation"},new[]{"新名称","NewName"},new[]{"失败原因","Status"}})list.Columns.Add(new DataGridTextColumn{Header=spec[0],Binding=new Binding(spec[1]),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});panel.Children.Add(list);
  Action refresh=()=>{var problems=getProblems();list.ItemsSource=problems;title.Text="有 "+problems.Count+" 项阻止改名，选中后可查看完整路径和原因";if(problems.Count>0)list.SelectedIndex=0;else detail.Text="问题已全部排除，可关闭此窗口回到预览继续确认。";skip.IsEnabled=problems.Count>0;locateButton.IsEnabled=problems.Count>0;copy.IsEnabled=problems.Count>0;};
  list.SelectionChanged+=delegate{var row=list.SelectedItem as RenameRow;if(row!=null)detail.Text=RenameRules.ProblemDetails(row);};
  copy.Click+=delegate{try{Clipboard.SetText(RenameRules.ProblemReport(getProblems()));}catch(Exception e){MessageBox.Show(d,e.Message,"复制失败");}};
  skip.Click+=delegate{var row=list.SelectedItem as RenameRow;if(row!=null)row.Include=false;refresh();};
  locateButton.Click+=delegate{var row=list.SelectedItem as RenameRow;if(row==null)return;d.Close();locate(row);};
  d.Content=new Border{Padding=new Thickness(22),Background=d.Background,Child=panel};refresh();return d;
 }
 void SaveRenamePrefixes(List<string> values){var previous=renamePrefixes;renamePrefixes=values;try{Save();}catch{renamePrefixes=previous;throw;}}
 bool EditRenamePrefixes(Window owner){
  var d=Dialog("自定义要去掉的前缀",640,470);d.Owner=owner;bool saved=false;
  var panel=new DockPanel{Margin=new Thickness(24)};
  var note=new TextBlock{Text="每行一个前缀，按原文字面匹配，不区分大小写。只删除开头匹配的一次；重叠时优先最长的一条。\n例如：www.example.com@\n后面的标题与扩展名保留。保存后会记住列表，并生成清理预览。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,15),Foreground=Brushes.SlateGray};DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);
  var input=new TextBox{Name="RenamePrefixInput",Text=String.Join(Environment.NewLine,renamePrefixes),AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
  buttons.Children.Add(new Button{Content="取消",IsCancel=true});var save=new Button{Content="保存并预览",Style=(Style)Window.Resources["Primary"]};buttons.Children.Add(save);
  save.Click+=delegate{try{SaveRenamePrefixes(RenameRules.ParsePrefixes(input.Text));saved=true;d.Close();}catch(Exception e){MessageBox.Show(d,e.Message,"前缀未保存");}};
  panel.Children.Add(input);d.Content=panel;d.ShowDialog();return saved;
 }
 static T RenameAncestor<T>(DependencyObject element) where T:DependencyObject {while(element!=null){var match=element as T;if(match!=null)return match;element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);}return null;}
 List<RenameRow> ScanRenameRows(IEnumerable<string> sources,bool recursive,string[] marked,System.Threading.CancellationToken cancellation){
  var result=new List<RenameRow>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var pending=new Stack<string>(sources.Reverse());
  while(pending.Count>0){
   cancellation.ThrowIfCancellationRequested();string path=Path.GetFullPath(pending.Pop());if(!seen.Add(path))continue;
   var row=new RenameRow{Source=path,IsDirectory=Directory.Exists(path)};row.NewName=row.OriginalStem;
   try{RecycleSafety.Protect(path,root,state);if(marked.Any(k=>String.Equals(k,path,StringComparison.OrdinalIgnoreCase)||Safety.Under(k,path)||Safety.Under(path,k)))throw new Exception("已有相关移动标注，请先取消标注");row.Stamp=Safety.Stamp(path);}
   catch(Exception e){row.BlockedReason=e.Message.Replace("删除","改名");row.Include=false;}
   result.Add(row);
   if(recursive&&row.IsDirectory&&String.IsNullOrEmpty(row.BlockedReason)){
    try{foreach(string child in Directory.EnumerateFileSystemEntries(path).OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase)){cancellation.ThrowIfCancellationRequested();pending.Push(child);}}
    catch(OperationCanceledException){throw;}
    catch(Exception e){row.BlockedReason="子目录读取失败："+e.Message;row.Include=false;}
   }
  }
  return result;
 }
 void RenameSelected(){
  if(moving)return;bool autoDetect=cards.SelectedItems.Count==0;var sources=(autoDetect?cards.Items.Cast<CardItem>():cards.SelectedItems.Cast<CardItem>()).Select(i=>i.FullPath).ToList();if(sources.Count==0){Toast("当前没有可整理的项目。");return;}
  var rows=ScanRenameRows(sources,false,queue.Keys.ToArray(),System.Threading.CancellationToken.None);
  RenameDialog(rows,autoDetect).ShowDialog();
 }
 Window RenameDialog(List<RenameRow> rows,bool autoDetect=false){
  string locationBase=rows.Count>0?Path.GetDirectoryName(rows[0].Source):folder;if(rows.Any(r=>!String.Equals(Path.GetDirectoryName(r.Source),locationBase,StringComparison.OrdinalIgnoreCase)))locationBase=folder;foreach(var row in rows)row.LocationBase=locationBase;
  var d=Dialog("整理命名 · 预览后改名",1200,780);d.MinWidth=950;d.MinHeight=640;
  var panel=new DockPanel{Background=d.Background};
  var top=new StackPanel{Margin=new Thickness(0,0,0,14)};DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
  var heading=new TextBlock{Text="整理 "+rows.Count+" 项的名称",FontSize=23,FontWeight=FontWeights.Bold};top.Children.Add(heading);
  top.Children.Add(new TextBlock{Text="单击整行即可勾选或取消改名；双击新名称可编辑。勾选后按当前规则生成新名称，取消则保留原名。文件扩展名保留。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,8,0,10)});
  var optionsPanel=new WrapPanel();top.Children.Add(optionsPanel);
  var clean=new CheckBox{Content="清理空格、下划线和书名号",IsChecked=true,Margin=new Thickness(0,4,16,8)};
  var sites=new CheckBox{Content="去掉开头括号中的网址",IsChecked=true,Margin=new Thickness(0,4,16,8)};
  var episodes=new CheckBox{Content="集数统一为 EP01",IsChecked=true,Margin=new Thickness(0,4,0,8)};
  optionsPanel.Children.Add(clean);optionsPanel.Children.Add(sites);optionsPanel.Children.Add(episodes);
  var title=new TextBox{Name="RenameTitle",Tag="统一名称（可选），如：示例剧集",Margin=new Thickness(0,0,0,9)};top.Children.Add(title);
  var numbered=new CheckBox{Name="RenameNumbered",Content="全部改为“统一名称 001、002…”（替换原名，按下表顺序编号）",Margin=new Thickness(0,0,0,9)};top.Children.Add(numbered);
  var dated=new CheckBox{Name="RenameDate",Content="按当天日期编号，如 "+RenameRules.Today().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"__000001（北京时间；替换原名，保留扩展名）",Margin=new Thickness(0,0,0,9)};top.Children.Add(dated);
  var prefixOnly=new CheckBox{Name="RenamePrefixOnly",Content="只去网站/自定义前缀（保留后文；空名补日期、重名加编号）",Margin=new Thickness(0,0,0,9)};top.Children.Add(prefixOnly);
  var recursive=new CheckBox{Name="RenameRecursivePrefixes",Content="包含子文件夹（仅清理前缀；切换范围后重新扫描和预览）",IsChecked=false,Margin=new Thickness(0,0,0,9)};top.Children.Add(recursive);
  dated.Checked+=delegate{numbered.IsChecked=false;prefixOnly.IsChecked=false;title.IsEnabled=false;};dated.Unchecked+=delegate{title.IsEnabled=prefixOnly.IsChecked!=true;};numbered.Checked+=delegate{dated.IsChecked=false;prefixOnly.IsChecked=false;title.IsEnabled=true;};prefixOnly.Checked+=delegate{dated.IsChecked=false;numbered.IsChecked=false;title.IsEnabled=false;};prefixOnly.Unchecked+=delegate{title.IsEnabled=dated.IsChecked!=true;};
  var actions=new WrapPanel();top.Children.Add(actions);
  var autoSelect=new Button{Name="RenameAutoDetect",Content="自动挑乱码并按日期编号",Style=(Style)Window.Resources["Soft"]};actions.Children.Add(autoSelect);
  var regenerate=new Button{Name="RenameRegenerate",Content="按规则重新生成预览",HorizontalAlignment=HorizontalAlignment.Left,Style=(Style)Window.Resources["Soft"]};actions.Children.Add(regenerate);
  var prefixApply=new Button{Name="RenameApplyPrefixes",Content="清理已知前缀",Style=(Style)Window.Resources["Soft"]};actions.Children.Add(prefixApply);
  var prefixManage=new Button{Name="RenameManagePrefixes",Content="自定义前缀"};actions.Children.Add(prefixManage);
  var detectionSummary=new TextBlock{Text="自动识别只依据名称规则，可读名称和不确定项保留。无需选择文件即可扫描当前列表；有选中项时只处理选中项。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,FontSize=12,Margin=new Thickness(0,8,0,0)};top.Children.Add(detectionSummary);
  top.Children.Add(new TextBlock{Text="日期编号至少六位，数量多时自动扩展；从目录中当天最大编号接着排，文件和文件夹共用序号。重新生成会覆盖手动编辑。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,FontSize=12,Margin=new Thickness(0,8,0,0)});
  var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
  var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};bottom.Children.Add(summary);
  var problemBar=new Border{Name="RenameProblemBar",Background=(Brush)new BrushConverter().ConvertFromString("#FFF0F0"),CornerRadius=new CornerRadius(10),Padding=new Thickness(10),Margin=new Thickness(0,0,0,8),Visibility=Visibility.Collapsed};bottom.Children.Insert(0,problemBar);
  var problemActions=new WrapPanel();problemBar.Child=problemActions;
  var problemCount=new TextBlock{Name="RenameProblemCount",Foreground=Brushes.Firebrick,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};problemActions.Children.Add(problemCount);
  var onlyProblems=new CheckBox{Name="RenameOnlyProblems",Content="只看问题项",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};problemActions.Children.Add(onlyProblems);
  var nextProblem=new Button{Name="RenameNextProblem",Content="定位下一项",Padding=new Thickness(10,7,10,7)};problemActions.Children.Add(nextProblem);
  var showProblems=new Button{Name="RenameShowProblems",Content="查看全部问题",Padding=new Thickness(10,7,10,7)};problemActions.Children.Add(showProblems);
  var skipProblems=new Button{Name="RenameSkipProblems",Content="跳过所有问题项",Padding=new Thickness(10,7,10,7)};problemActions.Children.Add(skipProblems);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);
  var cancel=new Button{Content="取消",IsCancel=true};buttons.Children.Add(cancel);
  var execute=new Button{Name="RenameExecute",Content="确认改名",Style=(Style)Window.Resources["Primary"],IsDefault=false};buttons.Children.Add(execute);
  var grid=new DataGrid{Name="RenameGrid",AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,RowHeaderWidth=0,ItemsSource=rows,Background=Brushes.White,BorderThickness=new Thickness(0),MinRowHeight=42,SelectionUnit=DataGridSelectionUnit.FullRow,SelectionMode=DataGridSelectionMode.Single,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,HorizontalGridLinesBrush=(Brush)new BrushConverter().ConvertFromString("#E9EAF3")};
  var rowStyle=new Style(typeof(DataGridRow));rowStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,Window.Foreground));
  var included=new DataTrigger{Binding=new Binding("Include"),Value=true};included.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFromString("#F1EDFF")));rowStyle.Triggers.Add(included);
  var problemStyle=new DataTrigger{Binding=new Binding("IsProblem"),Value=true};problemStyle.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFromString("#FFE0E0")));rowStyle.Triggers.Add(problemStyle);grid.RowStyle=rowStyle;
  var cellStyle=new Style(typeof(DataGridCell));cellStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));cellStyle.Setters.Add(new Setter(Control.ForegroundProperty,Window.Foreground));cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));cellStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty,null));cellStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty,true));grid.CellStyle=cellStyle;
  var indicator=new FrameworkElementFactory(typeof(CheckBox));indicator.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("Include"){Mode=BindingMode.OneWay});indicator.SetValue(UIElement.IsHitTestVisibleProperty,false);indicator.SetValue(UIElement.FocusableProperty,false);indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);indicator.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
  grid.Columns.Add(new DataGridTemplateColumn{Header="我要改名",CellTemplate=new DataTemplate{VisualTree=indicator},IsReadOnly=true,Width=75});
  grid.Columns.Add(new DataGridTextColumn{Header="原名称",Binding=new Binding("Original"),IsReadOnly=true,MinWidth=180,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="新名称（不含扩展名，可编辑）",Binding=new Binding("NewName"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},MinWidth=240,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="扩展名",Binding=new Binding("Extension"),IsReadOnly=true,Width=75});
  grid.Columns.Add(new DataGridTextColumn{Header="自动识别依据",Binding=new Binding("Detection"),IsReadOnly=true,Width=220});
  grid.Columns.Add(new DataGridTextColumn{Header="检查",Binding=new Binding("Status"),IsReadOnly=true,Width=110});
  var locationColumn=new DataGridTextColumn{Header="所在目录（悬停查看完整路径）",Binding=new Binding("Location"),IsReadOnly=true,Width=220,Visibility=Visibility.Collapsed};grid.Columns.Add(locationColumn);
  foreach(var textColumn in grid.Columns.OfType<DataGridTextColumn>()){var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));textStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(textColumn==locationColumn?"FullLocation":((Binding)textColumn.Binding).Path.Path)));textColumn.ElementStyle=textStyle;}panel.Children.Add(grid);
  bool scanning=false;string scanFailure=null;
  Action updateProblems=null;
  Action validate=()=>{bool valid=RenameRules.Validate(rows);int count=rows.Count(r=>r.Include&&r.Status=="可改名");execute.IsEnabled=!scanning&&scanFailure==null&&valid&&count>0;execute.Content="确认改名 "+count+" 项";summary.Text=scanning?"正在扫描子文件夹，可取消勾选或关闭窗口停止扫描…":scanFailure!=null?"扫描失败，请切换范围重试："+scanFailure:valid?"待改名 "+count+" 项；重名不覆盖。确认后会保存旧名 → 新名日志。":RenameRules.ProblemSummary(rows);if(updateProblems!=null)updateProblems();};
  bool generating=false;
  Action regeneratePreview=()=>{try{var options=new RenameOptions{CleanSeparators=clean.IsChecked==true,RemoveSite=sites.IsChecked==true,NormalizeEpisode=episodes.IsChecked==true,Title=title.Text,NumberAll=numbered.IsChecked==true,CustomPrefixes=renamePrefixes};var names=prefixOnly.IsChecked==true?rows.Select(r=>r.Include?RenameRules.StripWebsitePrefix(r.OriginalStem,renamePrefixes):r.OriginalStem).ToList():dated.IsChecked==true?RenameRules.DateNames(rows,RenameRules.Today()):rows.Select((r,i)=>RenameRules.Suggest(r.OriginalStem,options,i+1)).ToList();var completed=RenameRules.FillEmptyNames(rows,names,RenameRules.Today());var unique=dated.IsChecked!=true&&numbered.IsChecked!=true?RenameRules.NumberPrefixCollisions(rows,completed,renamePrefixes,prefixOnly.IsChecked==true||sites.IsChecked==true):completed;generating=true;for(int i=0;i<rows.Count;i++){rows[i].EmptyNameFallback=rows[i].Include&&String.IsNullOrWhiteSpace(names[i]);rows[i].DuplicateSuffixAdded=unique[i]!=completed[i];rows[i].NewName=unique[i];}validate();}catch(Exception e){MessageBox.Show(d,e.Message,"无法生成预览");}finally{generating=false;}};
  PropertyChangedEventHandler rowChanged=delegate(object sender,PropertyChangedEventArgs e){
   if(generating)return;
   if(e.PropertyName=="Include"){
    var changed=(RenameRow)sender;generating=true;
    try{
     changed.ManualOverride=changed.Include;changed.DuplicateSuffixAdded=false;
     if(!changed.Include){changed.EmptyNameFallback=false;changed.NewName=changed.OriginalStem;}
     else if(String.IsNullOrEmpty(changed.BlockedReason)){changed.EmptyNameFallback=false;
      if(prefixOnly.IsChecked==true)changed.NewName=RenameRules.StripWebsitePrefix(changed.OriginalStem,renamePrefixes);
      else if(dated.IsChecked==true)changed.NewName=RenameRules.DateNames(new List<RenameRow>{changed},RenameRules.Today(),rows.Where(r=>r!=changed))[0];
      else {var options=new RenameOptions{CleanSeparators=clean.IsChecked==true,RemoveSite=sites.IsChecked==true,NormalizeEpisode=episodes.IsChecked==true,Title=title.Text,NumberAll=numbered.IsChecked==true,CustomPrefixes=renamePrefixes};changed.NewName=RenameRules.Suggest(changed.OriginalStem,options,rows.IndexOf(changed)+1);}
      if(String.IsNullOrWhiteSpace(changed.NewName)){changed.EmptyNameFallback=true;changed.NewName=RenameRules.FillEmptyNames(new List<RenameRow>{changed},new List<string>{changed.NewName},RenameRules.Today(),rows.Where(r=>r!=changed))[0];}
      if(dated.IsChecked!=true&&numbered.IsChecked!=true){string unique=RenameRules.NumberPrefixCollisions(new List<RenameRow>{changed},new List<string>{changed.NewName},renamePrefixes,prefixOnly.IsChecked==true||sites.IsChecked==true,rows.Where(r=>r!=changed))[0];changed.DuplicateSuffixAdded=unique!=changed.NewName;changed.NewName=unique;}
     }
    }catch(Exception error){MessageBox.Show(d,error.Message,"无法生成新名称");}finally{generating=false;}
    validate();
   }else if(e.PropertyName=="NewName"){((RenameRow)sender).EmptyNameFallback=false;((RenameRow)sender).DuplicateSuffixAdded=false;validate();}
  };foreach(var row in rows)row.PropertyChanged+=rowChanged;
  regenerate.Click+=delegate{regeneratePreview();};
  Action refreshPrefixes=()=>{foreach(var row in rows)row.PrefixMatch=RenameRules.MatchWebsitePrefix(row.OriginalStem,renamePrefixes);};refreshPrefixes();
  prefixApply.Click+=delegate{refreshPrefixes();generating=true;int count=0;try{foreach(var row in rows){row.ManualOverride=false;row.Include=String.IsNullOrEmpty(row.BlockedReason)&&!String.IsNullOrEmpty(row.PrefixMatch);if(row.Include)count++;else row.NewName=row.OriginalStem;}}finally{generating=false;}prefixOnly.IsChecked=true;regeneratePreview();detectionSummary.Text="匹配到 "+count+" 项，可通过整行点击调整勾选；只去开头前缀，后文与扩展名保留。";};
  prefixManage.Click+=delegate{if(EditRenamePrefixes(d))prefixApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
  autoSelect.Click+=delegate{generating=true;int count;try{count=NameDetection.SelectSuspects(rows);}finally{generating=false;}dated.IsChecked=true;regeneratePreview();detectionSummary.Text="自动勾选 "+count+" 项疑似乱码；其余 "+(rows.Count-count)+" 项保留。未勾选的名称保持原样；可查看识别依据或调整勾选。";};
  var scopeSources=rows.Select(r=>r.Source).ToArray();int scopeVersion=0;bool closed=false;System.Threading.CancellationTokenSource scopeCancellation=null;
  Action updateScopeControls=()=>{bool deep=recursive.IsChecked==true;grid.IsEnabled=!scanning;prefixApply.IsEnabled=!scanning;prefixManage.IsEnabled=!scanning;regenerate.IsEnabled=!scanning;autoSelect.IsEnabled=!scanning&&!deep;dated.IsEnabled=!scanning&&!deep;numbered.IsEnabled=!scanning&&!deep;prefixOnly.IsEnabled=!scanning&&!deep;clean.IsEnabled=!scanning&&!deep;sites.IsEnabled=!scanning&&!deep;episodes.IsEnabled=!scanning&&!deep;title.IsEnabled=!scanning&&!deep&&dated.IsChecked!=true&&prefixOnly.IsChecked!=true;};
  RoutedEventHandler changeScope=async delegate(object sender,RoutedEventArgs e){
   int version=++scopeVersion;if(scopeCancellation!=null)scopeCancellation.Cancel();var cancellation=new System.Threading.CancellationTokenSource();scopeCancellation=cancellation;
   bool deep=recursive.IsChecked==true;var marked=queue.Keys.ToArray();scanning=true;scanFailure=null;prefixOnly.IsChecked=true;updateScopeControls();validate();
   try{
    var scanned=await Task.Run(()=>ScanRenameRows(scopeSources,deep,marked,cancellation.Token));if(closed||version!=scopeVersion)return;
    grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);foreach(var row in rows)row.PropertyChanged-=rowChanged;
    grid.ItemsSource=null;rows.Clear();rows.AddRange(scanned);foreach(var row in rows){row.LocationBase=locationBase;row.PropertyChanged+=rowChanged;}grid.ItemsSource=rows;
    locationColumn.Visibility=deep?Visibility.Visible:Visibility.Collapsed;heading.Text="整理 "+rows.Count+" 项的名称"+(deep?"（包含子文件夹）":"");scanning=false;prefixApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   }catch(OperationCanceledException){}
   catch(Exception error){if(!closed&&version==scopeVersion)scanFailure=error.Message;}
   finally{cancellation.Dispose();if(!closed&&version==scopeVersion){scopeCancellation=null;scanning=false;updateScopeControls();validate();}}
  };
  recursive.Checked+=changeScope;recursive.Unchecked+=changeScope;
  d.Closed+=delegate{closed=true;++scopeVersion;if(scopeCancellation!=null)scopeCancellation.Cancel();};
  Predicate<object> problemFilter=item=>((RenameRow)item).IsProblem||Object.ReferenceEquals(item,grid.SelectedItem);
  Action refreshProblemView=()=>{var view=CollectionViewSource.GetDefaultView(rows);var edit=view as IEditableCollectionView;if(edit!=null&&(edit.IsEditingItem||edit.IsAddingNew))return;Predicate<object> wanted=onlyProblems.IsChecked==true?problemFilter:null;if(view.Filter!=wanted)view.Filter=wanted;else if(wanted!=null)view.Refresh();};
  updateProblems=()=>{int count=rows.Count(r=>r.IsProblem);problemBar.Visibility=!scanning&&(count>0||onlyProblems.IsChecked==true)?Visibility.Visible:Visibility.Collapsed;problemCount.Text=count>0?count+" 项问题阻止改名":"问题已清空";onlyProblems.IsEnabled=!scanning;nextProblem.IsEnabled=showProblems.IsEnabled=skipProblems.IsEnabled=!scanning&&count>0;refreshProblemView();};
  Action<RenameRow> locateProblem=row=>{if(row==null)return;grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);onlyProblems.IsChecked=true;refreshProblemView();locationColumn.Visibility=Visibility.Visible;grid.SelectedItem=row;grid.ScrollIntoView(row,grid.Columns[1]);grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[1]);grid.Focus();summary.Text=RenameRules.ProblemDetails(row);};
  RoutedEventHandler toggleProblemFilter=delegate{grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);var selected=grid.SelectedItem as RenameRow;if(selected!=null&&!selected.IsProblem)grid.SelectedItem=null;refreshProblemView();updateProblems();};onlyProblems.Checked+=toggleProblemFilter;onlyProblems.Unchecked+=toggleProblemFilter;
  nextProblem.Click+=delegate{var problems=rows.Where(r=>r.IsProblem).ToList();if(problems.Count>0)locateProblem(problems[(problems.IndexOf(grid.SelectedItem as RenameRow)+1)%problems.Count]);};
  showProblems.Click+=delegate{RenameProblemsDialog(d,()=>rows.Where(r=>r.IsProblem).ToList(),locateProblem).ShowDialog();};
  skipProblems.Click+=delegate{var problems=rows.Where(r=>r.IsProblem).ToList();generating=true;try{foreach(var row in problems){row.Include=false;row.ManualOverride=false;row.EmptyNameFallback=false;row.DuplicateSuffixAdded=false;row.NewName=row.OriginalStem;}}finally{generating=false;}grid.SelectedItem=null;validate();};
  grid.SelectionChanged+=delegate{if(onlyProblems.IsChecked==true)d.Dispatcher.BeginInvoke(new Action(()=>{if(!closed)refreshProblemView();}),System.Windows.Threading.DispatcherPriority.Background);};
  grid.CellEditEnding+=delegate{d.Dispatcher.BeginInvoke(new Action(()=>{if(!closed){validate();refreshProblemView();}}),System.Windows.Threading.DispatcherPriority.Background);};
  RenameRow lastClicked=null;bool wasIncluded=false;string previousName=null;
  grid.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
   var source=e.OriginalSource as DependencyObject;if(RenameAncestor<TextBox>(source)!=null)return;
   var container=RenameAncestor<DataGridRow>(source);var cell=RenameAncestor<DataGridCell>(source);if(container==null||cell==null)return;var row=container.Item as RenameRow;if(row==null)return;
   if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true)){e.Handled=true;return;}
   grid.SelectedItem=row;
   if(e.ClickCount==2&&cell.Column==grid.Columns[2]){
    generating=true;try{row.Include=true;if(lastClicked==row&&wasIncluded)row.NewName=previousName;row.ManualOverride=true;}finally{generating=false;}validate();
    grid.CurrentCell=new DataGridCellInfo(row,cell.Column);cell.Focus();grid.BeginEdit();
   }else if(e.ClickCount<2){lastClicked=row;wasIncluded=row.Include;previousName=row.NewName;row.Include=!row.Include;grid.Focus();}
   e.Handled=true;
  };
  grid.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Space&&RenameAncestor<TextBox>(e.OriginalSource as DependencyObject)==null){var row=grid.SelectedItem as RenameRow;if(row!=null){row.Include=!row.Include;e.Handled=true;}}};
  execute.Click+=async delegate{
   grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);validate();if(!execute.IsEnabled)return;
   var plans=rows.Where(r=>r.Include&&r.Status=="可改名").ToList();
   if(MessageBox.Show(d,"确认按预览修改 "+plans.Count+" 项名称？"+(recursive.IsChecked==true?"\n范围包含子文件夹，请核对“所在目录”列。":"")+"\n只修改名称，文件内容和扩展名保留。","确认整理命名",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
   moving=true;d.IsEnabled=false;string report;try{Save();report=await Task.Run(()=>ExecuteRename(plans));}catch(Exception e){report="改名未开始："+e.Message;}finally{moving=false;d.IsEnabled=true;}
   d.Close();await NavigateAsync(folder);MessageBox.Show(Window,report,"改名结果");
  };
  d.Closing+=delegate(object sender,CancelEventArgs e){if(moving)e.Cancel=true;};d.Content=new Border{Padding=new Thickness(22),Background=d.Background,Child=panel};if(autoDetect&&rows.Any(r=>String.IsNullOrEmpty(r.BlockedReason)&&RenameRules.MatchPrefix(r.OriginalStem,renamePrefixes)!=null))prefixApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else if(autoDetect)autoSelect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else regeneratePreview();return d;
 }
 string ExecuteRename(List<RenameRow> plans){
  plans=plans.OrderByDescending(r=>r.Source.TrimEnd('\\').Count(c=>c=='\\'||c=='/')).ThenBy(r=>r.Source,StringComparer.OrdinalIgnoreCase).ToList();
  int done=0;string error="";string log=Path.Combine(Program.Home,"改名日志-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".xml");
  var journal=new XElement("renameBatch",plans.Select(r=>new XElement("item",new XAttribute("source",r.Source),new XAttribute("target",r.Target),new XAttribute("finalTarget",FinalRenameTarget(r,plans)),new XAttribute("status","待处理"))));RenameRow current=null;
  try{
   if(!RenameRules.Validate(plans))throw new Exception(RenameRules.ProblemReport(plans));
   foreach(var row in plans){current=row;RecycleSafety.Protect(row.Source,root,state);if(Safety.Stamp(row.Source)!=row.Stamp)throw new Exception("项目已变化，请重新预览："+row.Source);}current=null;
   journal.Save(log);
   for(int i=0;i<plans.Count;i++){current=plans[i];
    var parents=plans.Skip(i+1).Where(r=>r.IsDirectory&&String.Equals(r.Source,Path.GetDirectoryName(plans[i].Source),StringComparison.OrdinalIgnoreCase)).ToList();
    foreach(var parent in parents)if(Safety.Stamp(parent.Source)!=parent.Stamp)throw new Exception("父目录在预览后发生变化，请重新预览："+parent.Source);
    var entry=journal.Elements("item").ElementAt(i);entry.SetAttributeValue("status","准备改名");journal.Save(log);RenameRules.Move(plans[i]);done++;
    foreach(var parent in parents)parent.Stamp=Safety.Stamp(parent.Source);
    entry.SetAttributeValue("status","已完成");journal.Save(log);current=null;
   }
  }catch(Exception e){error="\n后续操作已停止："+e.Message+(current==null?"":"\n问题项目："+current.Source+"\n目标："+current.Target);try{journal.SetAttributeValue("error",e.Message);journal.Save(log);}catch{}}
  return "已改名 "+done+" / "+plans.Count+" 项。"+error+"\n旧名与新名记录："+log;
 }
 static string FinalRenameTarget(RenameRow row,List<RenameRow> plans){string path=row.Target;foreach(var ancestor in plans.Where(r=>r.IsDirectory&&Safety.Under(row.Source,r.Source)).OrderByDescending(r=>r.Source.Length))path=Path.Combine(ancestor.Target,path.Substring(ancestor.Source.TrimEnd('\\').Length+1));return path;}
 async Task RenameSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Rename: "+message);};
  Action<Action,string> reject=(action,message)=>{bool stopped=false;try{action();}catch{stopped=true;}check(stopped,message);};
  var options=new RenameOptions();
  foreach(string name in new[]{"DRr01j","_2U7lyt","_hwc3vu","_0Hj","_qoD","[example.net]-19","________","a1b2c3d4e5f60708","5a062bc1-7abc-4def-8123-123456789abc","文件\uFFFD名"})check(NameDetection.Assess(name).Suspect,"suspect not recognized: "+name);
  foreach(string name in new[]{"示例剧集EP1","《示例剧集》 EP06 演员甲 演员乙","sampletitle_-_IT_NEVER_ENDS_Oi4NFN","Jang_Wonyoung_DuRzN9","entoyun_Sentodan_Sentofen","GitHub","OneDrive","PotPlayer","Mahjongsoul","launcher","H264","3DMark","FinalFantasy14","IMG_20261025_123456","DSC_1234","2026-10-25_000001","S01E02","README","qoD","12345","作品の名前","한글 제목"})check(!NameDetection.Assess(name).Suspect,"readable or uncertain name incorrectly selected: "+name);
  check(RenameRules.Suggest("《示例剧集》 EP06 演员甲 演员乙",options,1)=="示例剧集 EP06 演员甲 演员乙","title and cast preserved");
  check(RenameRules.Suggest("示例剧集_EP04_嘉宾甲 嘉宾乙",options,1)=="示例剧集 EP04 嘉宾甲 嘉宾乙","underscores");
  check(RenameRules.Suggest("示例剧集EP1",options,1)=="示例剧集 EP01","single digit episode");
  check(RenameRules.Suggest("示例剧集EP02 演员丙 演员丁",options,1)=="示例剧集 EP02 演员丙 演员丁","zero padded episode");
  check(RenameRules.Suggest("[example.net]-19",options,1)=="19","site prefix");
  check(RenameRules.Suggest("DRr01j",options,1)=="DRr01j","unknown code preserved");
  check(RenameRules.Suggest("sampletitle_-_IT_NEVER_ENDS_Oi4NFN",options,1)=="sampletitle - IT NEVER ENDS Oi4NFN","unknown content preserved");
  check(RenameRules.Suggest("示例剧集 EP01",options,1)=="示例剧集 EP01","rules idempotent");
  options.Title="统一片名";check(RenameRules.Suggest("示例剧集EP3 演员",options,1)=="统一片名 EP03 演员","unified title");
  check(RenameRules.Suggest("DRr01j",options,1)=="DRr01j","title leaves unknown code alone");
  options.NumberAll=true;check(RenameRules.Suggest("DRr01j",options,2)=="统一片名 002","explicit numbering");
  options.Title="";reject(()=>RenameRules.Suggest("DRr01j",options,1),"numbering requires title");
  string dateFixture=Path.Combine(Program.Home,"日期编号测试");Directory.CreateDirectory(dateFixture);DateTime testDate=new DateTime(2026,10,25);
  string datePrefix="2026-10-25_";
  var numberedRows=new List<RenameRow>{new RenameRow{Source=Path.Combine(dateFixture,"random.mp4"),NewName="random"},new RenameRow{Source=Path.Combine(dateFixture,"code.MKV"),NewName="code"},new RenameRow{Source=Path.Combine(dateFixture,"random_folder"),IsDirectory=true,NewName="random_folder"}};
  var names=RenameRules.DateNames(numberedRows,testDate);check(names.SequenceEqual(new[]{datePrefix+"000001",datePrefix+"000002",datePrefix+"000003"}),"date numbering includes mixed extensions and folders");
  File.WriteAllText(Path.Combine(dateFixture,datePrefix+"000010.MP4"),"existing");Directory.CreateDirectory(Path.Combine(dateFixture,datePrefix+"000012"));
  names=RenameRules.DateNames(numberedRows,testDate);check(names.SequenceEqual(new[]{datePrefix+"000013",datePrefix+"000014",datePrefix+"000015"}),"date numbering continues existing maximum across types");
  check(RenameRules.DateNames(numberedRows,testDate.AddDays(1))[0]=="2026-10-26_000001","new date resets sequence");
  numberedRows[1].Include=false;names=RenameRules.DateNames(numberedRows,testDate);check(names[0]==datePrefix+"000013"&&names[1]=="code"&&names[2]==datePrefix+"000014","excluded row keeps its name and consumes no number");numberedRows[1].Include=true;
  var manyRows=Enumerable.Range(0,10005).Select(i=>new RenameRow{Source=Path.Combine(dateFixture,"random"+i+".mp4"),NewName="random"+i}).ToList();
  names=RenameRules.DateNames(manyRows,testDate);check(names.Count==10005&&names.Distinct().Count()==10005&&names.Last()==datePrefix+"010017","ten thousand items have unique padded numbers");
  File.WriteAllText(Path.Combine(dateFixture,datePrefix+"999998.mp4"),"existing");names=RenameRules.DateNames(numberedRows,testDate);check(names.SequenceEqual(new[]{datePrefix+"0999999",datePrefix+"1000000",datePrefix+"1000001"}),"digit width grows past one million without truncation");
  File.WriteAllText(Path.Combine(dateFixture,datePrefix+Int64.MaxValue+".mp4"),"existing");reject(()=>RenameRules.DateNames(numberedRows,testDate),"sequence overflow stops safely");
  string fixture=Path.Combine(Program.Home,"命名测试");Directory.CreateDirectory(fixture);
  string a=Path.Combine(fixture,"示例剧集EP1.mp4"),b=Path.Combine(fixture,"示例剧集_EP02_演员.MKV");File.WriteAllText(a,"video-fixture-a");File.WriteAllText(b,"video-fixture-b");
  string dir=Path.Combine(fixture,"《示例剧集》 EP3");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"inside_original.txt"),"keep");
  var rows=RenameRules.Capture(new[]{a,b,dir});for(int i=0;i<rows.Count;i++)rows[i].NewName=RenameRules.Suggest(rows[i].OriginalStem,new RenameOptions(),i+1);
  check(RenameRules.Validate(rows)&&rows.All(r=>r.Status=="可改名"),"valid preview");
  rows[1].NewName="bad/name";check(!RenameRules.Validate(rows),"invalid path blocked");rows[1].NewName=RenameRules.Suggest(rows[1].OriginalStem,new RenameOptions(),2);
  var duplicate=RenameRules.Capture(new[]{a});duplicate[0].NewName=rows[0].NewName;var duplicateRows=new List<RenameRow>{rows[0],duplicate[0]};check(!RenameRules.Validate(duplicateRows),"duplicates blocked");
  File.WriteAllText(rows[0].Target,"existing");check(!RenameRules.Validate(rows),"existing target blocked");reject(()=>RenameRules.Move(rows[0]),"no overwrite");check(File.ReadAllText(rows[0].Target)=="existing"&&File.Exists(a),"collision preserves both");File.Delete(rows[0].Target);
  var stale=RenameRules.Capture(new[]{b})[0];stale.NewName="changed";File.AppendAllText(b,"updated");reject(()=>RenameRules.Move(stale),"changed file blocked");rows[1].Stamp=Safety.Stamp(b);
  var dialog=RenameDialog(rows);dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();
  check(((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"confirm available");
  string proposed=rows[0].NewName;rows[0].NewName="CON";check(!((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"invalid edit disables execution");rows[0].Include=false;check(((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"unchecking invalid row permits other rows");rows[0].NewName=proposed;rows[0].Include=true;
  var dateOption=(CheckBox)FindNamed(dialog,"RenameDate");var numberedOption=(CheckBox)FindNamed(dialog,"RenameNumbered");numberedOption.IsChecked=true;dateOption.IsChecked=true;check(numberedOption.IsChecked==false&&!((TextBox)FindNamed(dialog,"RenameTitle")).IsEnabled,"date and custom numbering are mutually exclusive");
  ((Button)FindNamed(dialog,"RenameRegenerate")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  string todayPrefix=RenameRules.Today().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"_";
  check(rows.Select(r=>r.NewName).SequenceEqual(new[]{todayPrefix+"000001",todayPrefix+"000002",todayPrefix+"000003"}),"date preview matches chosen mode");
  dialog.UpdateLayout();
  var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"整理命名预览.png")))encoder.Save(stream);dialog.Close();
  check(File.Exists(a)&&Directory.Exists(dir)&&!File.Exists(rows[0].Target),"preview cancellation leaves originals");
  string report=await Task.Run(()=>ExecuteRename(rows));check(rows.All(r=>Safety.Exists(r.Target)&&!Safety.Exists(r.Source)),"batch completed: "+report);
  check(File.ReadAllText(rows[0].Target)=="video-fixture-a"&&File.ReadAllText(rows[1].Target)=="video-fixture-bupdated","file contents unchanged");
  check(Path.GetExtension(rows[1].Target)==".MKV"&&File.Exists(Path.Combine(rows[2].Target,"inside_original.txt")),"extensions and folder contents preserved");
  check(RenameRules.DateNames(rows,RenameRules.Today())[0]==todayPrefix+"000004","subsequent batch continues sequence");
  var log=XElement.Load(Directory.GetFiles(Program.Home,"改名日志-*.xml").Single());check(log.Elements("item").Count()==3&&log.Elements("item").All(e=>(string)e.Attribute("status")=="已完成"),"rename journal");
  string autoFixture=Path.Combine(Program.Home,"自动识别测试");Directory.CreateDirectory(autoFixture);
  string[] autoNames={"DRr01j.mp4","示例剧集EP1.mp4","sampletitle_-_IT_NEVER_ENDS_Oi4NFN.mkv","a1b2c3d4e5f60708.mp4","[example.net]-19.mp4","_hwc3vu.mp4"};
  foreach(string name in autoNames)File.WriteAllText(Path.Combine(autoFixture,name),name);
  var autoRows=RenameRules.Capture(autoNames.Select(n=>Path.Combine(autoFixture,n)));autoRows.Last().BlockedReason="已有相关移动标注";autoRows.Last().Include=false;
  var autoDialog=RenameDialog(autoRows,true);check(autoRows.Count(r=>r.Include)==3,"automatic selection includes only suspected and unblocked rows");check(autoRows.Where(r=>!r.Include).All(r=>r.NewName==r.OriginalStem),"normal titles stay completely unchanged");check(autoRows.Where(r=>r.Include).Select(r=>r.NewName).SequenceEqual(new[]{todayPrefix+"000001",todayPrefix+"000002",todayPrefix+"000003"}),"automatic date numbering applies only to suspects");
  string originalProposal=autoRows[0].NewName;autoRows[0].NewName=todayPrefix+"000050";var previewsBeforeCheck=autoRows.Select(r=>r.NewName).ToArray();
  autoRows[1].Include=true;check(autoRows[1].NewName==todayPrefix+"000051"&&autoRows[1].Status=="可改名"&&autoRows[1].Detection.Contains("手动指定"),"checking a readable title immediately creates a manual date proposal");
  check(autoRows.Where((r,i)=>i!=1).Select(r=>r.NewName).SequenceEqual(previewsBeforeCheck.Where((n,i)=>i!=1)),"manual selection preserves all other edited previews");
  autoRows[1].Include=false;check(autoRows[1].NewName==autoRows[1].OriginalStem&&autoRows[1].Status=="保留原名","unchecking restores the original name");
  autoRows[2].Include=true;check(autoRows[2].NewName==todayPrefix+"000051","released preview numbers can be used without collisions");autoRows[1].Include=true;check(autoRows[1].NewName==todayPrefix+"000052","multiple manual selections get unique preview numbers");
  autoRows[1].Include=false;autoRows[2].Include=false;autoRows[0].NewName=originalProposal;
  autoRows[5].Include=true;check(autoRows[5].Status.Contains("标注")&&!((Button)FindNamed(autoDialog,"RenameExecute")).IsEnabled,"manual selection cannot bypass protected rows");autoRows[5].Include=false;
  autoDialog.WindowStartupLocation=WindowStartupLocation.Manual;autoDialog.Left=-3000;autoDialog.Top=-3000;autoDialog.Show();await Task.Delay(150);autoDialog.UpdateLayout();
  var autoGrid=(DataGrid)FindNamed(autoDialog,"RenameGrid");
  Action<int,int> clickRow=(rowIndex,columnIndex)=>{var content=autoGrid.Columns[columnIndex].GetCellContent(autoRows[rowIndex]);check(content!=null,"row click test cell exists");var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=content};autoGrid.RaiseEvent(click);check(click.Handled,"row click handled without inner checkbox interaction");};
  clickRow(1,1);check(autoRows[1].Include&&autoRows[1].Status=="可改名","clicking original-name area toggles row on");
  clickRow(1,5);check(!autoRows[1].Include&&autoRows[1].NewName==autoRows[1].OriginalStem,"clicking status area toggles row off");
  clickRow(1,2);check(autoRows[1].Include,"single click new-name area toggles row on");
  clickRow(1,0);check(!autoRows[1].Include,"checkbox indicator area toggles exactly once");
  var checkboxContent=autoGrid.Columns[0].GetCellContent(autoRows[1]);var checkbox=checkboxContent as CheckBox??VisualTreeHelper.GetChild(checkboxContent,0) as CheckBox;check(checkbox!=null&&!checkbox.IsHitTestVisible&&!checkbox.Focusable,"indicator has no nested click or keyboard target");
  var autoVisual=(FrameworkElement)autoDialog.Content;var autoImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)autoVisual.ActualWidth,(int)autoVisual.ActualHeight,96,96,PixelFormats.Pbgra32);autoImage.Render(autoVisual);var autoEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();autoEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(autoImage));using(var stream=File.Create(Path.Combine(Program.Home,"自动识别命名预览.png")))autoEncoder.Save(stream);autoDialog.Close();
  check(autoRows.All(r=>File.Exists(r.Source)),"automatic preview leaves originals unchanged");
  report=await Task.Run(()=>ExecuteRename(autoRows.Where(r=>r.Include).ToList()));check(autoRows.Where(r=>r.Include).All(r=>File.Exists(r.Target)&&!File.Exists(r.Source)),"suspect-only execution: "+report);check(autoRows.Where(r=>!r.Include).All(r=>File.ReadAllText(r.Source)==r.Original),"normal and blocked files preserved during suspect-only execution");
  var normalOnly=RenameRules.Capture(new[]{Path.Combine(autoFixture,"示例剧集EP1.mp4")});var noSuspects=RenameDialog(normalOnly,true);check(!normalOnly[0].Include&&!((Button)FindNamed(noSuspects,"RenameExecute")).IsEnabled,"normal-only folder does not enable execution");noSuspects.Close();
  string tumblr=Path.Combine(autoFixture,"tumblr_od6h76nGDx1vze..._r1_480.mp4");File.WriteAllText(tumblr,"manual-rename-fixture");var manualRows=RenameRules.Capture(new[]{tumblr});var manualDialog=RenameDialog(manualRows,true);
  check(!manualRows[0].Include&&manualRows[0].NewName==manualRows[0].OriginalStem,"readable prefix initially retained");manualRows[0].Include=true;
  check(manualRows[0].NewName==todayPrefix+"000004"&&manualRows[0].Status=="可改名"&&((Button)FindNamed(manualDialog,"RenameExecute")).IsEnabled,"manual checkbox overrides auto retention and skips existing date files");manualDialog.Close();
  report=await Task.Run(()=>ExecuteRename(manualRows));check(File.Exists(manualRows[0].Target)&&!File.Exists(tumblr)&&File.ReadAllText(manualRows[0].Target)=="manual-rename-fixture","manual checkbox proposal really renames the file: "+report);
  string unchanged=Path.Combine(autoFixture,"launcher.txt");File.WriteAllText(unchanged,"keep");var unchangedRows=RenameRules.Capture(new[]{unchanged});var unchangedDialog=RenameDialog(unchangedRows);check(unchangedRows[0].Status=="待设置新名称"&&!((Button)FindNamed(unchangedDialog,"RenameExecute")).IsEnabled,"checked unchanged row requests a new name rather than being silently ignored");unchangedDialog.Close();
  cards.SelectedItems.Clear();UpdateSelection();check(E<Button>("RenameButton").IsEnabled,"automatic rename available with no selection");
  await PrefixSmokeTest();
 }
 async Task PrefixSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Prefixes: "+message);};
  var originalPrefixes=renamePrefixes.ToList();var rules=RenameRules.ParsePrefixes("www.example.com@\r\nWWW.EXAMPLE.COM@\n\nwww.example.com@VIP-\nliteral.[x]@");
  check(rules.Count==3,"blank lines and case-insensitive duplicates removed");
  check(RenameRules.StripPrefix("WWW.EXAMPLE.COM@标题_后文",rules)=="标题_后文","literal case-insensitive prefix only");
  check(RenameRules.StripPrefix("www.example.com@VIP-标题",rules)=="标题","longest prefix wins");
  check(RenameRules.StripPrefix("片名www.example.com@后文",rules)=="片名www.example.com@后文","matching is anchored at beginning");
  check(RenameRules.StripPrefix("literal.[x]@片名",rules)=="片名"&&RenameRules.StripPrefix("literal.ax@片名",rules)=="literal.ax@片名","prefixes are literal, not regex");
  check(RenameRules.StripPrefix("www.example.com@www.example.com@标题",rules)=="www.example.com@标题","only one leading occurrence removed");
  SaveRenamePrefixes(rules);var restored=new ModernWindow();check(restored.renamePrefixes.SequenceEqual(rules),"custom prefix list restored after reopening");restored.Window.Close();
  SaveRenamePrefixes(new List<string>());restored=new ModernWindow();check(restored.renamePrefixes.Count==0,"explicitly cleared list remains empty");restored.Window.Close();SaveRenamePrefixes(rules);
  string fixture=Path.Combine(Program.Home,"前缀清理测试");Directory.CreateDirectory(fixture);
  string[] names={"www.example.com@[测试标题]人物_后文.MP4","WWW.EXAMPLE.COM@Classic Dress Styles in Fashion - A Vision.mp4","普通片名www.example.com@后文.mp4","www.example.com@.mp4"};foreach(string name in names)File.WriteAllText(Path.Combine(fixture,name),name);
  var rows=RenameRules.Capture(names.Select(n=>Path.Combine(fixture,n)));var dialog=RenameDialog(rows,true);
  check(((CheckBox)FindNamed(dialog,"RenamePrefixOnly")).IsChecked==true&&((CheckBox)FindNamed(dialog,"RenameDate")).IsChecked!=true,"known prefixes select title-preserving mode instead of date mode");
  check(rows[0].NewName=="[测试标题]人物_后文"&&rows[1].NewName=="Classic Dress Styles in Fashion - A Vision"&&!rows[2].Include&&rows[2].NewName==rows[2].OriginalStem,"preview preserves trailing titles exactly and leaves other files alone");
  string fallbackPrefix=RenameRules.Today().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"_";
  check(rows[3].Include&&rows[3].NewName==fallbackPrefix+"000001"&&rows[3].EmptyNameFallback&&((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"prefix-only filename automatically receives a date fallback");rows[3].Include=false;check(rows[3].NewName==rows[3].OriginalStem&&!rows[3].EmptyNameFallback,"excluded empty-name row retains original name");rows[3].Include=true;
  check(rows[3].NewName==fallbackPrefix+"000001"&&rows[3].EmptyNameFallback&&((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"manually rechecking empty name also generates date fallback");
  dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();
  var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"自定义前缀清理预览.png")))encoder.Save(stream);dialog.Close();
  check(rows.All(r=>File.Exists(r.Source)),"prefix preview leaves actual files untouched");
  string report=await Task.Run(()=>ExecuteRename(rows.Where(r=>r.Include).ToList()));check(rows.Where(r=>r.Include).All(r=>File.Exists(r.Target)&&!File.Exists(r.Source)&&File.ReadAllText(r.Target)==r.Original),"prefix cleanup really renames files and keeps contents: "+report);
  check(rows[0].Extension==".MP4"&&rows.Where(r=>!r.Include).All(r=>File.Exists(r.Source)),"extensions and excluded filenames retained");
  SaveRenamePrefixes(originalPrefixes);
  await RecursivePrefixSmokeTest();await EmptyPrefixFallbackSmokeTest();await RenameProblemsSmokeTest();await PrefixCollisionSmokeTest();
 }
 async Task PrefixCollisionSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Prefix collision numbering: "+message);};
  var previous=renamePrefixes;renamePrefixes=new List<string>{"www.example.com@","example.org@","literal.[x]@"};string fixture=Path.Combine(Program.Home,"前缀重名编号测试");Directory.CreateDirectory(fixture);
  File.WriteAllText(Path.Combine(fixture,"film.mp4"),"existing-base");File.WriteAllText(Path.Combine(fixture,"film (1).mp4"),"existing-one");Directory.CreateDirectory(Path.Combine(fixture,"film (2).mp4"));
  string a=Path.Combine(fixture,"www.example.com@film.mp4"),b=Path.Combine(fixture,"example.org@film.MP4"),natural=Path.Combine(fixture,"www.example.com@film (3).mp4"),otherExt=Path.Combine(fixture,"www.example.com@film.mkv"),caseVariant=Path.Combine(fixture,"literal.[x]@FILM.mp4");foreach(string path in new[]{a,b,natural,otherExt,caseVariant})File.WriteAllText(path,Path.GetFileName(path));
  string existingFolder=Path.Combine(fixture,"合集");Directory.CreateDirectory(existingFolder);File.WriteAllText(Path.Combine(existingFolder,"keep.txt"),"keep-existing-folder");Directory.CreateDirectory(Path.Combine(fixture,"合集 (1)"));
  string parent=Path.Combine(fixture,"www.example.com@合集");Directory.CreateDirectory(parent);string child=Path.Combine(parent,"www.example.com@片名.mp4");File.WriteAllText(child,"nested-video");File.WriteAllText(Path.Combine(parent,"片名.mp4"),"existing-nested-video");
  var roots=new[]{a,b,natural,otherExt,caseVariant,parent};var rows=ScanRenameRows(roots,false,queue.Keys.ToArray(),System.Threading.CancellationToken.None);var dialog=RenameDialog(rows,true);
  check(rows.Single(r=>r.Source==a).NewName=="film (4)"&&rows.Single(r=>r.Source==b).NewName=="film (5)"&&rows.Single(r=>r.Source==caseVariant).NewName=="FILM (6)","existing files, directory leaves and case variants reserve suffix numbers");
  check(rows.Single(r=>r.Source==natural).NewName=="film (3)"&&rows.Single(r=>r.Source==otherExt).NewName=="film"&&rows.Single(r=>r.Source==parent).NewName=="合集 (2)","natural numbered titles, different extensions and folders are preserved correctly");
  var first=rows.Single(r=>r.Source==a);var second=rows.Single(r=>r.Source==b);second.NewName="film (9)";first.Include=false;first.Include=true;check(first.NewName=="film (4)"&&second.NewName=="film (9)"&&first.DuplicateSuffixAdded,"manual inclusion numbers only that row and preserves other edits");
  var recursive=(CheckBox)FindNamed(dialog,"RenameRecursivePrefixes");recursive.IsChecked=true;var grid=(DataGrid)FindNamed(dialog,"RenameGrid");for(int i=0;i<200&&!grid.IsEnabled;i++)await Task.Delay(25);check(grid.IsEnabled&&rows.Single(r=>r.Source==child).NewName=="片名 (1)","recursive preview numbers nested collisions as well");
  check(rows.All(r=>!r.IsProblem)&&((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"generated collision suffixes clear validation errors");
  first=rows.Single(r=>r.Source==a);string late=first.Target;File.WriteAllText(late,"late-arriving-file");check(!RenameRules.Validate(rows)&&first.IsProblem,"new collision after preview still blocks overwriting");
  ((Button)FindNamed(dialog,"RenameRegenerate")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(!rows.Any(r=>r.IsProblem)&&rows.Single(r=>r.Source==a).NewName=="film (5)","regenerating preview allocates a new suffix after late collisions");
  dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"去前缀重名自动编号预览.png")))encoder.Save(stream);dialog.Close();
  var plans=rows.Where(r=>r.Include).ToList();var final=plans.ToDictionary(r=>r.Source,r=>FinalRenameTarget(r,plans));string report=await Task.Run(()=>ExecuteRename(plans));check(final.Values.All(Safety.Exists),"numbered files and nested folders actually rename: "+report);
  check(File.ReadAllText(final[a])==Path.GetFileName(a)&&File.ReadAllText(final[b])==Path.GetFileName(b)&&Path.GetExtension(final[b])==".MP4"&&File.ReadAllText(final[child])=="nested-video","renamed contents and extension casing stay unchanged");
  check(File.ReadAllText(Path.Combine(fixture,"film.mp4"))=="existing-base"&&File.ReadAllText(Path.Combine(fixture,"film (1).mp4"))=="existing-one"&&Directory.Exists(Path.Combine(fixture,"film (2).mp4"))&&File.ReadAllText(late)=="late-arriving-file"&&File.ReadAllText(Path.Combine(existingFolder,"keep.txt"))=="keep-existing-folder","existing files and directories are never overwritten or merged");
  check(File.ReadAllText(Path.Combine(final[parent],"片名.mp4"))=="existing-nested-video","existing file inside a renamed folder keeps its original name and contents");
  var many=Enumerable.Range(0,10005).Select(i=>new RenameRow{Source=Path.Combine(fixture,"www.example.com@source"+i+".mp4"),NewName="source"+i}).ToList();var generated=RenameRules.NumberPrefixCollisions(many,many.Select(r=>"bulk").ToList(),renamePrefixes);check(generated[0]=="bulk"&&generated.Last()=="bulk (10004)"&&generated.Distinct(StringComparer.OrdinalIgnoreCase).Count()==10005,"ten thousand generated duplicates get unique unbounded suffix numbers");renamePrefixes=previous;
 }
 async Task RenameProblemsSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Problem locating: "+message);};
  string fixture=Path.Combine(Program.Home,"问题定位测试");Directory.CreateDirectory(fixture);var paths=Enumerable.Range(0,100).Select(i=>Path.Combine(fixture,"普通文件"+i.ToString("D3")+".txt")).ToList();foreach(string path in paths)File.WriteAllText(path,Path.GetFileName(path));File.WriteAllText(Path.Combine(fixture,"occupied.txt"),"existing");
  var rows=RenameRules.Capture(paths);var dialog=RenameDialog(rows);((CheckBox)FindNamed(dialog,"RenameDate")).IsChecked=true;((Button)FindNamed(dialog,"RenameRegenerate")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  rows[60].NewName="occupied";rows[75].NewName="bad:name";rows[81].NewName="trailing.";rows[90].NewName="duplicate";rows[91].NewName="duplicate";rows[96].NewName=rows[96].OriginalStem;rows[99].BlockedReason="已有相关移动标注";rows[99].NewName=rows[99].NewName;
  check(rows.Count(r=>r.IsProblem)==7&&rows[75].Status.Contains(":")&&rows[81].Status.Contains("末尾有句点"),"all problem types have specific reasons");
  var grid=(DataGrid)FindNamed(dialog,"RenameGrid");var filter=(CheckBox)FindNamed(dialog,"RenameOnlyProblems");var next=(Button)FindNamed(dialog,"RenameNextProblem");
  check(((Border)FindNamed(dialog,"RenameProblemBar")).Visibility==Visibility.Visible&&((TextBlock)FindNamed(dialog,"RenameProblemCount")).Text.Contains("7")&&!((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"problem banner shows exact blocking count");
  dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();filter.IsChecked=true;check(grid.Items.Count==7,"problem-only filter hides normal rows");
  next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Object.ReferenceEquals(grid.SelectedItem,rows[60])&&rows[60].Include,"locating scrolls to buried conflict without toggling it");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Object.ReferenceEquals(grid.SelectedItem,rows[75])&&rows[75].Include,"next locator cycles through problems");
  var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"改名问题定位.png")))encoder.Save(stream);
  grid.CurrentCell=new DataGridCellInfo(rows[75],grid.Columns[2]);check(grid.BeginEdit(),"problem new-name cell can be edited");grid.UpdateLayout();var editor=grid.Columns[2].GetCellContent(rows[75]) as TextBox;check(editor!=null,"problem row editor available");editor.Text="合法新名";grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);await Task.Delay(50);check(!rows[75].IsProblem&&rows.Count(r=>r.IsProblem)==6,"editing fixes a problem without refreshing during edit transaction");
  next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);check(grid.Items.Count==6,"resolved row disappears after moving to another problem");
  RenameRow located=null;var detail=RenameProblemsDialog(dialog,()=>rows.Where(r=>r.IsProblem).ToList(),r=>located=r);detail.WindowStartupLocation=WindowStartupLocation.Manual;detail.Left=-3000;detail.Top=-3000;detail.Show();await Task.Delay(150);detail.UpdateLayout();
  check(((DataGrid)FindNamed(detail,"RenameProblemsGrid")).Items.Count==6&&((TextBox)FindNamed(detail,"RenameProblemDetail")).Text.Contains(rows[60].Source)&&((TextBox)FindNamed(detail,"RenameProblemDetail")).Text.Contains(rows[60].Target)&&((TextBox)FindNamed(detail,"RenameProblemDetail")).Text.Contains("同名目标已存在"),"problem detail shows full original path, target and reason");
  visual=(FrameworkElement)detail.Content;image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"改名问题清单.png")))encoder.Save(stream);
  check(RenameRules.ProblemReport(rows).Contains(rows[99].Source)&&RenameRules.ProblemReport(rows).Contains("已有相关移动标注"),"copyable report contains every failure");
  ((Button)FindNamed(detail,"RenameSkipSelectedProblem")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(!rows[60].Include&&rows.Count(r=>r.IsProblem)==5,"skip one problem excludes only that item");((Button)FindNamed(detail,"RenameLocateSelectedProblem")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(located!=null&&located.IsProblem,"detail can return a selected problem to the main preview");
  ((Button)FindNamed(dialog,"RenameSkipProblems")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(!rows.Any(r=>r.IsProblem)&&grid.Items.Count==0&&((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"skip all problems leaves valid plans enabled");filter.IsChecked=false;check(grid.Items.Count==100&&rows.Count(r=>r.Include)==94,"all preview rows remain available after excluding problems");dialog.Close();
  check(paths.All(p=>File.ReadAllText(p)==Path.GetFileName(p))&&File.ReadAllText(Path.Combine(fixture,"occupied.txt"))=="existing","diagnosis, filtering and skipping do not rename or overwrite files");
 }
 async Task EmptyPrefixFallbackSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Empty prefix fallback: "+message);};
  var previous=renamePrefixes;renamePrefixes=new List<string>{"www.example.com@"};string prefix=RenameRules.Today().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"_";
  string fixture=Path.Combine(Program.Home,"空名前缀回退测试");Directory.CreateDirectory(fixture);File.WriteAllText(Path.Combine(fixture,prefix+"000005.MKV"),"existing");Directory.CreateDirectory(Path.Combine(fixture,prefix+"000007"));
  string emptyFile=Path.Combine(fixture,"www.example.com@.mp4"),emptyFolder=Path.Combine(fixture,"www.example.com@"),datedTitle=Path.Combine(fixture,"www.example.com@"+prefix+"000010.mp4");Directory.CreateDirectory(emptyFolder);File.WriteAllText(emptyFile,"empty-file");File.WriteAllText(datedTitle,"title-with-date");
  string nested=Path.Combine(emptyFolder,"[example.net]-.MKV"),unchanged=Path.Combine(emptyFolder,prefix+"000020.mp4");File.WriteAllText(nested,"empty-nested-file");File.WriteAllText(unchanged,"untouched");
  var rows=ScanRenameRows(new[]{emptyFile,emptyFolder,datedTitle},true,queue.Keys.ToArray(),System.Threading.CancellationToken.None);var dialog=RenameDialog(rows,true);
  var file=rows.Single(r=>r.Source==emptyFile);var dir=rows.Single(r=>r.Source==emptyFolder);var child=rows.Single(r=>r.Source==nested);var title=rows.Single(r=>r.Source==datedTitle);
  check(file.NewName==prefix+"000011"&&dir.NewName==prefix+"000012"&&child.NewName==prefix+"000021","fallback skips existing and planned numbers independently in each directory");
  check(file.EmptyNameFallback&&dir.EmptyNameFallback&&child.EmptyNameFallback&&!title.EmptyNameFallback&&title.NewName==prefix+"000010","only truly empty names use fallback");
  check(rows.Where(r=>r.Include).All(r=>r.Status=="可改名")&&((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"empty names no longer block a prefix cleanup batch");
  file.Include=false;file.Include=true;check(file.NewName==prefix+"000013"&&dir.NewName==prefix+"000012"&&title.NewName==prefix+"000010","manual fallback reserves other preview numbers without changing their names");
  ((Button)FindNamed(dialog,"RenameRegenerate")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(file.NewName==prefix+"000011","regenerating preview remains deterministic");
  dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"空名自动补日期预览.png")))encoder.Save(stream);dialog.Close();
  var plans=rows.Where(r=>r.Include).ToList();var final=plans.ToDictionary(r=>r.Source,r=>FinalRenameTarget(r,plans));string report=await Task.Run(()=>ExecuteRename(plans));
  check(final.Values.All(Safety.Exists)&&File.ReadAllText(final[emptyFile])=="empty-file"&&File.ReadAllText(final[nested])=="empty-nested-file","nested empty folders and files can really be renamed: "+report);
  check(Path.GetExtension(final[nested])==".MKV"&&File.ReadAllText(Path.Combine(final[emptyFolder],prefix+"000020.mp4"))=="untouched"&&File.ReadAllText(Path.Combine(fixture,prefix+"000005.MKV"))=="existing","fallback preserves extensions, untouched names, and existing files");
  var whitespaceRows=new List<RenameRow>{new RenameRow{Source=Path.Combine(fixture,"sample.mp4"),NewName="sample"}};var whitespace=RenameRules.FillEmptyNames(whitespaceRows,new List<string>{"  "},RenameRules.Today());check(whitespace[0]==prefix+"000013","whitespace-only result also receives the next available date number");
  file.Status="同名目标已存在";string detail=RenameRules.ProblemSummary(new List<RenameRow>{file});check(detail.Contains(file.Original)&&detail.Contains("同名目标已存在"),"other validation problems identify the actual filename and reason");renamePrefixes=previous;
 }
 async Task RecursivePrefixSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Recursive prefixes: "+message);};
  var originalPrefixes=renamePrefixes.ToList();renamePrefixes=new List<string>{"www.example.com@"};
  string fixture=Path.Combine(Program.Home,"子目录清理测试"),outer=Path.Combine(fixture,"www.example.com@外层"),inner=Path.Combine(outer,"www.example.com@内层"),normal=Path.Combine(fixture,"普通目录"),blocked=Path.Combine(fixture,"已标注目录");Directory.CreateDirectory(inner);Directory.CreateDirectory(normal);Directory.CreateDirectory(blocked);
  string video=Path.Combine(inner,"www.example.com@视频_演员.mp4"),bracket=Path.Combine(inner,"[example.net]-片名_人物.MKV"),untouched=Path.Combine(inner,"普通名字.mp4"),top=Path.Combine(fixture,"www.example.com@顶层.mp4"),duplicate=Path.Combine(normal,"www.example.com@视频_演员.mp4"),marked=Path.Combine(blocked,"www.example.com@保留.mp4");
  foreach(string path in new[]{video,bracket,untouched,top,duplicate,marked})File.WriteAllText(path,Path.GetFileName(path));
  queue[marked]=new MovePlan{Source=marked,Target=Path.Combine(root,"标注保留",Path.GetFileName(marked)),Stamp=Safety.Stamp(marked)};
  var roots=Directory.GetFileSystemEntries(fixture);var flat=ScanRenameRows(roots,false,queue.Keys.ToArray(),System.Threading.CancellationToken.None);check(flat.Count==roots.Length&&!flat.Any(r=>r.Source==video),"nonrecursive scan includes only original scope");
  var deep=ScanRenameRows(roots.Concat(new[]{inner}),true,queue.Keys.ToArray(),System.Threading.CancellationToken.None);check(deep.Count==9&&deep.Select(r=>r.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()==9,"recursive scan walks all levels and deduplicates overlapping roots");check(!deep.Any(r=>r.Source==marked)&&deep.Single(r=>r.Source==blocked).BlockedReason!=null,"marked subtree is skipped");
  var rows=flat;var dialog=RenameDialog(rows,true);var recursive=(CheckBox)FindNamed(dialog,"RenameRecursivePrefixes");var grid=(DataGrid)FindNamed(dialog,"RenameGrid");check(recursive.IsChecked==false&&rows.Count==4,"recursive scope is off by default");
  Func<Task> settle=async ()=>{for(int i=0;i<200&&!grid.IsEnabled;i++)await Task.Delay(25);check(grid.IsEnabled,"scope scan completes");};
  recursive.IsChecked=true;check(!((Button)FindNamed(dialog,"RenameExecute")).IsEnabled,"execution is disabled during scanning");await settle();
  check(rows.Count==9&&rows.Count(r=>r.Include)==6&&grid.Columns.Last().Visibility==Visibility.Visible,"recursive preview includes locations and only prefix matches");
  check(rows.Single(r=>r.Source==top).Location=="."&&rows.Single(r=>r.Source==video).Location==Path.Combine(Path.GetFileName(outer),Path.GetFileName(inner))&&rows.Single(r=>r.Source==video).FullLocation==inner,"relative locations distinguish nested files with complete path available");
  check(rows.Single(r=>r.Source==bracket).NewName=="片名_人物"&&rows.Single(r=>r.Source==video).NewName=="视频_演员","recursive cleanup supports website brackets and custom prefixes while preserving titles");
  check(((CheckBox)FindNamed(dialog,"RenameDate")).IsEnabled==false&&((CheckBox)FindNamed(dialog,"RenamePrefixOnly")).IsChecked==true,"recursive scope applies only to prefix cleanup");
  recursive.IsChecked=false;await settle();check(rows.Count==4&&rows.Count(r=>r.Include)==2&&grid.Columns.Last().Visibility==Visibility.Collapsed,"turning recursion off removes descendant plans");
  recursive.IsChecked=true;recursive.IsChecked=false;await settle();await Task.Delay(80);check(rows.Count==4,"canceled old scan cannot overwrite the latest scope");
  recursive.IsChecked=true;await settle();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-3000;dialog.Top=-3000;dialog.Show();await Task.Delay(150);dialog.UpdateLayout();
  var visual=(FrameworkElement)dialog.Content;var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"子文件夹前缀清理预览.png")))encoder.Save(stream);dialog.Close();check(File.Exists(video)&&Directory.Exists(inner),"recursive preview does not rename actual files");
  var plans=rows.Where(r=>r.Include).ToList();var finalPaths=plans.ToDictionary(r=>r.Source,r=>FinalRenameTarget(r,plans));string report=await Task.Run(()=>ExecuteRename(plans));check(finalPaths.Values.All(Safety.Exists),"child-first parent and child renames complete: "+report);
  check(File.ReadAllText(finalPaths[video])==Path.GetFileName(video)&&File.ReadAllText(finalPaths[bracket])==Path.GetFileName(bracket)&&Path.GetExtension(finalPaths[bracket])==".MKV","nested file contents and extension casing preserved");
  check(File.Exists(Path.Combine(fixture,"外层","内层","普通名字.mp4"))&&File.Exists(marked)&&queue.ContainsKey(marked),"unmatched file keeps its name and marked subtree stays untouched");
  check(File.Exists(Path.Combine(normal,"视频_演员.mp4")),"same cleaned basename is allowed in another directory");
  string logPath=Directory.GetFiles(Program.Home,"改名日志-*.xml").OrderByDescending(File.GetLastWriteTimeUtc).First();var entries=XElement.Load(logPath).Elements("item").ToList();check(entries.Count==6&&entries.All(e=>(string)e.Attribute("status")=="已完成"&&Safety.Exists((string)e.Attribute("finalTarget"))),"recursive journal records actual final paths");
  check(entries.FindIndex(e=>(string)e.Attribute("source")==video)<entries.FindIndex(e=>(string)e.Attribute("source")==inner)&&entries.FindIndex(e=>(string)e.Attribute("source")==inner)<entries.FindIndex(e=>(string)e.Attribute("source")==outer),"journal confirms deepest-first execution");
  string collision=Path.Combine(fixture,"冲突测试");Directory.CreateDirectory(collision);string a=Path.Combine(collision,"www.example.com@重名.txt"),b=Path.Combine(collision,"重名.txt");File.WriteAllText(a,"source");File.WriteAllText(b,"existing");var collisionRows=RenameRules.Capture(new[]{a});collisionRows[0].NewName=RenameRules.StripWebsitePrefix(collisionRows[0].OriginalStem,renamePrefixes);report=await Task.Run(()=>ExecuteRename(collisionRows));check(File.ReadAllText(a)=="source"&&File.ReadAllText(b)=="existing","recursive cleanup collision does not overwrite files");
  using(var cancel=new System.Threading.CancellationTokenSource()){cancel.Cancel();bool canceled=false;try{ScanRenameRows(new[]{fixture},true,queue.Keys.ToArray(),cancel.Token);}catch(OperationCanceledException){canceled=true;}check(canceled,"scanning can be canceled");}
  queue.Remove(marked);renamePrefixes=originalPrefixes;Save();
 }
}
