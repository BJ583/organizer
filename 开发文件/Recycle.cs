using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

class RecyclePlan {
 public string Path {get;set;}
 public bool IsDirectory {get;set;}
 public long Bytes {get;set;}
 public int Files {get;set;}
 public Dictionary<string,string> Snapshot=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 public string Kind {get{return IsDirectory?"文件夹（包含全部内容）":"文件";}}
 public string Summary {get{return Files+" 个文件 · "+(Bytes/1048576d).ToString("0.##")+" MB";}}
}
static class RecycleSafety {
 public static void Protect(string path,string library,string state) {
  path=System.IO.Path.GetFullPath(path).TrimEnd('\\');Safety.NoLinks(path);
  if(!Safety.Exists(path))throw new Exception("项目已不存在："+path);
  if(String.Equals(path,System.IO.Path.GetPathRoot(path).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new Exception("不能删除整个磁盘。");
  foreach(string protectedPath in new[]{library,Program.Home}) {string full=System.IO.Path.GetFullPath(protectedPath).TrimEnd('\\');if(String.Equals(path,full,StringComparison.OrdinalIgnoreCase)||Safety.Under(full,path))throw new Exception("不能删除资料库、软件目录本身或其父目录。");}
  if(String.Equals(path,state,StringComparison.OrdinalIgnoreCase)||String.Equals(path,System.Reflection.Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new Exception("不能删除正在使用的程序或配置文件。");
 }
 public static RecyclePlan Capture(string path,string library,string state){Protect(path,library,state);var plan=new RecyclePlan{Path=System.IO.Path.GetFullPath(path),IsDirectory=Directory.Exists(path)};Walk(plan.Path,plan);return plan;}
 static void Walk(string path,RecyclePlan plan){Safety.NoLinks(path);plan.Snapshot.Add(path,Safety.Stamp(path));if(Directory.Exists(path)){foreach(string child in Directory.GetFileSystemEntries(path).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))Walk(child,plan);}else{plan.Files++;plan.Bytes+=new FileInfo(path).Length;}}
 public static void Check(RecyclePlan plan,string library,string state){var current=Capture(plan.Path,library,state);if(current.Snapshot.Count!=plan.Snapshot.Count||current.Snapshot.Any(p=>!plan.Snapshot.ContainsKey(p.Key)||plan.Snapshot[p.Key]!=p.Value))throw new Exception("项目或文件夹内容在预览后发生变化，请重新打开删除预览："+plan.Path);}
 public static Task<T> Sta<T>(Func<T> work){var result=new TaskCompletionSource<T>();var thread=new Thread(()=>{try{result.SetResult(work());}catch(Exception e){result.SetException(e);}});thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();return result.Task;}
}

[ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface RecycleShellItem {
 [PreserveSig]int BindToHandler(IntPtr bind,ref Guid handler,ref Guid iid,out IntPtr result);
 [PreserveSig]int GetParent(out RecycleShellItem parent);
 [PreserveSig]int GetDisplayName(uint kind,out IntPtr name);
 [PreserveSig]int GetAttributes(uint mask,out uint attributes);
 [PreserveSig]int Compare(RecycleShellItem other,uint hint,out int order);
}
[ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface RecycleOperation {
 [PreserveSig]int Advise(RecycleProgress progress,out uint cookie);
 [PreserveSig]int Unadvise(uint cookie);
 [PreserveSig]int SetOperationFlags(uint flags);
 [PreserveSig]int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string text);
 [PreserveSig]int SetProgressDialog(IntPtr dialog);
 [PreserveSig]int SetProperties(IntPtr properties);
 [PreserveSig]int SetOwnerWindow(IntPtr owner);
 [PreserveSig]int ApplyPropertiesToItem(IntPtr item);
 [PreserveSig]int ApplyPropertiesToItems(IntPtr items);
 [PreserveSig]int RenameItem(IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);
 [PreserveSig]int RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);
 [PreserveSig]int MoveItem(IntPtr item,IntPtr destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);
 [PreserveSig]int MoveItems(IntPtr items,IntPtr destination);
 [PreserveSig]int CopyItem(IntPtr item,IntPtr destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);
 [PreserveSig]int CopyItems(IntPtr items,IntPtr destination);
 [PreserveSig]int DeleteItem(RecycleShellItem item,RecycleProgress sink);
 [PreserveSig]int DeleteItems(IntPtr items);
 [PreserveSig]int NewItem(IntPtr destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,IntPtr sink);
 [PreserveSig]int PerformOperations();
 [PreserveSig]int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
}
[ComVisible(true),Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface RecycleProgress {
 [PreserveSig]int StartOperations();[PreserveSig]int FinishOperations(int result);
 [PreserveSig]int PreRenameItem(uint flags,IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string name);
 [PreserveSig]int PostRenameItem(uint flags,IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IntPtr created);
 [PreserveSig]int PreMoveItem(uint flags,IntPtr item,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name);
 [PreserveSig]int PostMoveItem(uint flags,IntPtr item,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IntPtr created);
 [PreserveSig]int PreCopyItem(uint flags,IntPtr item,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name);
 [PreserveSig]int PostCopyItem(uint flags,IntPtr item,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IntPtr created);
 [PreserveSig]int PreDeleteItem(uint flags,IntPtr item);
 [PreserveSig]int PostDeleteItem(uint flags,IntPtr item,int result,IntPtr created);
 [PreserveSig]int PreNewItem(uint flags,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name);
 [PreserveSig]int PostNewItem(uint flags,IntPtr folder,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,uint attributes,int result,IntPtr created);
 [PreserveSig]int UpdateProgress(uint total,uint completed);[PreserveSig]int ResetTimer();[PreserveSig]int PauseTimer();[PreserveSig]int ResumeTimer();
}
[ComVisible(true),ClassInterface(ClassInterfaceType.None)]
public class RecycleGuard : RecycleProgress {
 internal Action Validate;
 public string Error,RecycledPath;
 public bool Completed;
 public int StartOperations(){return 0;}public int FinishOperations(int result){return 0;}
 public int PreRenameItem(uint f,IntPtr i,string n){return 0;}public int PostRenameItem(uint f,IntPtr i,string n,int r,IntPtr c){return 0;}
 public int PreMoveItem(uint f,IntPtr i,IntPtr d,string n){return 0;}public int PostMoveItem(uint f,IntPtr i,IntPtr d,string n,int r,IntPtr c){return 0;}
 public int PreCopyItem(uint f,IntPtr i,IntPtr d,string n){return 0;}public int PostCopyItem(uint f,IntPtr i,IntPtr d,string n,int r,IntPtr c){return 0;}
 public int PreDeleteItem(uint flags,IntPtr item){try{if((flags&0x80)==0)throw new Exception("Windows 无法将此项目放入回收站，已阻止永久删除。");if(Validate!=null)Validate();return 0;}catch(Exception e){Error=e.Message;return unchecked((int)0x80004004);}}
 public int PostDeleteItem(uint flags,IntPtr item,int result,IntPtr created){if(result<0)Error=Marshal.GetExceptionForHR(result).Message;else if(created!=IntPtr.Zero){try{RecycledPath=RecycleNative.Name((RecycleShellItem)Marshal.GetObjectForIUnknown(created));Completed=true;}catch(Exception e){Error="回收结果需要人工核对："+e.Message;}}return 0;}
 public int PreNewItem(uint f,IntPtr d,string n){return 0;}public int PostNewItem(uint f,IntPtr d,string n,string t,uint a,int r,IntPtr c){return 0;}
 public int UpdateProgress(uint t,uint c){return 0;}public int ResetTimer(){return 0;}public int PauseTimer(){return 0;}public int ResumeTimer(){return 0;}
}
static class RecycleNative {
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHCreateItemFromParsingName(string name,IntPtr bind,ref Guid iid,out RecycleShellItem item);
 internal static string Name(RecycleShellItem item){IntPtr value;Marshal.ThrowExceptionForHR(item.GetDisplayName(0x80058000,out value));try{return Marshal.PtrToStringUni(value);}finally{Marshal.FreeCoTaskMem(value);}}
 public static string Send(RecyclePlan plan,string library,string state){
  RecycleSafety.Check(plan,library,state);RecycleOperation operation=null;RecycleShellItem item=null;
  try{operation=(RecycleOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));var iid=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(plan.Path,IntPtr.Zero,ref iid,out item));
   // Recycle only, stop on errors, suppress native prompts and connected-file expansion.
   Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x00080000|0x00100000|0x20000000|0x0400|0x0010|0x0004|0x2000));
   var guard=new RecycleGuard{Validate=()=>RecycleSafety.Check(plan,library,state)};Marshal.ThrowExceptionForHR(operation.DeleteItem(item,guard));int result=operation.PerformOperations();bool aborted;Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out aborted));
   if(guard.Error!=null)throw new Exception(guard.Error);Marshal.ThrowExceptionForHR(result);if(aborted||!guard.Completed||Safety.Exists(plan.Path))throw new Exception("操作未完成，未自动继续。请核对源位置和系统回收站。");return guard.RecycledPath;
  }finally{if(item!=null)Marshal.ReleaseComObject(item);if(operation!=null)Marshal.ReleaseComObject(operation);}
 }
}
