using System;using System.IO;using System.Runtime.InteropServices;using System.Runtime.InteropServices.ComTypes;using System.Reflection;using System.Security;using Microsoft.Win32;using Windows.Data.Xml.Dom;using Windows.UI.Notifications;
// Windows invokes the registered COM activator directly, including from Action Center.
// The old protocol parser is retained for compatibility, but new toasts never use it.
internal static class ChatNotifications {
 const string AppId="NyangGaming.Messenger",Scheme="nyang-gaming-chat";
 internal const string ActivatorId="7B057A13-5A49-43CB-A205-7E832592346B";
 static bool registered;
 static int activatorCookie;static bool activatorStarted;
 internal static void StartActivator(){Register();if(activatorStarted)return;System.Windows.Forms.Application.OleRequired();activatorCookie=new RegistrationServices().RegisterTypeForComClients(typeof(NyangToastActivator),RegistrationClassContext.LocalServer,RegistrationConnectionType.MultipleUse);activatorStarted=true;
  // Retire only this app's obsolete protocol toasts; their links cannot be repaired.
  try{foreach(var toast in ToastNotificationManager.History.GetHistory(AppId)){if(toast.Content.GetXml().Contains("nyang-gaming-chat:")&&!string.IsNullOrEmpty(toast.Tag))ToastNotificationManager.History.Remove(toast.Tag,toast.Group,AppId);}}catch{}
 }
 internal static void StopActivator(){if(activatorStarted){new RegistrationServices().UnregisterTypeForComClients(activatorCookie);activatorStarted=false;}}
 internal static void Activated(string app,string argument){Guid id;if(app!=AppId||!Guid.TryParseExact(argument,"D",out id))return;Queue(id.ToString());try{using(var signal=System.Threading.EventWaitHandle.OpenExisting("Local\\NyangGaming.Open"))signal.Set();}catch{} }
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey {public Guid id;public uint pid;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct Variant {[FieldOffset(0)]public ushort type;[FieldOffset(8)]public IntPtr text;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore {uint GetCount();void GetAt(uint i,out PropertyKey key);void GetValue(ref PropertyKey key,out Variant value);void SetValue(ref PropertyKey key,ref Variant value);void Commit();}
 [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink {}
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref Variant value);
 [DllImport("shell32.dll")]static extern void SHChangeNotify(uint events,uint flags,IntPtr first,IntPtr second);
 internal static string Parse(string value){Uri uri;Guid room;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!=Scheme||uri.Host!="room"||uri.Query!=""||uri.Fragment!=""||!Guid.TryParse(uri.AbsolutePath.Trim('/'),out room))return null;return room.ToString();}
 internal static string Link(string room){Guid id;if(!Guid.TryParse(room,out id))throw new ArgumentException();return Scheme+"://room/"+id;}
 internal static string RequestPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KoruGaming_Next","UserData","chat-open-request.txt");}}
 internal static void Queue(string room){Guid id;if(!Guid.TryParse(room,out id))return;LeagueJson.AtomicBytes(RequestPath,System.Text.Encoding.UTF8.GetBytes(id.ToString()));}
 internal static string Take(){try{string value=File.ReadAllText(RequestPath);File.Delete(RequestPath);Guid id;return Guid.TryParse(value,out id)?id.ToString():null;}catch{return null;}}
 internal static void Register(){if(registered)return;string exe=System.Windows.Forms.Application.ExecutablePath;
  using(var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64)){
   using(var server=hive.CreateSubKey("Software\\Classes\\CLSID\\{"+ActivatorId+"}\\LocalServer32"))server.SetValue("","\""+exe+"\" --toast-activated",RegistryValueKind.String);
   using(var k=hive.CreateSubKey("Software\\Classes\\"+Scheme)){k.SetValue("","URL:Nyang Gaming Chat",RegistryValueKind.String);k.SetValue("URL Protocol","",RegistryValueKind.String);using(var icon=k.CreateSubKey("DefaultIcon"))icon.SetValue("",exe+",0");using(var command=k.CreateSubKey("shell\\open\\command"))command.SetValue("","\""+exe+"\" --chat-room \"%1\"",RegistryValueKind.String);}
   using(var cap=hive.CreateSubKey("Software\\NyangGaming\\Capabilities")){cap.SetValue("ApplicationName","Nyang Gaming");cap.SetValue("ApplicationDescription","Nyang Gaming chat notifications");using(var urls=cap.CreateSubKey("URLAssociations"))urls.SetValue(Scheme,Scheme);}
   using(var apps=hive.CreateSubKey("Software\\RegisteredApplications"))apps.SetValue("Nyang Gaming","Software\\NyangGaming\\Capabilities");
  }
  // Invalidate the shell/broker association cache after first launch or moving the app.
  SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
  string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Nyang Gaming Chat.lnk");Directory.CreateDirectory(Path.GetDirectoryName(shortcut));
  object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object link=null;try{link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});var t=link.GetType();t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{exe});t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(exe)});t.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{exe+",0"});t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
  object native=new ShellLink();try{((IPersistFile)native).Load(shortcut,2);var store=(IPropertyStore)native;var key=new PropertyKey{id=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),pid=5};var value=new Variant{type=31,text=Marshal.StringToCoTaskMemUni(AppId)};try{store.SetValue(ref key,ref value);var activatorKey=new PropertyKey{id=key.id,pid=26};var activator=new Variant{type=72,text=Marshal.AllocCoTaskMem(16)};try{Marshal.StructureToPtr(new Guid("7B057A13-5A49-43CB-A205-7E832592346B"),activator.text,false);store.SetValue(ref activatorKey,ref activator);}finally{PropVariantClear(ref activator);}store.Commit();((IPersistFile)native).Save(shortcut,true);}finally{PropVariantClear(ref value);}}finally{Marshal.FinalReleaseComObject(native);}registered=true;
 }
 internal static void Show(string room,string title,string body){Register();Guid id;if(!Guid.TryParse(room,out id))throw new ArgumentException();var xml=new XmlDocument();xml.LoadXml("<toast activationType='foreground' launch='"+id.ToString()+"'><visual><binding template='ToastGeneric'><text>"+SecurityElement.Escape(title)+"</text><text hint-maxLines='3'>"+SecurityElement.Escape(body)+"</text></binding></visual></toast>");var toast=new ToastNotification(xml){Tag=id.ToString("N").Substring(0,16),Group="chat",ExpirationTime=DateTimeOffset.Now.AddDays(1)};ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);}
}

[ComVisible(true),Guid("53E31837-6600-4A81-9395-75CFFE746F94"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INyangNotificationActivationCallback {
 void Activate([MarshalAs(UnmanagedType.LPWStr)]string appUserModelId,[MarshalAs(UnmanagedType.LPWStr)]string invokedArgs,IntPtr data,uint count);
}
[ComVisible(true),Guid(ChatNotifications.ActivatorId),ClassInterface(ClassInterfaceType.None),ComDefaultInterface(typeof(INyangNotificationActivationCallback))]
public sealed class NyangToastActivator:INyangNotificationActivationCallback {
 public void Activate(string appUserModelId,string invokedArgs,IntPtr data,uint count){try{ChatNotifications.Activated(appUserModelId,invokedArgs);}catch{}}
}

