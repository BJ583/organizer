using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

partial class ModernWindow {
 Dictionary<string,long> categoryChoiceCounts=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
 long CategoryChoiceCount(string path){long count;return categoryChoiceCounts.TryGetValue(Safety.Destination(root,path),out count)?count:0;}
 void RecordSuccessfulCategoryMove(string full){full=Path.GetFullPath(full);long count;categoryChoiceCounts.TryGetValue(full,out count);categoryChoiceCounts[full]=count==Int64.MaxValue?count:count+1;}
 List<string> HistoricalCategoryPaths(){string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";return categoryChoiceCounts.Where(p=>p.Value>0&&p.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key.Substring(prefix.Length)).Where(p=>categories.Contains(p,StringComparer.OrdinalIgnoreCase)||ExistingCategoryFolder(p)).ToList();}
 List<string> FrequentCategoryDescendants(string top){string ignored;var direct=CategoryChildren(top,out ignored);return HistoricalCategoryPaths().Concat(categories).Concat(direct).Concat(discoveredCategoryFolders.Where(ExistingCategoryFolder)).Where(p=>p.StartsWith(top+"\\",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(CategoryChoiceCount).Take(2).ToList();}
 HashSet<string> expandedCategoryFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 HashSet<string> discoveredCategoryFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 string categoryFolderRoot="";
 void CheckCategoryRoot(){if(!String.Equals(categoryFolderRoot,root,StringComparison.OrdinalIgnoreCase)){expandedCategoryFolders.Clear();discoveredCategoryFolders.Clear();categoryFolderRoot=root;}}
 bool ExistingCategoryFolder(string relative){try{string path=Safety.Destination(root,relative);Safety.NoLinks(path);return Directory.Exists(path);}catch{return false;}}
 List<string> AvailableCategoryPaths(){CheckCategoryRoot();return categories.Concat(discoveredCategoryFolders.Where(ExistingCategoryFolder)).Concat(pinnedCategories.Where(ExistingCategoryFolder)).Concat(HistoricalCategoryPaths()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
 List<string> CategoryChildren(string relative,out string error){
  CheckCategoryRoot();error=null;var children=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string prefix=relative.Length==0?"":relative+"\\";
  foreach(string path in categories){if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;string tail=path.Substring(prefix.Length);if(tail.Length>0)children.Add(prefix+tail.Split('\\')[0]);}
  try{
   string directory=relative.Length==0?Path.GetFullPath(root):Safety.Destination(root,relative);Safety.NoLinks(directory);
   if(Directory.Exists(directory))foreach(string path in Directory.EnumerateDirectories(directory)){
    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;string child=prefix+Path.GetFileName(path);Safety.Destination(root,child);children.Add(child);discoveredCategoryFolders.Add(child);
   }
  }catch(Exception e){error=e.Message;}
  var preferred=categories.Where(p=>p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&p.Length>prefix.Length).Select(p=>prefix+p.Substring(prefix.Length).Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();var result=preferred.Concat(children.Except(preferred,StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase));return (relative.Length==0?result:result.OrderByDescending(CategoryChoiceCount)).ToList();
 }
 List<string> CategoryRoots(){string ignored;return CategoryChildren("",out ignored);}
 void RenderCategories(){
  CheckCategoryRoot();var panel=E<StackPanel>("CategoryPanel");panel.Children.Clear();var pins=E<WrapPanel>("PinnedPanel");pins.Children.Clear();string query=E<TextBox>("CategorySearch").Text.Trim();
  foreach(string path in pinnedCategories.Where(c=>(categories.Contains(c,StringComparer.OrdinalIgnoreCase)||ExistingCategoryFolder(c))&&CategoryMatches(c,query)))pins.Children.Add(CategoryChip(path,true));
  E<System.Windows.Controls.Border>("PinSection").Visibility=pins.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
  string error;var roots=CategoryChildren("",out error);var matches=AvailableCategoryPaths().Where(c=>CategoryMatches(c,query)).ToList();
  var compact=new CompactGroupPanel();
  foreach(string path in roots.Where(c=>group.Length==0||String.Equals(c,group,StringComparison.OrdinalIgnoreCase))){
   if(query.Length>0&&!CategoryBranchMatches(path,matches))continue;
   var block=new System.Windows.Controls.Border{Child=CategoryFolderNode(path,query,matches,0),Background=(Brush)new BrushConverter().ConvertFromString("#F8F7FC"),BorderBrush=(Brush)new BrushConverter().ConvertFromString("#EBE7F3"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(8,5,5,2),Margin=new Thickness(0,0,7,8),MaxWidth=550};compact.Children.Add(block);
  }
  if(compact.Children.Count>0)panel.Children.Add(compact);
  if(error!=null)panel.Children.Add(new TextBlock{Text="目录读取失败："+error,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick});
  if(compact.Children.Count==0&&pins.Children.Count==0&&error==null)panel.Children.Add(new TextBlock{Text="没有匹配的分类。可点击“刷新目录”读取新建文件夹。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(4,12,0,0)});
  E<TextBlock>("CategoryHint").Text="默认显示常用前两项 · 展开按成功移动次数排序 · 同一批同一分类计一次";
 }
 static bool CategoryBranchMatches(string path,List<string> matches){return matches.Any(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase)||p.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase));}
 FrameworkElement CategoryFolderNode(string path,string query,List<string> matches,int depth){
  var node=new StackPanel{Margin=new Thickness(depth==0?0:12,0,0,0)};var header=new StackPanel{Orientation=Orientation.Horizontal};node.Children.Add(header);
  var label=CategoryChip(path,false,true);string alias;string text=categoryAliases.TryGetValue(path,out alias)?alias:Clean(path.Split('\\').Last());label.Content=new TextBlock{Text=text,TextTrimming=TextTrimming.CharacterEllipsis};label.MaxWidth=300;if(depth==0){label.FontWeight=FontWeights.SemiBold;label.Background=Brushes.Transparent;}header.Children.Add(label);
  var arrow=new Button{Content="▸",Tag="expand:"+path,Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,0,3,5),MinWidth=28,ToolTip="展开下一层："+Safety.Destination(root,path)};header.Children.Add(arrow);
  var frequent=new WrapPanel{Margin=new Thickness(4,0,0,0),MaxWidth=500};if(depth==0){foreach(string favorite in FrequentCategoryDescendants(path)){var shortcut=CategoryChip(favorite,false,true);shortcut.Content=new TextBlock{Text=ShortCategory(favorite),MaxWidth=90,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};shortcut.MaxWidth=110;shortcut.ToolTip="常用目录："+DisplayCategory(favorite)+"\n目标："+Safety.Destination(root,favorite);frequent.Children.Add(shortcut);}node.Children.Add(frequent);}
  var childrenPanel=new StackPanel();node.Children.Add(childrenPanel);bool opened=false;
  Action open=()=>{
   string error;var children=CategoryChildren(path,out error);childrenPanel.Children.Clear();
   foreach(string child in children){if(query.Length>0&&!CategoryBranchMatches(child,matches)&&!CategoryMatches(path,query))continue;childrenPanel.Children.Add(CategoryFolderNode(child,query,matches,depth+1));}
   if(error!=null)childrenPanel.Children.Add(new TextBlock{Text="无法读取："+error,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick,Margin=new Thickness(15,0,5,6),MaxWidth=320});
   else if(childrenPanel.Children.Count==0)childrenPanel.Children.Add(new TextBlock{Text="没有下一级文件夹",Foreground=Brushes.SlateGray,FontSize=11,Margin=new Thickness(15,0,5,6)});
   opened=true;expandedCategoryFolders.Add(path);frequent.Visibility=Visibility.Collapsed;arrow.Content="▾";arrow.ToolTip="收起；再次展开会重新读取目录";
  };
  arrow.Click+=delegate{if(opened){childrenPanel.Children.Clear();opened=false;frequent.Visibility=Visibility.Visible;expandedCategoryFolders.RemoveWhere(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase)||p.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase));arrow.Content="▸";arrow.ToolTip="展开并读取下一层目录";}else open();};
  if(expandedCategoryFolders.Contains(path)||(query.Length>0&&matches.Any(p=>p.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase))))open();
  return node;
 }
 static IEnumerable<Button> CategoryButtons(DependencyObject node){var element=node as UIElement;if(element!=null&&element.Visibility!=Visibility.Visible)yield break;var button=node as Button;if(button!=null)yield return button;foreach(object child in LogicalTreeHelper.GetChildren(node)){var dependency=child as DependencyObject;if(dependency!=null)foreach(var found in CategoryButtons(dependency))yield return found;}}
 async System.Threading.Tasks.Task CategoryTreeSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Category folders: "+message);};string oldFolder=folder;var previousPins=pinnedCategories.ToList();var previousAliases=new Dictionary<string,string>(categoryAliases,StringComparer.OrdinalIgnoreCase);
  string top="手动目录测试",one=top+"\\一级",two=one+"\\二级",leaf=two+"\\独有末级";Directory.CreateDirectory(Safety.Destination(root,leaf));File.WriteAllText(Path.Combine(root,top,"不是文件夹.txt"),"keep");
  group="";E<TextBox>("CategorySearch").Clear();RenderSidebar();RenderCategories();var categoryPanel=E<StackPanel>("CategoryPanel");
  Func<string,Button> find=tag=>CategoryButtons(categoryPanel).FirstOrDefault(b=>String.Equals(b.Tag as string,tag,StringComparison.OrdinalIgnoreCase));
  check(find("expand:"+top)!=null&&find("expand:"+one)==null&&!discoveredCategoryFolders.Contains(two),"manual root is visible and descendants are not scanned before expanding");
  int queued=queue.Count;find("expand:"+top).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find(one)!=null&&find("expand:"+two)==null&&!discoveredCategoryFolders.Contains(two),"first click reads exactly one level");
  find("expand:"+one).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find(two)!=null&&find(leaf)==null,"second click opens the next level only");
  find("expand:"+two).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find(leaf)!=null&&queue.Count==queued,"deep manual categories are available without arrows marking files");
  string added=one+"\\手动新建";Directory.CreateDirectory(Safety.Destination(root,added));find("expand:"+one).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));find("expand:"+one).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find(added)!=null&&find(leaf)==null,"collapse/reopen sees newly created folders without opening grandchildren");
  find("expand:"+two).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find(leaf)!=null&&!categories.Contains(leaf),"real directories work without being added to the fixed framework");
  string source=Path.Combine(Program.Home,"目录展开来源");Directory.CreateDirectory(source);string file=Path.Combine(source,"标注测试.txt");File.WriteAllText(file,"keep-source");await NavigateAsync(source);cards.SelectedItems.Add(items.Single(i=>i.FullPath==file));find(leaf).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  check(queue.ContainsKey(file)&&queue[file].Target==Path.Combine(root,leaf,"标注测试.txt")&&File.Exists(file)&&!File.Exists(queue[file].Target),"clicking a deep manual folder marks its exact target without moving files");queue.Remove(file);Save();
  SetCategoryAlias(leaf,"手动末级简称");TogglePin(leaf);check(E<WrapPanel>("PinnedPanel").Children.Cast<Button>().Any(b=>(string)b.Tag==leaf),"manual folders support aliases and pinning");
  E<TextBox>("CategorySearch").Text="手动末级简称";check(find(leaf)!=null,"search opens ancestors of a discovered folder alias");E<TextBox>("CategorySearch").Clear();
  var reloaded=new ModernWindow();reloaded.RenderCategories();check(reloaded.pinnedCategories.Contains(leaf)&&reloaded.ShortCategory(leaf)=="手动末级简称"&&reloaded.E<WrapPanel>("PinnedPanel").Children.Cast<Button>().Any(b=>(string)b.Tag==leaf),"manual folder pin and alias survive reopening");reloaded.Window.Close();
  Directory.CreateDirectory(Path.Combine(root,"刷新发现的新大项"));E<Button>("RefreshCategoryFoldersButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find("expand:刷新发现的新大项")!=null&&CategoryRoots().Contains("刷新发现的新大项"),"refresh reads new top-level folders");
  check(E<StackPanel>("GroupsPanel").Children.OfType<Button>().Any(b=>b.ToolTip as string=="刷新发现的新大项"),"new manual root is also available as a sidebar group");
  string virtualPath="仅框架测试\\虚拟一级\\虚拟二级";categories.Add(virtualPath);RenderCategories();find("expand:仅框架测试").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(find("仅框架测试\\虚拟一级")!=null&&!Directory.Exists(Path.Combine(root,"仅框架测试")),"uncreated framework branches can be expanded without creating directories");categories.Remove(virtualPath);
  string originalRoot=root;root=Path.Combine(Program.Home,"另一个目录测试");Directory.CreateDirectory(root);RenderCategories();check(find(top)==null&&expandedCategoryFolders.Count==0,"switching library clears discovered paths and expansion state");root=originalRoot;RenderCategories();
  check(File.ReadAllText(Path.Combine(root,top,"不是文件夹.txt"))=="keep"&&find(top+"\\不是文件夹.txt")==null,"only directories are offered as destinations");
  expandedCategoryFolders.Add(top);expandedCategoryFolders.Add(one);expandedCategoryFolders.Add(two);RenderCategories();Window.UpdateLayout();var visual=(FrameworkElement)Window.Content;var image=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"逐层展开分类目录.png")))encoder.Save(stream);
  pinnedCategories=previousPins;categoryAliases=previousAliases;group="";RenderCategories();await NavigateAsync(oldFolder);Save();await CategoryUsageSmokeTest();
 }
 async System.Threading.Tasks.Task CategoryUsageSmokeTest(){
  Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception("Category usage: "+message);};var previous=new Dictionary<string,long>(categoryChoiceCounts,StringComparer.OrdinalIgnoreCase);string oldFolder=folder;
  string top="常用统计测试",a=top+"\\直接甲",b=top+"\\直接乙",c=top+"\\直接丙",deep=a+"\\二级\\深层常用";foreach(string path in new[]{a,b,c,deep})Directory.CreateDirectory(Safety.Destination(root,path));
  string source=Path.Combine(Program.Home,"选择次数来源");Directory.CreateDirectory(source);string first=Path.Combine(source,"第一项.txt"),second=Path.Combine(source,"第二项.txt");File.WriteAllText(first,"one");File.WriteAllText(second,"two");await NavigateAsync(source);cards.SelectAll();
  long baseline=CategoryChoiceCount(deep);Mark(deep);Mark(deep);check(CategoryChoiceCount(deep)==baseline,"marking or remarking a batch does not count");Unmark();check(CategoryChoiceCount(deep)==baseline&&File.Exists(first)&&File.Exists(second),"canceling annotations does not affect move counts");cards.SelectedItems.Clear();Mark(deep);check(CategoryChoiceCount(deep)==baseline,"click without selected files does not count");
  Func<string,int,System.Threading.Tasks.Task> moveBatch=async (target,number)=>{var files=new List<string>();for(int i=0;i<number;i++){string path=Path.Combine(source,"真实移动-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(path,"move-fixture");files.Add(path);}await NavigateAsync(source);foreach(var item in items.Where(i=>files.Contains(i.FullPath)))cards.SelectedItems.Add(item);long batchBaseline=CategoryChoiceCount(target);Mark(target);check(CategoryChoiceCount(target)==batchBaseline,"marking leaves counts unchanged until execution");var plans=files.Select(f=>queue[f]).ToList();string report=await System.Threading.Tasks.Task.Run(()=>Execute(plans,null));check(plans.All(p=>File.Exists(p.Target)&&!File.Exists(p.Source))&&CategoryChoiceCount(target)==batchBaseline+1,"successful same-category batch counts once: "+report);};
  await moveBatch(deep,2);await moveBatch(deep,2);await moveBatch(deep,1);await moveBatch(b,2);await moveBatch(b,1);await moveBatch(c,1);check(CategoryChoiceCount(deep)==baseline+3&&CategoryChoiceCount(b)==2&&CategoryChoiceCount(c)==1,"ranking counts successful batches instead of clicks or file quantity");
  var favorites=FrequentCategoryDescendants(top);check(favorites.SequenceEqual(new[]{deep,b}),"top two favorites can include grandchildren at any depth");
  string error;check(CategoryChildren(top,out error).SequenceEqual(new[]{b,c,a}),"expanded children sort by their own selection count without aggregating descendants");
  expandedCategoryFolders.RemoveWhere(p=>p==top||p.StartsWith(top+"\\",StringComparison.OrdinalIgnoreCase));group=top;RenderCategories();var panel=E<StackPanel>("CategoryPanel");var shortcuts=CategoryButtons(panel).Where(button=>(button.Tag as string)==deep||(button.Tag as string)==b||(button.Tag as string)==c).Select(button=>button.Tag as string).ToList();check(shortcuts.SequenceEqual(new[]{deep,b}),"collapsed big item displays exactly the two frequent shortcuts");
  int before=queue.Count;CategoryButtons(panel).Single(button=>(button.Tag as string)=="expand:"+top).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var children=CategoryButtons(panel).Where(button=>new[]{a,b,c}.Contains(button.Tag as string)).Select(button=>button.Tag as string).ToArray();check(children.SequenceEqual(new[]{b,c,a})&&queue.Count==before&&CategoryChoiceCount(deep)==baseline+3,"expanding sorts without recording choices or duplicating shortcuts");
  Save();var restored=new ModernWindow();check(restored.CategoryChoiceCount(deep)==baseline+3&&restored.FrequentCategoryDescendants(top).SequenceEqual(new[]{deep,b}),"statistics and unexpanded deep favorites survive restarting");restored.Window.Close();
  string originalRoot=root;root=Path.Combine(Program.Home,"另一个统计资料库");Directory.CreateDirectory(Path.Combine(root,top,"直接乙"));check(CategoryChoiceCount(b)==0,"same category name in a different library has independent counts");root=originalRoot;
  string missing=top+"\\不存在的旧目录";categoryChoiceCounts[Safety.Destination(root,missing)]=100;check(!FrequentCategoryDescendants(top).Contains(missing),"missing manual folders do not occupy frequent slots");
  categoryChoiceCounts[Safety.Destination(root,a)]=Int64.MaxValue;RecordSuccessfulCategoryMove(Safety.Destination(root,a));check(CategoryChoiceCount(a)==Int64.MaxValue,"very large counts saturate rather than overflow");categoryChoiceCounts[Safety.Destination(root,a)]=0;categoryChoiceCounts.Remove(Safety.Destination(root,missing));
  group=top;expandedCategoryFolders.Remove(top);RenderCategories();Window.UpdateLayout();var visual=(FrameworkElement)Window.Content;var image=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(Program.Home,"大项默认常用两个目录.png")))encoder.Save(stream);
  string failSource=Path.Combine(source,"冲突源.txt"),failTarget=Path.Combine(root,a,"冲突目标.txt");File.WriteAllText(failSource,"source");File.WriteAllText(failTarget,"existing");long beforeFailure=CategoryChoiceCount(a);var invalid=new MovePlan{Source=failSource,Target=failTarget,Stamp=Safety.Stamp(failSource)};await System.Threading.Tasks.Task.Run(()=>Execute(new List<MovePlan>{invalid},null));check(CategoryChoiceCount(a)==beforeFailure&&File.Exists(failSource),"preflight failure does not count as a move");
  string partialOne=Path.Combine(source,"部分成功-一.txt"),partialTwo=Path.Combine(source,"部分成功-二.txt");File.WriteAllText(partialOne,"one");File.WriteAllText(partialTwo,"two");var partialPlans=new List<MovePlan>{new MovePlan{Source=partialOne,Target=Path.Combine(root,a,Path.GetFileName(partialOne)),Stamp=Safety.Stamp(partialOne)},new MovePlan{Source=partialTwo,Target=Path.Combine(root,c,Path.GetFileName(partialTwo)),Stamp=Safety.Stamp(partialTwo)}};foreach(var plan in partialPlans)queue[plan.Source]=plan;
  long beforePartial=CategoryChoiceCount(a),beforeUnmoved=CategoryChoiceCount(c);var interrupt=new CategoryMoveTestProgress(text=>{if(text.StartsWith("处理 2 /"))throw new IOException("测试中断第二项");});await System.Threading.Tasks.Task.Run(()=>Execute(partialPlans,interrupt));
  check(File.Exists(partialPlans[0].Target)&&File.Exists(partialTwo)&&CategoryChoiceCount(a)==beforePartial+1&&CategoryChoiceCount(c)==beforeUnmoved,"partial batch counts only categories that actually received files");queue.Remove(partialTwo);Save();
  var saved=System.Xml.Linq.XElement.Load(state);saved.Element("categoryUsage").SetAttributeValue("mode",null);saved.Save(state);var legacy=new ModernWindow();check(legacy.categoryChoiceCounts.Count==0,"old click-based counts are not loaded as successful-move counts");legacy.Window.Close();Save();
  categoryChoiceCounts=previous;group="";RenderCategories();await NavigateAsync(oldFolder);Save();
 }
}
sealed class CategoryMoveTestProgress : IProgress<string>{readonly Action<string> callback;public CategoryMoveTestProgress(Action<string> callback){this.callback=callback;}public void Report(string text){callback(text);}}
