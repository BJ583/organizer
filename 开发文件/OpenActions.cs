using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

static class OpenActions {
 static Dictionary<IntPtr,uint> pinned=new Dictionary<IntPtr,uint>();
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 public static ProcessStartInfo DefaultApplication(string path){if(!Safety.Exists(path))throw new FileNotFoundException("项目已不存在。",path);return new ProcessStartInfo{FileName=Path.GetFullPath(path),UseShellExecute=true,WorkingDirectory=Directory.Exists(path)?path:Path.GetDirectoryName(path)};}
 public static string[] Folders(IEnumerable<string> paths){return paths.Select(path=>{if(!Safety.Exists(path))throw new FileNotFoundException("项目已不存在。",path);return Directory.Exists(path)?Path.GetFullPath(path):Path.GetDirectoryName(Path.GetFullPath(path));}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
 static object Property(object target,string name){return target.GetType().InvokeMember(name,BindingFlags.GetProperty,null,target,null);}
 static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 public static List<IntPtr> FindFolders(string path){
  var found=new List<IntPtr>();object shell=null,windows=null;
  try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=shell.GetType().InvokeMember("Windows",BindingFlags.InvokeMethod,null,shell,null);int count=Convert.ToInt32(Property(windows,"Count"));for(int i=0;i<count;i++){object window=null;try{window=windows.GetType().InvokeMember("Item",BindingFlags.InvokeMethod,null,windows,new object[]{i});string exe=Convert.ToString(Property(window,"FullName"));if(!String.Equals(Path.GetFileName(exe),"explorer.exe",StringComparison.OrdinalIgnoreCase))continue;Uri uri;if(Uri.TryCreate(Convert.ToString(Property(window,"LocationURL")),UriKind.Absolute,out uri)&&uri.IsFile&&String.Equals(Path.GetFullPath(uri.LocalPath).TrimEnd('\\'),Path.GetFullPath(path).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))found.Add(new IntPtr(Convert.ToInt64(Property(window,"HWND"))));}catch{}finally{Release(window);}}}finally{Release(windows);Release(shell);}return found;
 }
 public static bool IsTopmost(IntPtr window){return (GetWindowLong(window,-20)&8)!=0;}
 public static void Pin(IntPtr window){uint process;GetWindowThreadProcessId(window,out process);bool wasTop=IsTopmost(window);if(!SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x43))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"文件夹已打开，但设置置顶失败。");if(!wasTop)pinned[window]=process;ShowWindow(window,9);SetForegroundWindow(window);}
 public static void ReleasePins(){foreach(var pair in pinned){uint process;GetWindowThreadProcessId(pair.Key,out process);if(IsWindow(pair.Key)&&process==pair.Value)SetWindowPos(pair.Key,new IntPtr(-2),0,0,0,0,0x13);}pinned.Clear();}
 public static async Task OpenFolder(string path){
  var existing=FindFolders(path);if(existing.Count>0){Pin(existing.Last());return;}
  using(var process=Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Arguments="/n,\""+path.TrimEnd('\\')+"\\.\"",UseShellExecute=true})){}
  for(int i=0;i<40;i++){await Task.Delay(200);var windows=FindFolders(path);if(windows.Count>0){Pin(windows.Last());return;}}
  throw new Exception("已请求打开文件夹，但未找到对应窗口，无法确认置顶："+path);
 }
}
