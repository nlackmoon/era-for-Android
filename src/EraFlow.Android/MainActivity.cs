using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using EraFlow.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Uri=Android.Net.Uri;

namespace EraFlow;

[Activity(Label="era for Android",MainLauncher=true,Exported=true,Theme="@style/EraTheme",ScreenOrientation=ScreenOrientation.SensorPortrait,ConfigurationChanges=ConfigChanges.ScreenSize|ConfigChanges.Orientation|ConfigChanges.KeyboardHidden)]
public sealed class MainActivity:Activity
{
 WebView web=null!; GameLibrary library=null!; bool busy,openingGame; string? pendingGame;
 Dictionary<string,string> scanIndex=new();
 string? pendingDirectPath;
 Android.Content.ISharedPreferences Prefs=>GetSharedPreferences("eraflow",FileCreationMode.Private)!;
 protected override void OnCreate(Bundle? state)
 {
  base.OnCreate(state);library=new GameLibrary(Path.Combine(FilesDir!.AbsolutePath,"library"));library.Load();LoadScanIndex();
  web=new WebView(this);Platform.Configure(this,web,false);web.AddJavascriptInterface(new JsBridge(Receive),"Android");
  // 起動時は保存済みの一覧を出すだけ。ディレクトリ全体の再走査は
  // ライブラリ画面の更新ボタンを押したときだけ行う (毎回の自動取り込みをやめる)。
  web.SetWebViewClient(new LocalClient(()=>Render(),u=>u.StartsWith("file:///android_asset/")));
  Platform.SetContent(this,web);web.LoadUrl("file:///android_asset/library.html");
 }
 protected override void OnResume()
 {
  base.OnResume();openingGame=false;
  if(library!=null){library.Load();Render();}
  // 从「所有文件访问」设置页回来：授权成功就直接登记，不再复制。
  if(pendingDirectPath!=null && HasAllFilesAccess()){var p=pendingDirectPath;pendingDirectPath=null;_=ScanDirectAsync(p);}
 }
 void Render()=>Platform.Send(this,web,"library",new {games=library.Games.OrderByDescending(g=>g.LastPlayed).Select(g=>new {id=g.Id,name=g.Name,scripts=g.Scripts,images=g.Images,bytes=g.Bytes,lastPlayed=g.LastPlayed,external=g.External}),folder=Prefs.GetString("tree",null)!=null});
 void Message(string text)=>Platform.Send(this,web,"message",text);
 void Progress(string? text)=>Platform.Send(this,web,"progress",text);
 void Receive(string json)=>RunOnUiThread(()=>{
  try {using var doc=JsonDocument.Parse(json);var d=doc.RootElement;string action=d.GetProperty("action").GetString()??"";string id=d.TryGetProperty("id",out var i)?i.GetString()??"":"";
   if(busy && action!="ready")return;
   switch(action)
   {
    case "ready":Render();break;
    case "exit":Finish();break;
    case "folder":StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree).AddFlags(ActivityFlags.GrantReadUriPermission|ActivityFlags.GrantPersistableUriPermission),1);break;
    case "zip":PickFile(2);break;
    case "scan":_ = ScanSaved();break;
    case "play":_ = OpenGame(id);break;
    case "export":pendingGame=id;StartActivityForResult(new Intent(Intent.ActionCreateDocument).AddCategory(Intent.CategoryOpenable).SetType("application/zip").PutExtra(Intent.ExtraTitle,"era-for-Android-存档-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".zip"),3);break;
    case "restore":pendingGame=id;new AlertDialog.Builder(this).SetTitle("恢复存档").SetMessage("将替换此游戏的同名存档。原存档会先在本机备份。").SetNegativeButton("取消",(_,_)=>{}).SetPositiveButton("选择备份",(_,_)=>PickFile(4)).Show();break;
    case "delete":ConfirmDelete(id);break;
    case "haptic":web.PerformHapticFeedback(Android.Views.FeedbackConstants.KeyboardTap);break;
    case "about":ShowAbout();break;
   }
  }catch(Exception e){Message(e.Message);}
 });
 async Task OpenGame(string id)
 {
  if(openingGame)return;var game=library.Games.Find(g=>g.Id==id);if(game==null)return;if(!Directory.Exists(game.Path)){Message("游戏目录不存在（SD 卡是否已挂载？）");return;}openingGame=true;
  try
  {
   var manager=(ActivityManager)GetSystemService(ActivityService)!;
   bool Running()=>manager.RunningAppProcesses?.Any(p=>p.ProcessName==PackageName+":game")==true;
   for(int n=0;n<50&&Running();n++)await Task.Delay(100);
   if(Running()){openingGame=false;Message("上一个游戏正在关闭，请稍候再试");return;}
   library.Played(id);StartActivity(new Intent(this,typeof(GameActivity)).PutExtra("path",game.Path).PutExtra("title",game.Name).PutExtra("id",game.Id).PutExtra("external",game.External));
  }
  catch(Exception e){openingGame=false;Message(e.Message);}
 }
 void ConfirmDelete(string id)
 {
  var game=library.Games.Find(g=>g.Id==id);if(game==null)return;
  bool external=game.External;
  string message=external
   ? $"「{game.Name}」是直接读取的游戏。只会从游戏库移除，不会删除原目录和存档。"
   : $"「{game.Name}」的游戏文件和存档将从本机删除，无法恢复。\n\n如需保留进度，请先导出存档。";
  var dialog=new AlertDialog.Builder(this).SetTitle(external?"从库中移除？":"删除游戏？").SetMessage(message).SetNegativeButton("取消",(_,_)=>{}).SetPositiveButton(external?"移除":"删除",(s,e)=>{_=DeleteGame(game);}).Create();
  dialog.Show();dialog.GetButton((int)DialogButtonType.Positive)?.SetTextColor(Android.Graphics.Color.ParseColor("#b3261e"));
 }
 async Task DeleteGame(GameEntry game)
 {
  if(openingGame){Message("正在打开游戏，请稍候再试");return;}
  var manager=(ActivityManager)GetSystemService(ActivityService)!;
  bool Running()=>manager.RunningAppProcesses?.Any(p=>p.ProcessName==PackageName+":game")==true;
  for(int n=0;n<50&&Running();n++)await Task.Delay(100);
  if(Running()){Message("游戏仍在运行，请先返回游戏库再试");return;}
  await Work(async()=>{bool external=game.External;await Task.Run(()=>library.Delete(game.Id));GetSharedPreferences("player",FileCreationMode.Private)!.Edit()!.Remove(game.Id)!.Apply();Message(external?$"已从库中移除「{game.Name}」":$"已删除「{game.Name}」");},"正在删除…");
 }
 void PickFile(int request)=>StartActivityForResult(new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable).SetType("*/*").PutExtra(Intent.ExtraAllowMultiple,request==2).AddFlags(ActivityFlags.GrantReadUriPermission),request);
 protected override async void OnActivityResult(int request,Result result,Intent? data)
 {
  base.OnActivityResult(request,result,data);if(result!=Result.Ok||data==null)return;
  var uris=new List<Uri>();if(data.ClipData is { } clips){for(int k=0;k<clips.ItemCount;k++)if(clips.GetItemAt(k)?.Uri is {} item)uris.Add(item);}else if(data.Data is {} item)uris.Add(item);
  if(uris.Count==0)return;var uri=uris[0];
  if(request==1)
  {
   try
   {
    ContentResolver!.TakePersistableUriPermission(uri,ActivityFlags.GrantReadUriPermission);
    Prefs.Edit()!.PutString("tree",uri.ToString())!.Apply();
    string? direct=TreeToPath(uri);
    if(direct!=null && Directory.Exists(direct))
    {
     if(HasAllFilesAccess())
     {
      if(CanUseDirect(direct))await ScanDirectAsync(direct);
      else{Message("目录不可写，改为复制导入");await ScanSaved();}
     }
     else
     {
      // 复制几 GB 的目录可能要几十分钟。能直读时先问一句，别让用户白等。
      pendingDirectPath=direct;
      new AlertDialog.Builder(this)
       .SetTitle("直接读取，不复制")
       .SetMessage("这个目录可以直接读取。授权「所有文件访问」后，几 GB 的游戏也能几秒进库，不占额外空间，存档也写回原目录。\n\n不授权也能用，但要把整个目录复制进应用，大目录会非常慢。")
       .SetNegativeButton("复制导入",(_,_)=>{pendingDirectPath=null;_=ScanSaved();})
       .SetPositiveButton("去授权",(_,_)=>RequestAllFilesAccess())
       .Show();
     }
    }
    else await ScanSaved();
   }
   catch(Exception e){Message("目录授权失败："+e.Message);}
   return;
  }
  await Work(async()=>{
   if(request==2){int count=0;var failures=new List<string>();foreach(var selected in uris){string temp=Path.Combine(CacheDir!.AbsolutePath,"import-"+Guid.NewGuid()+".zip");string name=DisplayName(selected);try{Progress("正在读取 · "+name);using(var from=ContentResolver!.OpenInputStream(selected)!)using(var to=File.Create(temp)){await from.CopyToAsync(to);}var added=await Task.Run(()=>library.ImportZip(temp,name,Progress));count+=added.Count;}catch(Exception e){failures.Add(name+"："+e.Message);}finally{if(File.Exists(temp))File.Delete(temp);}}Message($"已读取 {count} 个游戏"+(failures.Count>0?$"，{failures.Count} 个文件未导入":""));if(failures.Count>0)new AlertDialog.Builder(this).SetTitle("部分文件未导入").SetMessage(string.Join("\n\n",failures)).SetPositiveButton("知道了",(_,_)=>{}).Show();}
   else {var game=library.Games.Find(g=>g.Id==pendingGame);if(game==null)return;if(request==3){using var output=ContentResolver!.OpenOutputStream(uri)!;await Task.Run(()=>library.ExportSaves(game,output));Message("存档已导出");}else if(request==4){using var source=ContentResolver!.OpenInputStream(uri)!;string temp=Path.Combine(CacheDir!.AbsolutePath,"restore.zip");try{using(var to=File.Create(temp))await source.CopyToAsync(to);using var input=File.OpenRead(temp);await Task.Run(()=>library.ImportSaves(game,input));Message("存档已恢复");}finally{if(File.Exists(temp))File.Delete(temp);}}}
  });
 }
 string DisplayName(Uri uri){using var c=ContentResolver!.Query(uri,new[]{OpenableColumns.DisplayName},null,null,null);return c?.MoveToFirst()==true?c.GetString(0)??"游戏":"游戏";}
 async Task Work(Func<Task> action,string progress="正在读取文件…")
 {
  if(busy)return;busy=true;Progress(progress);try{await action();library.Save();Render();}catch(Exception e){Android.Util.Log.Error("EraFlow",e.ToString());Message("未完成："+e.Message);}finally{busy=false;Progress(null);}
 }
 async Task ScanSaved()
 {
  string? saved=Prefs.GetString("tree",null);if(saved==null){Message("请先选择游戏目录");return;}
  var tree=Uri.Parse(saved)!;
  string? direct=TreeToPath(tree);
  // 老用户升级到 mod2：目录能直读但还没授权时，问一次要不要改成零拷贝。
  if(direct!=null && !HasAllFilesAccess() && Prefs.GetBoolean("direct_ask",true))
  {
   pendingDirectPath=direct;
   new AlertDialog.Builder(this)
    .SetTitle("可以改成直接读取")
    .SetMessage("检测到这个目录可以直接读取。开启「所有文件访问」后不再复制文件，几 GB 的合集也能几秒进库，存档写回原目录。\n\n要现在开启吗？")
    .SetPositiveButton("去授权",(_,_)=>RequestAllFilesAccess())
    .SetNeutralButton("以后再说",(_,_)=>pendingDirectPath=null)
    .SetNegativeButton("继续复制扫描",(_,_)=>{pendingDirectPath=null;Prefs.Edit()!.PutBoolean("direct_ask",false)!.Apply();_=ScanSaved();})
    .Show();
   return;
  }
  await Work(async()=>{
   int before=library.Games.Count;
   // 能拿到真实路径且已授权时，直接读原目录，完全不复制。
   if(direct!=null && HasAllFilesAccess() && CanUseDirect(direct))await Task.Run(()=>ScanDirect(direct,0));
   else await Task.Run(()=>ScanTree(tree,DocumentsContract.GetTreeDocumentId(tree),"游戏目录",0));
   SaveScanIndex();
   int n=library.Games.Count-before;
   Message(n>0?$"发现 {n} 个新游戏":"游戏库已是最新");
  },"正在检查目录…");
 }
 async Task ScanDirectAsync(string path)
 {
  if(!CanUseDirect(path)){Message("目录不可写，改为复制导入");await ScanSaved();return;}
  await Work(async()=>{
   int before=library.Games.Count;
   await Task.Run(()=>ScanDirect(path,0));
   int n=library.Games.Count-before;
   Message(n>0?$"已直接登记 {n} 个游戏（没有复制文件）":"游戏库已是最新");
  },"正在读取目录…");
 }
 static bool IsGameFolder(string path)=>Directory.Exists(Path.Combine(path,"erb"))&&Directory.Exists(Path.Combine(path,"csv"));
 /// <summary>直接遍历真实文件系统：不解 SAF、不复制，几 GB 的合集也只是一次目录枚举。</summary>
 void ScanDirect(string path,int depth)
 {
  if(depth>10)return;
  if(IsGameFolder(path)){RegisterDirect(path);return;}
  string[] dirs;
  try{dirs=Directory.GetDirectories(path);}catch(Exception e){Android.Util.Log.Warn("EraFlow","读取目录失败 "+path+"："+e.Message);return;}
  foreach(var dir in dirs)
  {
   if(Path.GetFileName(dir).StartsWith('.'))continue;
   ScanDirect(dir,depth+1);
  }
 }
 /// <summary>直读要求目录可读且可写（存档要写回原目录）；只读的 SD 卡会退回复制模式。</summary>
 static bool CanUseDirect(string path)
 {
  try
  {
   using var e=Directory.EnumerateDirectories(path).GetEnumerator();e.MoveNext();
   string probe=Path.Combine(path,".eraflow-probe-"+Guid.NewGuid().ToString("N")[..8]);
   using(File.Create(probe)){}
   File.Delete(probe);
   return true;
  }
  catch{return false;}
 }
 void RegisterDirect(string path)
 {
  string full=Path.GetFullPath(path),source="path:"+full;
  if(library.Games.Any(g=>g.Source==source))return;
  Progress("正在登记 · "+Path.GetFileName(full));
  library.Register(full,source,Path.GetFileName(full));
  library.Save();
 }
 /// <summary>把 SAF 的 tree document id 还原成真实路径；拿不到就返回 null，调用方回退到复制模式。</summary>
 static string? TreeToPath(Uri tree)
 {
  string docId;
  try{docId=DocumentsContract.GetTreeDocumentId(tree);}catch{return null;}
  if(docId.StartsWith("raw:",StringComparison.OrdinalIgnoreCase))
  {
   string raw=docId[4..];
   return Directory.Exists(raw)?Path.GetFullPath(raw):null;
  }
  int colon=docId.IndexOf(':');
  if(colon<0)return null;
  string volume=docId[..colon],rel=docId[(colon+1)..];
  string? root=volume.Equals("primary",StringComparison.OrdinalIgnoreCase)
   ? Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
   : "/storage/"+volume;
  if(string.IsNullOrEmpty(root))return null;
  string rootFull=Path.GetFullPath(root);
  string full=Path.GetFullPath(Path.Combine(rootFull,rel.Replace('/',Path.DirectorySeparatorChar)));
  if(!full.Equals(rootFull,StringComparison.OrdinalIgnoreCase) && !full.StartsWith(rootFull+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return null;
  return Directory.Exists(full)?full:null;
 }
 bool HasAllFilesAccess()
 {
  // Android 11+ 用「所有文件访问」；10 及以下用普通读写权限。
  if(OperatingSystem.IsAndroidVersionAtLeast(30))return Android.OS.Environment.IsExternalStorageManager;
  return CheckSelfPermission("android.permission.READ_EXTERNAL_STORAGE")==Permission.Granted && CheckSelfPermission("android.permission.WRITE_EXTERNAL_STORAGE")==Permission.Granted;
 }
 void RequestAllFilesAccess()
 {
  if(OperatingSystem.IsAndroidVersionAtLeast(30))
  {
   try{StartActivity(new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,Uri.Parse("package:"+PackageName)));}
   catch{StartActivity(new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission));}
  }
  else RequestPermissions(new[]{"android.permission.READ_EXTERNAL_STORAGE","android.permission.WRITE_EXTERNAL_STORAGE"},9);
 }
 public override void OnRequestPermissionsResult(int requestCode,string[] permissions,Permission[] grantResults)
 {
  base.OnRequestPermissionsResult(requestCode,permissions,grantResults);
  if(requestCode==9 && pendingDirectPath!=null && HasAllFilesAccess())
  {var p=pendingDirectPath;pendingDirectPath=null;_=ScanDirectAsync(p);}
 }
 record Document(string Id,string Name,bool Directory,long Size);
 List<Document> Children(Uri tree,string id)
 {
  var uri=DocumentsContract.BuildChildDocumentsUriUsingTree(tree,id);using var cursor=ContentResolver!.Query(uri,new[]{DocumentsContract.Document.ColumnDocumentId,DocumentsContract.Document.ColumnDisplayName,DocumentsContract.Document.ColumnMimeType,DocumentsContract.Document.ColumnSize},null,null,null);
  if(cursor==null)throw new IOException("无法读取目录，请重新选择。");var list=new List<Document>();while(cursor.MoveToNext())list.Add(new(cursor.GetString(0)!,cursor.GetString(1)??"未命名",cursor.GetString(2)==DocumentsContract.Document.MimeTypeDir,cursor.IsNull(3)?0:cursor.GetLong(3)));return list;
 }
 /// <summary>erb / csv 子目录同时存在，才认作一个 Era 游戏目录。</summary>
 static bool IsGame(List<Document> children)=>children.Any(d=>d.Directory&&d.Name.Equals("erb",StringComparison.OrdinalIgnoreCase))&&children.Any(d=>d.Directory&&d.Name.Equals("csv",StringComparison.OrdinalIgnoreCase));
 /// <summary>子项 id/名字的指纹。没变化就不必再往这层目录里走。</summary>
 static string Fingerprint(List<Document> children)
 {
  using var sha=SHA256.Create();
  string text=string.Join("\n",children.OrderBy(c=>c.Id,StringComparer.Ordinal).Select(c=>(c.Directory?"D:":"F:")+c.Id+":"+c.Name));
  return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))[..16];
 }
 void ScanTree(Uri tree,string id,string name,int depth)
 {
  if(depth>10)return;
  string source=tree+"|"+id;
  // 取り込み済みのゲームは、子要素を問い合わせる前に打ち切る。
  // ここを素通りさせると、ゲームごとに数千ファイルを SAF 越しに数えることになる。
  if(library.Games.Any(g=>g.Source==source))return;
  var children=Children(tree,id);
  if(IsGame(children)){Progress("正在导入 · "+name);ImportGame(tree,id,name);return;}
  // 一覧が前回と同じなら配下は見ない。追加・削除があれば親の一覧が変わるので取りこぼさない。
  string fingerprint=Fingerprint(children);
  if(scanIndex.TryGetValue(source,out var seen)&&seen==fingerprint)return;
  foreach(var child in children.Where(c=>c.Directory&&!c.Name.StartsWith('.')))ScanTree(tree,child.Id,child.Name,depth+1);
  scanIndex[source]=fingerprint;
 }
 void ImportGame(Uri tree,string id,string name)
 {
  string source=tree+"|"+id,key=GameLibrary.Key(source),stage=Path.Combine(library.Root,".stage-"+key),final=Path.Combine(library.Root,"dir-"+key);
  library.DeleteInside(stage);Directory.CreateDirectory(stage);
  try
  {
   long bytes=0;int files=0,scripts=0,images=0;
   CopyTree(tree,id,stage,ref bytes,ref files,ref scripts,ref images,0);
   if(Directory.Exists(final))library.DeleteInside(stage); // 中断した前回の展開結果をそのまま使う
   else Directory.Move(stage,final);
   library.Register(final,source,name,scripts,images,bytes);library.Save();
  }
  finally{if(Directory.Exists(stage))library.DeleteInside(stage);}
 }
 static readonly HashSet<string> ImageExt=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".webp",".gif",".bmp"};
 void CopyTree(Uri tree,string id,string dest,ref long bytes,ref int files,ref int scripts,ref int images,int depth)
 {
  if(depth>32)throw new IOException("目录层级过深。");
  foreach(var child in Children(tree,id))
  {
   if(child.Name.Contains('/')||child.Name.Contains('\\')||child.Name is "." or "..")throw new IOException("文件名无效。");string path=GameLibrary.SafePath(dest,child.Name);
   if(child.Directory){Directory.CreateDirectory(path);CopyTree(tree,child.Id,path,ref bytes,ref files,ref scripts,ref images,depth+1);}
   else
   {
    if(++files>200000)throw new IOException("文件数量过多。");
    string ext=Path.GetExtension(child.Name);
    if(ext.Equals(".erb",StringComparison.OrdinalIgnoreCase))scripts++;
    else if(ImageExt.Contains(ext))images++;
    using var input=ContentResolver!.OpenInputStream(DocumentsContract.BuildDocumentUriUsingTree(tree,child.Id)!)!;using var output=File.Create(path);byte[] buffer=new byte[65536];int read;while((read=input.Read(buffer))>0){bytes+=read;if(bytes>8L*1024*1024*1024)throw new IOException("游戏超过 8 GB。");output.Write(buffer,0,read);}if(files%100==0)Progress($"正在导入 · {files} 个文件");
   }
  }
 }
 void LoadScanIndex(){try{string path=Path.Combine(library.Root,"scan-index.json");if(File.Exists(path))scanIndex=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new();}catch{scanIndex=new();}}
 void SaveScanIndex(){try{string path=Path.Combine(library.Root,"scan-index.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(scanIndex));File.Move(path+".tmp",path,true);}catch{}}
 void ShowAbout()=>new AlertDialog.Builder(this).SetTitle("era for Android 1.1.1").SetMessage("为文字世界留一块安静的屏幕。\n\n基于 Emuera.EM+EE 与 andEmuera 的修改版本。支持 ERB、ERH、CSV 和游戏图片资源。\n\n引擎原作者：MinorShift、妊）|дﾟ)の中の人、VVII、EvilMask、Enter。Android 兼容移植：BluishCat。\n\n游戏及存档保存在本机；卸载应用前请导出存档。游玩中点右上角按钮打开键盘或设置。\n\n本应用免费、源码公开，禁止商业倒卖；若你是付费获得，说明卖家在倒卖本应用。\n\n官方仓库：https://github.com/ArtifactYu/era-for-Android\n\n特别感谢：@RikaMundus（X）\n\n完整许可证随安装包和源码提供。").SetPositiveButton("知道了",(_,_)=>{}).Show();
 public override void OnBackPressed(){if(busy){Message("正在处理文件，请稍候");return;}web.EvaluateJavascript("window.Era?.back()",null);}
 protected override void OnDestroy(){web?.RemoveJavascriptInterface("Android");web?.Destroy();base.OnDestroy();}
}
