using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ClearGuard {
 [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 internal interface IRecycleOperation {
  [PreserveSig]int Advise(IntPtr sink,out uint cookie);[PreserveSig]int Unadvise(uint cookie);[PreserveSig]int SetOperationFlags(uint flags);[PreserveSig]int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);[PreserveSig]int SetProgressDialog(IntPtr dialog);[PreserveSig]int SetProperties(IntPtr changes);[PreserveSig]int SetOwnerWindow(IntPtr owner);[PreserveSig]int ApplyPropertiesToItem(IntPtr item);[PreserveSig]int ApplyPropertiesToItems(IntPtr items);[PreserveSig]int RenameItem(IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);[PreserveSig]int RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);[PreserveSig]int MoveItem(IntPtr item,IntPtr destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);[PreserveSig]int MoveItems(IntPtr items,IntPtr destination);[PreserveSig]int CopyItem(IntPtr item,IntPtr destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);[PreserveSig]int CopyItems(IntPtr items,IntPtr destination);[PreserveSig]int DeleteItem(IntPtr item,IntPtr sink);[PreserveSig]int DeleteItems(IntPtr items);[PreserveSig]int NewItem(IntPtr destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,IntPtr sink);[PreserveSig]int PerformOperations();[PreserveSig]int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
 }
 public static class RecycleService {
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]private static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid id,out IntPtr item);
  private static string BinFor(string path){string sid=WindowsIdentity.GetCurrent().User.Value;return Path.Combine(Path.GetPathRoot(Path.GetFullPath(path)),"$Recycle.Bin",sid);}
  private static IEnumerable<string> Metadata(string bin){return Directory.Exists(bin)?Directory.GetFiles(bin,"$I*",SearchOption.TopDirectoryOnly):new string[0];}
  private static bool ReadReceipt(string metadata,out string original,out long bytes){
   original=null;bytes=0;try{using(var reader=new BinaryReader(File.Open(metadata,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))){long version=reader.ReadInt64();bytes=reader.ReadInt64();reader.ReadInt64();if(version==2){int length=reader.ReadInt32();if(length<1||length>32768)return false;byte[] raw=reader.ReadBytes(length*2);if(raw.Length!=length*2)return false;original=Encoding.Unicode.GetString(raw).TrimEnd('\0');}else if(version==1){byte[] raw=reader.ReadBytes(520);original=Encoding.Unicode.GetString(raw).TrimEnd('\0');}else return false;}return !String.IsNullOrWhiteSpace(original);}catch{return false;}
  }
  private static long SizeOf(string path){if(File.Exists(path))return new FileInfo(path).Length;long bytes=0;var stack=new Stack<string>();stack.Push(path);while(stack.Count>0){string d=stack.Pop();if(SafetyPolicy.HasReparseAncestor(d))throw new IOException("Reparse point");foreach(string sub in Directory.GetDirectories(d))stack.Push(sub);foreach(string f in Directory.GetFiles(d)){if(SafetyPolicy.HasReparseAncestor(f))throw new IOException("Reparse file");bytes+=new FileInfo(f).Length;}}return bytes;}
  public static OperationEntry Recycle(string path){
   path=Path.GetFullPath(path);if(!String.Equals(Path.GetPathRoot(path),"C:\\",StringComparison.OrdinalIgnoreCase)||new DriveInfo("C").DriveType!=DriveType.Fixed)throw new IOException("بازیافت این برنامه فقط روی درایو ثابت C مجاز است");if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("فایل یافت نشد",path);if(SafetyPolicy.HasReparseAncestor(path))throw new IOException("Junction یا symlink مجاز نیست");
   var entry=new OperationEntry{OriginalPath=path,Hash=SafetyPolicy.HashTree(path),Bytes=SizeOf(path),Status="Pending"};string bin=BinFor(path);var before=new HashSet<string>(Metadata(bin),StringComparer.OrdinalIgnoreCase);
   object raw=null;IntPtr item=IntPtr.Zero;
   try{raw=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));var operation=(IRecycleOperation)raw;
    // FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD | FOFX_EARLYFAILURE |
    // FOF_NOERRORUI | FOF_SILENT | FOF_NOCONFIRMATION |
    // FOF_WANTNUKEWARNING | FOF_NO_CONNECTED_ELEMENTS. No delete fallback.
    Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x20086414u|0x00100000u));var id=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path,IntPtr.Zero,ref id,out item));Marshal.ThrowExceptionForHR(operation.DeleteItem(item,IntPtr.Zero));Marshal.ThrowExceptionForHR(operation.PerformOperations());bool aborted;Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out aborted));if(aborted)throw new IOException("Windows انتقال به سطل بازیافت را متوقف کرد");
   }finally{if(item!=IntPtr.Zero)Marshal.Release(item);if(raw!=null)Marshal.FinalReleaseComObject(raw);}
   if(File.Exists(path)||Directory.Exists(path))throw new IOException("فایل پس از عملیات باقی مانده است");
   foreach(string metadata in Metadata(bin).Where(m=>!before.Contains(m))){string original;long size;if(!ReadReceipt(metadata,out original,out size)||!String.Equals(Path.GetFullPath(original),path,StringComparison.OrdinalIgnoreCase)||size!=entry.Bytes)continue;string payload=Path.Combine(bin,"$R"+Path.GetFileName(metadata).Substring(2));if(!File.Exists(payload)&&!Directory.Exists(payload))continue;if(SafetyPolicy.HashTree(payload)!=entry.Hash)continue;entry.DestinationPath=payload;entry.RecycleMetadata=metadata;entry.Status="Recycled";entry.Detail="انتقال قابل بازیابی؛ رسید و SHA-256 تأیید شد";return entry;}
   entry.Status="ReceiptMissing";entry.Detail="مسیر اصلی منتقل شد اما رسید تأیید نشد؛ هیچ حذف دائمی انجام نشد. در Recycle Bin بررسی کنید.";return entry;
  }
  public static OperationEntry Restore(OperationEntry source){
   if(source==null||source.Status!="Recycled"||String.IsNullOrWhiteSpace(source.Hash)||String.IsNullOrWhiteSpace(source.OriginalPath)||String.IsNullOrWhiteSpace(source.DestinationPath)||String.IsNullOrWhiteSpace(source.RecycleMetadata))throw new IOException("رسید قابل بازیابی معتبر نیست");
   string original=Path.GetFullPath(source.OriginalPath),bin=BinFor(original),payload=Path.GetFullPath(source.DestinationPath),metadata=Path.GetFullPath(source.RecycleMetadata);
   if(!SafetyPolicy.IsWithin(payload,bin)||!SafetyPolicy.IsWithin(metadata,bin)||Path.GetDirectoryName(payload)!=bin||Path.GetDirectoryName(metadata)!=bin||!Path.GetFileName(metadata).StartsWith("$I",StringComparison.Ordinal)||!Path.GetFileName(payload).Equals("$R"+Path.GetFileName(metadata).Substring(2),StringComparison.Ordinal))throw new IOException("رسید خارج از سطل بازیافت کاربر است");
   if(SafetyPolicy.HasReparseAncestor(payload)||SafetyPolicy.HasReparseAncestor(metadata)||SafetyPolicy.HasReparseAncestor(Path.GetDirectoryName(original)))throw new IOException("بازیابی در مسیر پیوندی مجاز نیست");
   if(File.Exists(original)||Directory.Exists(original))throw new IOException("مسیر اصلی وجود دارد؛ بازنویسی انجام نشد");
   string receiptPath;long size;if(!ReadReceipt(metadata,out receiptPath,out size)||!String.Equals(Path.GetFullPath(receiptPath),original,StringComparison.OrdinalIgnoreCase)||size!=source.Bytes||SizeOf(payload)!=source.Bytes||SafetyPolicy.HashTree(payload)!=source.Hash)throw new IOException("محتوا یا رسید تغییر کرده است؛ بازیابی متوقف شد");
   if(!Directory.Exists(Path.GetDirectoryName(original)))throw new IOException("پوشهٔ والد حذف شده؛ بازیابی خودکار انجام نمی‌شود");
   bool directory=Directory.Exists(payload);if(directory)Directory.Move(payload,original);else File.Move(payload,original);if(SafetyPolicy.HashTree(original)!=source.Hash)throw new IOException("بررسی محتوای بازیابی‌شده ناموفق");File.Delete(metadata);
   return new OperationEntry{OriginalPath=original,DestinationPath=original,RecycleMetadata=metadata,Hash=source.Hash,Bytes=source.Bytes,Status="Restored",Detail="فقط مورد همین گزارش، بدون بازنویسی، با SHA-256 بازیابی شد"};
  }
 }
}
